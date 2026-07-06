#if DEBUG
using System.Collections.Concurrent;
using RTAccess.Speech;

namespace RTAccess.Dev;

/// <summary>
/// Dev-only in-process driver, gated behind the RTACCESS_DEV env var (or a marker file). Exposes a
/// loopback HTTP server so an external driver (Claude, curl) can introspect and drive the live mod/game:
///   POST /eval           body = C# source, run against the live game (REPL state persists across
///                        calls); returns captured output + result/errors.
///   GET  /speech?since=N lines the mod has spoken since cursor N (we can't hear the TTS, so this is
///                        how we observe it). Tapped at the <see cref="Speaker"/> chokepoint.
///   GET  /screenshot     capture the framebuffer to a PNG (works unfocused); returns its path.
///   POST /loadsave       load a save from the title screen and block until in-play.
///   GET  /health         liveness.
///   GET  /gui POST /input land in Phase 2 (need the parallel Screen/Navigator tree).
///   Game cheat/console surface — mirrored in-process by <see cref="GameConsole"/> (no CheatsEnabled
///   needed): POST /cheat, /command, /external, /getvariable, /setvariable, /autocomplete, /dumpstate;
///   GET /known, /bindings, /status; GET|POST /log.
///
/// Eval runs on the Unity main thread: HTTP requests enqueue a job and block until <see cref="Pump"/>
/// (called once per frame from Main.OnUpdate) executes it. /speech reads a thread-safe buffer directly
/// off the HTTP thread.
///
/// This whole subsystem is compiled only in DEBUG (#if DEBUG) — a Release build has none of it, so it
/// cannot be toggled on by anything. Even in Debug it stays inert unless RTACCESS_DEV=1 or the marker
/// file is present.
/// </summary>
internal sealed class DevServer
{
    public static readonly DevServer Instance = new DevServer();

    public const string EnableEnv = "RTACCESS_DEV";
    public const string PortEnv = "RTACCESS_DEV_PORT";
    public const string MarkerFile = "devserver.enable"; // under persistentDataPath/RTAccess/
    private const int DefaultPort = 8772; // WrathAccess uses 8771; keep ours distinct.

    // Enabled by the env var OR a marker file the dev launcher drops. The marker is immune to HOW the
    // game is launched: a Steam relaunch spawns a fresh process that doesn't inherit our $env: var
    // (observed in WrathAccess — the server returned early at the env gate while the mod itself loaded
    // fine), whereas the file is read from persistentDataPath regardless. Still DEBUG-only, so neither
    // exists in Release.
    private static bool DevEnabled(out string how)
    {
        how = null;
        if (Environment.GetEnvironmentVariable(EnableEnv) == "1") { how = "env"; return true; }
        try
        {
            string marker = Path.Combine(
                UnityEngine.Application.persistentDataPath, "RTAccess", MarkerFile);
            if (File.Exists(marker)) { how = "marker"; return true; }
        }
        catch { }
        return false;
    }

    /// <summary>Whether the dev gate is open (RTACCESS_DEV=1 or the marker file). Lets other DEBUG-only
    /// behavior (e.g. <see cref="SkipSplashPatch"/>) share this gate without duplicating env/marker checks.</summary>
    internal static bool IsEnabled => DevEnabled(out _);

    private sealed class Job
    {
        public Func<string> Work;
        public string Result = "";
        public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
    }

    private readonly SpeechTap _speech = new SpeechTap();
    private readonly CSharpEvaluator _evaluator = new CSharpEvaluator();
    private readonly ConcurrentQueue<Job> _jobs = new ConcurrentQueue<Job>();
    private DevHttpServer _http;
    private bool _enabled;

    /// <summary>Stand up the server if RTACCESS_DEV=1 (or the marker is present); otherwise stay inert.</summary>
    public void Start()
    {
        string how;
        if (!DevEnabled(out how)) return;

        // Keep the Unity player loop (and thus our main-thread Pump, and thus /eval) running while the
        // game is unfocused — otherwise the loop freezes the moment our terminal takes focus and eval
        // jobs never execute. The game pauses LOGIC on focus loss but not the loop, and nothing in the
        // game writes runInBackground, so setting it true here (during focused boot, before any focus
        // loss) and re-asserting each Pump holds. DEBUG/dev-only behavior.
        UnityEngine.Application.runInBackground = true;

        int port = DefaultPort;
        string p = Environment.GetEnvironmentVariable(PortEnv);
        if (!string.IsNullOrEmpty(p)) int.TryParse(p, out port);

        // Tap every string the mod speaks through the Speaker chokepoint into the ring buffer.
        Speaker.Observer = _speech.Add;

        try
        {
            _http = new DevHttpServer(port, HandleRequest);
            _http.Start();
            _enabled = true;
            Main.Log?.Log("Dev server on http://127.0.0.1:" + port + " (gate: " + how + "; POST /eval, GET /speech, POST /cheat, POST /dumpstate, GET /known)");
        }
        catch (Exception e)
        {
            Main.Log?.Error("Dev server failed to start: " + e);
        }
    }

