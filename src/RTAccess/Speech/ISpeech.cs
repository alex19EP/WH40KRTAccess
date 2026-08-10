namespace RTAccess.Speech;

/// <summary>
/// A swappable text-to-speech sink. Implementations are called from the Unity main thread and
/// must not block it (queue/async internally if a backend is slow).
/// </summary>
public interface ISpeech : IDisposable
{
    /// <summary>Human-readable name of the active backend, for logging.</summary>
    string Name { get; }

    /// <summary>Speak <paramref name="text"/>. When <paramref name="interrupt"/> is true, cancel current speech first.</summary>
    void Speak(string text, bool interrupt = false);

    /// <summary>Cancel any in-progress speech.</summary>
    void Stop();
}
