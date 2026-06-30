using RTAccess.Speech;

namespace RTAccess
{
    /// <summary>
    /// The call-site facade the UI framework speaks through. Strips TMP rich-text (game labels are
    /// markup) then routes to <see cref="Speaker"/> (Prism / fallback backend). Speech NEVER interrupts
    /// by default — queued speech is the user's preference (carried over from WrathAccess / SayTheSpire).
    /// </summary>
    public static class Tts
    {
        public static void Speak(string text, bool interrupt = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            text = TextUtil.StripRichText(text); // game labels are TMP rich text
            if (string.IsNullOrEmpty(text)) return;
            Speaker.Speak(text, interrupt);
        }

        public static void Stop() => Speaker.Stop();
    }
}