    /// <summary>Run queued main-thread jobs. Call once per frame from the tick.</summary>
    public void Pump()
    {
        if (!_enabled) return;
        UnityEngine.Application.runInBackground = true; // re-assert each frame (cheap insurance vs any reset)
        Job job;
        while (_jobs.TryDequeue(out job))
        {
            try { job.Result = job.Work() ?? ""; }
            catch (Exception e) { job.Result = "[host error] " + e + "\n"; }
            job.Done.Set();
        }
    }

    /// <summary>Run <paramref name="work"/> on the main thread (next Pump) and block for its result.</summary>
    private string OnMainThread(Func<string> work, int timeoutSeconds = 30)
    {
        var job = new Job { Work = work };
        _jobs.Enqueue(job);
        if (!job.Done.Wait(TimeSpan.FromSeconds(timeoutSeconds)))
            return "[timeout] main thread did not run the job within " + timeoutSeconds + "s (frozen / not pumping?)\n";
        return job.Result;
    }

    // Runs on the HTTP thread.
    private string HandleRequest(string method, string path, string body)
    {
        string route = path;
        string query = "";
        int q = path.IndexOf('?');
        if (q >= 0) { route = path.Substring(0, q); query = path.Substring(q + 1); }

        if (route == "/eval" && method == "POST")
        {
            if (string.IsNullOrWhiteSpace(body)) return "[empty] POST C# source as the request body\n";
            return OnMainThread(() => _evaluator.Eval(body));
        }

        if (route == "/gui" && method == "GET")
            return OnMainThread(() => GuiInspector.Dump());

        if (route == "/input" && method == "POST")
        {
            string verb = (body ?? "").Trim();
            return OnMainThread(() => Inject(verb));
        }

        if (route == "/screenshot" && method == "GET")
            return Screenshot();

        if (route == "/loadsave" && method == "POST")
            return LoadSave(body);

        if (route == "/speech" && method == "GET")
        {
            long since = 0;
            foreach (string kv in query.Split('&'))
                if (kv.StartsWith("since=", StringComparison.Ordinal))
                    long.TryParse(kv.Substring("since=".Length), out since);
            long next;
            string lines = _speech.Render(since, out next);
            return "cursor: " + next + "\n" + lines;
        }

        if (route == "/health" || route == "/") return "ok\n";

        // Game cheat/console surface mirrored in-process (POST /cheat, POST /dumpstate, GET /known, …).
        // See GameConsole. OnMainThread has an optional arg, so wrap it in a lambda to match the delegate.
        if (GameConsole.TryHandle(route, method, body, work => OnMainThread(work), out string gc))
            return gc;

        return "[404] " + method + " " + route + "\n";
    }

    // Fire one of our InputActions by key, exactly as InputManager.Tick routes a real press: a UI action
    // goes to the navigator; anything else fires its handler. Lets the dev driver drive nav (ui.down,
    // ui.activate, …) and global hotkeys without physical keys (mode-independent). Unknown key → list
    // what's available. Main-thread only.
    private static string Inject(string key)
    {
        foreach (var a in RTAccess.Input.InputManager.Actions)
        {
            if (a.Key != key) continue;
            bool consumed = a.Category == RTAccess.Input.InputCategory.UI
                && RTAccess.UI.Navigation.DispatchJustPressed(a);
            if (!consumed) a.InvokePerformed();
            return "fired " + key + (consumed ? " (navigator)" : " (handler)") + "\n";
        }
        var sb = new System.Text.StringBuilder("[unknown action] " + key + "\navailable:\n");
        foreach (var a in RTAccess.Input.InputManager.Actions) sb.Append("  ").Append(a.Key).Append('\n');
        return sb.ToString();
    }

