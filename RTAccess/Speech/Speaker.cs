using UnityModManagerNet;
using RTAccess.Accessibility;

namespace RTAccess.Speech;

/// <summary>
/// Process-wide speech facade over an ordered backend roster (<see cref="PrismSpeech"/> →
/// <see cref="SapiSpeech"/> → <see cref="ClipboardSpeech"/>). The first backend that loads wins:
/// Prism is preferred (drives the user's real screen reader + braille), SAPI 5 is the fallback so the
/// mod still talks for players with no screen reader running (instead of going silent), and the
/// clipboard is the last resort. Only when none load (never, given clipboard) is speech a no-op.
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
            // Ordered roster — first usable backend wins. Each TryCreate returns null (no throw) when its
            // backend isn't available on this machine, so we fall through cleanly. Clipboard always loads,
            // so the mod is never fully silent.
            _backend = PrismSpeech.TryCreate(modEntry?.Path)
                       ?? (ISpeech)SapiSpeech.TryCreate()
                       ?? new ClipboardSpeech();
            Main.Log?.Log("Speech backend: " + ActiveBackend);
        }
    }

    public static void Speak(string text, bool interrupt = false)
    {
        // Master gate: a UMM-disabled mod goes fully silent even though its EventBus subscribers and Harmony
        // postfixes stay wired (barks/warnings/conviction/quest/service-window/settings events still fire, but
        // nothing reaches the synth). Single chokepoint every passive + keypress path routes through. See Main.Enabled.
        if (!Main.Enabled) return;
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
