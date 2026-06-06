using System.IO;
using System.Text;

namespace RTAccess.Accessibility;

/// <summary>
/// Chronological transcript of everything spoken, written to speech_log.txt in the mod folder so it
/// doesn't clutter the UnityModManager log. "[!]" = interrupting speech, "[+]" = queued.
/// </summary>
internal static class SpeechLog
{
    private static string _path;

    public static void Init(string modDir)
    {
        _path = Path.Combine(modDir ?? ".", "speech_log.txt");
        Reset();
    }

    public static void Reset()
    {
        if (_path == null) return;
        try { File.WriteAllText(_path, "# RTAccess speech log\n", Encoding.UTF8); } catch { }
    }

    public static void Write(string text, bool interrupt)
    {
        if (_path == null || string.IsNullOrEmpty(text)) return;
        try { File.AppendAllText(_path, (interrupt ? "[!] " : "[+] ") + text + "\n", Encoding.UTF8); } catch { }
    }
}