    // Load a save from the main menu and BLOCK until the gameplay scene is interactive, so the driver
    // can script "drop me in-game" in one call. body = "latest" (default) | "quick" | an index into the
    // save list. Drives MainMenuVM.EnterGame(() => Game.LoadGameFromMainMenu(save)) — the real
    // Continue-button path, which shows the loading screen, tears down the menu + loads the obligatory
    // scenes before running the load — then polls for loading to finish + an area to be loaded. We drive
    // nav via /eval (and /input in Phase 2), so we don't need the game's keyboard focus. Save metadata
    // loads async at the title screen, so a too-early call returns a retryable "[not ready]"/"[no save]".
    private string LoadSave(string body)
    {
        string sel = (body ?? "").Trim();
        if (sel.Length == 0) sel = "latest";

        string kick = OnMainThread(() =>
        {
            var game = Kingmaker.Game.Instance;
            if (game == null || game.SaveManager == null) return "[not ready] no SaveManager yet; retry\n";
            // Must be idle at the title screen. The server answers /health at the GameStarter entry point
            // (before the menu exists), and loading mid-boot half-initializes the game.
            var lp = Kingmaker.EntitySystem.Persistence.LoadingProcess.Instance;
            if (lp == null || lp.IsLoadingScreenActive || lp.IsLoadingInProcess)
                return "[not ready] still on a loading screen; retry\n";
            var mm = Kingmaker.Code.UI.MVVM.VM.MainMenu.MainMenuUI.Instance;
            if (mm == null) return "[not ready] not at the main menu (load only from the title screen); retry\n";
            game.SaveManager.UpdateSaveListIfNeeded();
            var save = ResolveSave(game.SaveManager, sel);
            if (save == null) return "[no save] '" + sel + "' not found (saves still loading? retry)\n";
            // Drive the real Continue-button path. EnterGame shows the loading screen, tears down the menu
            // + loads the obligatory scenes, THEN runs our action (LoadGameFromMainMenu). Calling
            // LoadGameFromMainMenu directly skips that transition and leaves a broken half-load.
            mm.EnterGame(() => game.LoadGameFromMainMenu(save));
            return "ok\n";
        });
        if (kick != "ok\n") return kick;

        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (timer.Elapsed.TotalSeconds < 90)
        {
            string status = OnMainThread(() =>
            {
                var game = Kingmaker.Game.Instance;
                var lp = Kingmaker.EntitySystem.Persistence.LoadingProcess.Instance;
                if (lp == null || lp.IsLoadingScreenActive || lp.IsLoadingInProcess) return "";
                // An area being loaded is our "interactive" signal; at the menu it's null, so we can't
                // falsely return before the load even starts.
                bool inPlay = game != null && game.CurrentlyLoadedArea != null;
                return inPlay ? "loaded '" + sel + "': area=" + game.CurrentlyLoadedArea.name + "\n" : "";
            });
            if (status.Length > 0) return status;
            Thread.Sleep(150);
        }
        return "[timeout] load '" + sel + "' did not become interactive within 90s\n";
    }

    private static Kingmaker.EntitySystem.Persistence.SaveInfo ResolveSave(
        Kingmaker.EntitySystem.Persistence.SaveManager mgr, string sel)
    {
        if (sel == "latest") return mgr.GetAnyLatestSave();
        if (sel == "quick") return mgr.GetNewestQuickslot();
        if (int.TryParse(sel, out int idx))
        {
            int i = 0;
            foreach (var s in mgr) if (i++ == idx) return s;
            return null;
        }
        return mgr.GetAnyLatestSave();
    }

    // Capture the game framebuffer to a PNG (works unfocused) and return its path for the driver to Read.
    // ScreenCapture writes asynchronously over the next frame(s): trigger on the main thread, then wait
    // here (HTTP thread) for the file to appear and its size to settle.
    private string Screenshot()
    {
        string path = Path.Combine(Path.GetTempPath(), "rt_shot.png");
        OnMainThread(() =>
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
            UnityEngine.ScreenCapture.CaptureScreenshot(path);
            return "requested";
        });

        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (timer.Elapsed.TotalSeconds < 8)
        {
            try
            {
                if (File.Exists(path))
                {
                    long size = new FileInfo(path).Length;
                    if (size > 0)
                    {
                        Thread.Sleep(60); // let the write settle, then confirm the size is stable
                        if (new FileInfo(path).Length == size) return path + "\n";
                    }
                }
            }
            catch { }
            Thread.Sleep(50);
        }
        return "[timeout] screenshot not written within 8s\n";
    }
}
#endif
