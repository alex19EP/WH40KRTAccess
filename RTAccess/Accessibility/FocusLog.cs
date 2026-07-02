using System.IO;
using System.Text;

namespace RTAccess.Accessibility;

/// <summary>
/// Writes screen-transition markers to focus_log.txt in the mod folder (via <see cref="Mark"/>), so an offline
/// review of the audio session can tell which screen each stretch belongs to. The per-focus-element recording
/// this log also used to carry was part of the console-focus ride and was removed with it.
/// </summary>
internal static class FocusLog
{
    private static string _path;

    public static void Init(string modDir)
    {
        _path = Path.Combine(modDir ?? ".", "focus_log.txt");
        Reset();
    }

    public static void Reset()
    {
        if (_path == null) return;
        try { File.WriteAllText(_path, "# RTAccess focus log\n", Encoding.UTF8); } catch { }
    }

    public static void Mark(string label)
    {
        if (_path == null) return;
        try { File.AppendAllText(_path, $"\n===== {label} =====\n", Encoding.UTF8); } catch { }
    }
}
