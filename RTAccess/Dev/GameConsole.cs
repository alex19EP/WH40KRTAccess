#if DEBUG
using Core.Cheats;
using Core.Console;
using Kingmaker.GameCommands.Cheats;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SC = Core.StateCrawler.StateCrawler;

namespace RTAccess.Dev;

/// <summary>
/// DEBUG-only: mirrors the game's own (retail-gated) cheat/console REST surface onto our loopback dev
/// server, driven entirely IN-PROCESS so it needs neither the game's server nor <c>CheatsEnabled</c> /
/// <c>startup.json</c>. The game builds <see cref="CheatsManagerHolder.System"/> unconditionally at boot
/// (SubsystemRegistration) and registers <see cref="ConsoleLogSink"/> as a log sink regardless of build
/// mode — so we just call the same entry points its REST plugins wrap (decompiled reference:
/// <c>Core.Cheats/…/ServerPlugins/*</c>, <c>Core.StateCrawler</c>, <c>Kingmaker.Logging.CheatGameCommandSystem</c>).
/// Unlike the game's server (binds <c>http://*:35555</c>, zero auth), ours stays loopback-only + marker-gated.
///
/// Endpoints (paths match the game's plugin <c>LocalPath</c>s, so scripts written against Owlcat's remote
/// console speak to ours unchanged):
///   POST /cheat        body = a raw command LINE ("local_teleport @cursor", "checks_success") — the
///                      ergonomic entry; routes through <c>Parser.Execute</c> (arg preprocessing +
///                      @cursor/@mouseover/@selectedUnits) and covers command/external/get/set in one.
///   POST /command      JSON {CommandName, Args[]}                run a pre-parsed cheat command.
///   POST /external     JSON {ExternalName, ExternalNameWithArgs} (externals are dev-registered; usually 404 in retail).
///   POST /getvariable  JSON {VariableName}                       (result is logged — read it back via /log)
///   POST /setvariable  JSON {VariableName, VariableValue}
///   POST /autocomplete body = a piece of a command    → JSON string[] of completions.
///   GET  /known        the whole cheat DB (name → params/description) as JSON — the command palette.
///   GET  /bindings     <c>CheatBindings.ActiveBindings</c> as JSON.
///   POST /dumpstate    body = a dotted path ("Game.Instance.Player.PartyCharacters") OR JSON
///                      {RootObjectPath, ExpandedChildren[]} → <c>StateCrawler</c> object-graph JSON tree.
///   GET|POST /log      drain the game's "Console" log sink (cheat output + errors) as JSON.
///   GET  /status       process / current-area status.
///
/// Command execution is FIRE-AND-FORGET, exactly like the game's own <c>CheatsHelper.Run</c>: the command
/// enqueues onto <c>Game.Instance.GameCommandQueue</c> and runs over subsequent frames. We must NOT block
/// the main thread waiting on the returned Task — that would stall the very queue that runs it — so we
/// return "queued" immediately and surface any fault through <see cref="Main.Log"/> + /log.
/// </summary>
internal static class GameConsole
{
    private static readonly JsonSerializerSettings Json = new JsonSerializerSettings
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
    };

    /// <summary>Dispatch a game-console route. Returns false (leaving <paramref name="response"/> null) if
    /// <paramref name="route"/> is not one of ours, so the caller can fall through to its own 404.
    /// <paramref name="onMain"/> runs a job on the Unity main thread (next Pump) and returns its result.</summary>
    internal static bool TryHandle(string route, string method, string body,
        Func<Func<string>, string> onMain, out string response)
    {
        switch (route)
        {
            case "/cheat": response = Line(body, onMain); return true;
            case "/command": response = Command(body, onMain); return true;
            case "/external": response = External(body, onMain); return true;
            case "/getvariable": response = GetVar(body, onMain); return true;
            case "/setvariable": response = SetVar(body, onMain); return true;
            case "/autocomplete": response = AutoComplete(body, onMain); return true;
            case "/known": response = onMain(Known); return true;
            case "/bindings": response = onMain(Bindings); return true;
            case "/dumpstate": response = DumpState(body, onMain); return true;
            case "/log": response = Log(); return true; // ConsoleLogSink is thread-safe; no main-thread hop.
            case "/status": response = onMain(Status); return true;
            default: response = null; return false;
        }
    }

    private static string Line(string body, Func<Func<string>, string> onMain)
    {
        string line = (body ?? "").Trim();
        if (line.Length == 0) return "[empty] POST a cheat command line as the body (e.g. checks_success)\n";
        return onMain(() =>
        {
            try
            {
                Observe(CheatsManagerHolder.System.Parser.Execute(line), line);
                return "queued: " + line + "\n(watch /log for output)\n";
            }
            catch (Exception e) { return Err(e); }
        });
    }

    private static string Command(string body, Func<Func<string>, string> onMain)
    {
        JObject o = Parse(body);
        string name = (string)o?["CommandName"];
        if (string.IsNullOrEmpty(name)) return "[bad request] expected JSON {\"CommandName\":\"…\",\"Args\":[…]}\n";
        string[] args = o["Args"]?.ToObject<string[]>() ?? Array.Empty<string>();
        return onMain(() =>
        {
            try
            {
                Observe(RunCheatCommandGameCommand.Create(name, args), name);
                return "queued: " + name + "\n";
            }
            catch (Exception e) { return Err(e); }
        });
    }

    private static string External(string body, Func<Func<string>, string> onMain)
    {
        JObject o = Parse(body);
        string name = (string)o?["ExternalName"];
        if (string.IsNullOrEmpty(name)) return "[bad request] expected JSON {\"ExternalName\":\"…\",\"ExternalNameWithArgs\":\"…\"}\n";
        string full = (string)o["ExternalNameWithArgs"] ?? name;
        return onMain(() =>
        {
            try
            {
                Observe(RunExternalCheatGameCommand.Create(name, full), name);
                return "queued: " + name + "\n";
            }
            catch (Exception e) { return Err(e); }
        });
    }

    private static string GetVar(string body, Func<Func<string>, string> onMain)
    {
        JObject o = Parse(body);
        string name = (string)o?["VariableName"];
        if (string.IsNullOrEmpty(name)) return "[bad request] expected JSON {\"VariableName\":\"…\"}\n";
        return onMain(() =>
        {
            try
            {
                Observe(CheatsManagerHolder.System.VariableExecutor.ExecuteGetVariableWithDefaultLogging(name), name);
                return "queued: get " + name + "\n(value is logged — read /log)\n";
            }
            catch (Exception e) { return Err(e); }
        });
    }

    private static string SetVar(string body, Func<Func<string>, string> onMain)
    {
        JObject o = Parse(body);
        string name = (string)o?["VariableName"];
        if (string.IsNullOrEmpty(name)) return "[bad request] expected JSON {\"VariableName\":\"…\",\"VariableValue\":\"…\"}\n";
        string value = (string)o["VariableValue"] ?? "";
        return onMain(() =>
        {
            try
            {
                Observe(SetCheatVariableGameCommand.Create(name, value), name);
                return "queued: set " + name + " = " + value + "\n";
            }
            catch (Exception e) { return Err(e); }
        });
    }

    private static string AutoComplete(string body, Func<Func<string>, string> onMain)
    {
        string piece = (body ?? "").Trim();
        return onMain(() => Ser(CheatsManagerHolder.System.Parser.TryAutocomplete(piece).ToArray()));
    }

    // Reads dictionaries settled at boot; no Unity API — but the main-thread hop keeps it uniform + cheap.
    private static string Known() => Ser(CheatsManagerHolder.System.Database.GetKnownObjects());

    private static string Bindings() => Ser(CheatBindings.ActiveBindings);

    private static string DumpState(string body, Func<Func<string>, string> onMain)
    {
        string b = (body ?? "").Trim();
        string path;
        System.Collections.Generic.List<SC.ExpandedChildren> expanded = null;
        if (b.StartsWith("{", StringComparison.Ordinal))
        {
            JObject o = Parse(b);
            path = (string)o?["RootObjectPath"];
            expanded = o?["ExpandedChildren"]?.ToObject<System.Collections.Generic.List<SC.ExpandedChildren>>();
        }
        else path = b;

        if (string.IsNullOrEmpty(path))
            return "[empty] POST a dotted path (Game.Instance.Player) or JSON {RootObjectPath, ExpandedChildren}\n";
        return onMain(() => Ser(SC.GetState(path, expanded)));
    }

    // Drain-on-poll, like the game's ConsolePlugin: each call returns the "Console" entries since the last.
    private static string Log()
    {
        var entries = ConsoleLogSink.Poll(Guid.Empty);
        var projected = entries.Select(e => new
        {
            e.Index,
            Time = e.Timestamp.ToString("HH:mm:ss.fff"),
            Level = e.Level.ToString(),
            e.Channel,
            e.Message,
        }).ToArray();
        return Ser(projected);
    }

    private static string Status()
    {
        var game = Kingmaker.Game.Instance;
        return Ser(new
        {
            IsEditor = UnityEngine.Application.isEditor,
            IsPlaying = UnityEngine.Application.isPlaying,
            ProcessID = System.Diagnostics.Process.GetCurrentProcess().Id,
            Area = game?.CurrentlyLoadedArea?.name,
        });
    }

    // Fire-and-forget: log a fault so it isn't swallowed (the sync path already returned "queued").
    private static void Observe(System.Threading.Tasks.Task task, string label)
    {
        task?.ContinueWith(
            t => Main.Log?.Warning("/cheat '" + label + "' faulted: " + t.Exception?.GetBaseException().Message),
            System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
    }

    private static JObject Parse(string body)
    {
        try { return string.IsNullOrWhiteSpace(body) ? null : JObject.Parse(body); }
        catch { return null; }
    }

    private static string Ser(object value) => JsonConvert.SerializeObject(value, Json) + "\n";

    private static string Err(Exception e) => "[error] " + e.GetType().Name + ": " + e.Message + "\n";
}
#endif
