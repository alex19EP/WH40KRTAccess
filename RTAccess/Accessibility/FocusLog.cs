using System.IO;
using System.Text;

namespace RTAccess.Accessibility;

/// <summary>
/// Records the sequence of focused elements to focus_log.txt in the mod folder, so the console focus
/// order/coverage can be reviewed offline alongside the live audio. Each line: seq, source type, text
/// (or &lt;no text&gt; to flag a coverage gap). Use <see cref="Mark"/> to label which screen you're on.
/// </summary>
internal static class FocusLog
{
    private static string _path;
    private static int _seq;

    public static void Init(string modDir)
    {
        _path = Path.Combine(modDir ?? ".", "focus_log.txt");
        Reset();
    }

    public static void Reset()
    {
        _seq = 0;
        if (_path == null) return;
        try { File.WriteAllText(_path, "# RTAccess focus log\n", Encoding.UTF8); } catch { }
    }

    public static void Write(FocusReading r, bool navigated)
    {
        if (_path == null) return;
        _seq++;
        var tag = navigated ? "" : "(reread) ";
        var line = $"{_seq,4}  {tag}[{r.Source}]  {(r.HasText ? r.Text : "<no text>")}\n";
        try { File.AppendAllText(_path, line, Encoding.UTF8); } catch { }
    }

    public static void Mark(string label)
    {
        if (_path == null) return;
        try { File.AppendAllText(_path, $"\n===== {label} =====\n", Encoding.UTF8); } catch { }
    }
}
