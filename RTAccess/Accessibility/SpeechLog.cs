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
        // One utterance is one line — the transcript is read (and parsed) line-wise. Spoken text may itself
        // carry breaks now that tooltip bodies keep their paragraph structure (the inspect readout renders a
        // whole template into one utterance), so fold them here rather than letting one line become several.
        var flat = text.Replace("\r", "").Replace('\n', ' ');
        try { File.AppendAllText(_path, (interrupt ? "[!] " : "[+] ") + flat + "\n", Encoding.UTF8); } catch { }
    }
}
