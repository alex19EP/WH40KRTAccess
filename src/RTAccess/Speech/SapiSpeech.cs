namespace RTAccess.Speech;

/// <summary>
/// Windows SAPI 5 text-to-speech, driven directly over COM through <see cref="ComDispatch"/> (manual
/// IDispatch — Unity's Mono implements neither System.Speech's registry internals nor managed COM
/// activation). This is the <b>fallback</b> backend: it lets the mod still talk for players who have no
/// screen reader running (Prism's <c>acquire_best</c> returns nothing without NVDA/JAWS), instead of
/// going silent. Prism stays preferred when present; <see cref="Speaker"/> picks the roster order.
///
/// Adapted from WrathAccess's <c>SapiHandler</c>, reduced to RT's simpler <see cref="ISpeech"/> contract:
/// the settings-driven param machinery and the render-to-PCM path (for spatial speech) are dropped for
/// now. Rate/volume/voice use sensible defaults exposed as static knobs a future settings screen can set.
/// </summary>
internal sealed class SapiSpeech : ISpeech
{
    // SpeechVoiceSpeakFlags
    private const int SVSFlagsAsync = 1;
    private const int SVSFPurgeBeforeSpeak = 2;

    /// <summary>Speaking rate, SAPI scale -10..10 (0 = normal). Default a touch quick, common for TTS users.</summary>
    public static int Rate = 1;
    /// <summary>Volume 0..100.</summary>
    public static int Volume = 100;
    /// <summary>Optional case-insensitive substring of the desired voice's description (e.g. "Zira",
    /// "David"). Null/empty = the system default voice. Applied lazily, only when it changes.</summary>
    public static string PreferredVoice = null;

    private ComDispatch _voice;
    private string _appliedVoice = "\0"; // sentinel: never matches, so the first Apply selects

    public string Name => "SAPI 5";

    private SapiSpeech(ComDispatch voice) { _voice = voice; }

    /// <summary>Probe + create the SAPI voice. Returns null (no throw) if SAPI isn't usable, so
    /// <see cref="Speaker"/> can fall through to the next backend in the roster.</summary>
    public static SapiSpeech TryCreate()
    {
        try
        {
            var voice = ComDispatch.Create("SAPI.SpVoice");
            if (voice == null)
            {
                Main.Log?.Log("SAPI: SpVoice not available — skipping SAPI backend.");
                return null;
            }
            Main.Log?.Log("SAPI: voice acquired (manual COM).");
            return new SapiSpeech(voice);
        }
        catch (Exception e)
        {
            Main.Log?.Log("SAPI: init failed — " + e.Message);
            return null;
        }
    }

    public void Speak(string text, bool interrupt = false)
    {
        if (_voice == null || string.IsNullOrEmpty(text)) return;
        try
        {
            Apply();
            _voice.Call("Speak", text, interrupt ? SVSFlagsAsync | SVSFPurgeBeforeSpeak : SVSFlagsAsync);
        }
        catch (Exception e) { Main.Log?.Log("SAPI speak failed: " + e.Message); }
    }

    public void Stop()
    {
        if (_voice == null) return;
        // The standard SAPI "stop": purge the queue with an empty async utterance.
        try { _voice.Call("Speak", string.Empty, SVSFlagsAsync | SVSFPurgeBeforeSpeak); } catch { }
    }

    public void Dispose()
    {
        _voice?.Dispose();
        _voice = null;
    }

    // Rate/volume are cheap (set every call); voice selection enumerates the registry, so it's skipped
    // unless the requested voice changed.
    private void Apply()
    {
        try { _voice.Set("Rate", Rate); } catch { }
        try { _voice.Set("Volume", Volume); } catch { }
        var want = PreferredVoice ?? "";
        if (want != _appliedVoice)
        {
            if (!string.IsNullOrEmpty(want))
            {
                try { SelectVoice(_voice, want); } catch (Exception e) { Main.Log?.Log("SAPI voice select failed: " + e.Message); }
            }
            _appliedVoice = want;
        }
    }

    // Pick the first installed voice whose description contains `match` (case-insensitive).
    private static void SelectVoice(ComDispatch voice, string match)
    {
        var tokens = (ComDispatch)voice.Call("GetVoices", string.Empty, string.Empty);
        if (tokens == null) return;
        try
        {
            int count = Convert.ToInt32(tokens.Get("Count"));
            for (int i = 0; i < count; i++)
            {
                var token = (ComDispatch)tokens.Call("Item", i);
                if (token == null) continue;
                try
                {
                    var desc = token.Call("GetDescription", 0) as string;
                    if (!string.IsNullOrEmpty(desc) && desc.IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        voice.SetRef("Voice", token); // putref — SAPI object-valued property
                        return;
                    }
                }
                finally { token.Dispose(); }
            }
        }
        finally { tokens.Dispose(); }
    }
}
