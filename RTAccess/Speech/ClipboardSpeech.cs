namespace RTAccess.Speech;

/// <summary>
/// Last-resort speech sink: every announcement is copied to the system clipboard, readable with any
/// clipboard-monitoring tool. Used only when neither Prism nor SAPI is available, so the mod is never
/// fully silent. Ported from WrathAccess's <c>ClipboardHandler</c> (Unity's copy buffer). Paramless.
/// </summary>
internal sealed class ClipboardSpeech : ISpeech
{
    public string Name => "Clipboard";

    public void Speak(string text, bool interrupt = false)
    {
        if (string.IsNullOrEmpty(text)) return;
        try { UnityEngine.GUIUtility.systemCopyBuffer = text; } catch { }
    }

    public void Stop() { }
    public void Dispose() { }
}
