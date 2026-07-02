using RTAccess.Speech;

namespace RTAccess.Buffers;

/// <summary>
/// The four buffer review actions, bound to Alt+arrows (wired by the main session in InputBindings):
/// Alt+Left/Right cycle between buffers (announcing the buffer's name + its current line), Alt+Up/Down move
/// through the current buffer's lines (announcing just the line). Speech interrupts so rapid scrolling stays
/// responsive (the interrupt-on-keypress rule). Spoken strings resolve through the locale table (see Loc / ui.json).
/// </summary>
internal static class BufferControls
{
    public static void NextBuffer() { BufferManager.Instance.MoveToNext(); ReportBuffer(); }
    public static void PrevBuffer() { BufferManager.Instance.MoveToPrevious(); ReportBuffer(); }

    public static void NextItem()
    {
        var b = BufferManager.Instance.CurrentBuffer;
        b?.MoveToNext();
        ReportItem(b);
    }

    public static void PrevItem()
    {
        var b = BufferManager.Instance.CurrentBuffer;
        b?.MoveToPrevious();
        ReportItem(b);
    }

    // Switching buffers reads "<buffer>. <current line>" (or empty / none).
    private static void ReportBuffer()
    {
        var b = BufferManager.Instance.CurrentBuffer;
        if (b == null) { Speaker.Speak(Loc.T("buffer.none"), interrupt: true); return; }
        if (b.IsEmpty) { Speaker.Speak(Loc.T("buffer.empty", new { label = b.Label }), interrupt: true); return; }
        Speaker.Speak(Loc.T("buffer.label_line", new { label = b.Label, item = b.CurrentItem ?? "" }), interrupt: true);
    }

    // Moving within a buffer reads just the landed line.
    private static void ReportItem(Buffer b)
    {
        if (b == null) { Speaker.Speak(Loc.T("buffer.none_selected"), interrupt: true); return; }
        if (b.IsEmpty) { Speaker.Speak(Loc.T("buffer.empty", new { label = b.Label }), interrupt: true); return; }
        var item = b.CurrentItem;
        if (item != null) Speaker.Speak(item, interrupt: true);
    }
}
