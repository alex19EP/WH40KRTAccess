using System.IO;
using System.Reflection;

namespace RTAccess.Audio
{
    /// <summary>Where the bundled audio stems live: <c>&lt;mod root&gt;/assets/audio</c> — wall-tone sets under
    /// <c>walltones/&lt;set&gt;/{north,south,east,west}.wav</c>, sonar interactable stems under
    /// <c>interactables/*.wav</c>, and cue earcons at the root. Sourced from WrathAccess. Mirrors WA's
    /// <c>OverlayAudio.Dir</c>; falls back to the DLL's own folder for a loose copy.</summary>
    internal static class AudioAssets
    {
        public static string Dir =>
            Path.Combine(Main.ModDir ?? Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "assets", "audio");

        /// <summary>A wall-tone set directory (<c>walltones/&lt;set&gt;</c>).</summary>
        public static string WallToneSet(string set) => Path.Combine(Dir, "walltones", set);

        /// <summary>An interactable sonar stem (<c>interactables/&lt;stem&gt;.wav</c>).</summary>
        public static string Interactable(string stem) => Path.Combine(Dir, "interactables", stem + ".wav");

        /// <summary>A root-level cue earcon (<c>&lt;name&gt;.wav</c>).</summary>
        public static string Cue(string name) => Path.Combine(Dir, name + ".wav");
    }
}
