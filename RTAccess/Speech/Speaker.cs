using UnityModManagerNet;
using RTAccess.Accessibility;

namespace RTAccess.Speech;

/// <summary>
/// Process-wide speech facade over the native Prism backend (<see cref="PrismSpeech"/>). If no usable
/// prism.dll / screen-reader backend is present, speech is a silent no-op (ActiveBackend is "&lt;none&gt;").
/// </summary>
public static class Speaker
{
    private static ISpeech _backend;
    private static readonly object _gate = new object();

    public static string ActiveBackend => _backend?.Name ?? "<none>";

#if DEBUG
    /// <summary>Dev-only tap: every line spoken (after normalization) is also handed here, so the dev
    /// server's /speech buffer can observe what the mod said. Wired by <c>DevServer</c>. See SpeechTap.</summary>
    public static Action<string> Observer;
#endif

    public static void Initialize(UnityModManager.ModEntry modEntry)
    {
        lock (_gate)
        {
            _backend = PrismSpeech.TryCreate(modEntry?.Path);
            Main.Log?.Log("Speech backend: " + ActiveBackend);
        }
    }

    public static void Speak(string text, bool interrupt = false)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        // Normalize line breaks to sentence breaks so the synth pauses instead of running lines together.
        text = text.Replace("\r\n", ". ").Replace("\n", ". ").Replace("\r", ". ");
        SpeechLog.Write(text, interrupt);
#if DEBUG
        try { Observer?.Invoke(text); } catch { }
#endif
        lock (_gate)
        {
            try { _backend?.Speak(text, interrupt); }
            catch (Exception e) { Main.Log?.Log("Speak failed: " + e.Message); }
        }
    }

    public static void Stop()
    {
        lock (_gate)
        {
            try { _backend?.Stop(); } catch { }
        }
    }

    public static void Shutdown()
    {
        lock (_gate)
        {
            _backend?.Dispose();
            _backend = null;
        }
    }
}
