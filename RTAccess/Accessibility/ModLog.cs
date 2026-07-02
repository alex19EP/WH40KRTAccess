using System.Text;
using UnityModManagerNet;

namespace RTAccess.Accessibility;

/// <summary>
/// Mirrors the mod's log output to rtaccess_log.txt in the mod folder so errors/warnings survive to disk
/// for offline review. UnityModManager keeps no on-disk log in this install and Player.log carries nothing
/// from <see cref="UnityModManager.ModEntry.ModLogger"/>, so a bare <c>Main.Log</c> left crashes invisible.
/// Every call still forwards to the wrapped ModLogger (the in-game console / UMM UI is unchanged); it just
/// also appends a timestamped, level-tagged line to disk. The method surface (Log/Error/Warning) matches the
/// subset of ModLogger the mod actually uses, so this drops in wherever <c>Main.Log</c> was.
/// </summary>
internal sealed class ModLog
{
    private readonly UnityModManager.ModEntry.ModLogger _inner;
    private readonly string _path;

    public ModLog(UnityModManager.ModEntry.ModLogger inner, string modDir)
    {
        _inner = inner;
        _path = Path.Combine(modDir ?? ".", "rtaccess_log.txt");
        try { File.WriteAllText(_path, "# RTAccess log\n", Encoding.UTF8); } catch { }
    }

    public void Log(string message) { _inner?.Log(message); Write("LOG", message); }
    public void Error(string message) { _inner?.Error(message); Write("ERROR", message); }
    public void Warning(string message) { _inner?.Warning(message); Write("WARN", message); }

    private void Write(string level, string message)
    {
        if (_path == null) return;
        try
        {
            var stamp = DateTime.Now.ToString("HH:mm:ss.fff");
            File.AppendAllText(_path, "[" + stamp + "] [" + level + "] " + (message ?? "") + "\n", Encoding.UTF8);
        }
        catch { }
    }
}
