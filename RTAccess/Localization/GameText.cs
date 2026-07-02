using System;
using Kingmaker.Blueprints.Root.Strings;   // UIStrings.CommonTexts (shared action-verb vocabulary)
using Kingmaker.Localization;

namespace RTAccess
{
    /// <summary>
    /// Prefer the GAME's own already-localized UI strings for labels/tooltips that have a canonical
    /// in-game equivalent, so they inherit Owlcat's translations for every shipped language rather than
    /// duplicating English in <c>ui.json</c>. Falls back to the mod's locale table (via
    /// <see cref="Loc.T"/>) when the game string is missing / empty or the lookup throws — early boot
    /// before the blueprint root is up, a key the game shipped blank, or a future game-version rename.
    ///
    /// <para>Every getter is a <see cref="Func{LocalizedString}"/> so the game blueprint root is
    /// dereferenced lazily at call time (when a label is read for focus), never at mod-init when it
    /// isn't loaded yet. Pair every call with a fallback key so a null game string is never spoken.</para>
    /// </summary>
    internal static class GameText
    {
        /// <summary>Current-language text of a game <see cref="LocalizedString"/>, else <see cref="Loc.T"/>(fallbackKey).</summary>
        public static string Or(Func<LocalizedString> game, string fallbackKey)
        {
            try
            {
                var s = game()?.Text;
                if (!string.IsNullOrEmpty(s)) return s;
            }
            catch (Exception e) { Main.Log?.Error("GameText.Or(" + fallbackKey + "): " + e); }
            return Loc.T(fallbackKey);
        }

        /// <summary>An <c>action.&lt;verb&gt;</c> label, preferring the game's own CommonTexts word (so it
        /// follows the game's language) for the verbs the game ships, else the mod's <c>ui.json</c>
        /// <c>action.&lt;verb&gt;</c> key. Verbs with no clean game equivalent (activate / open / toggle /
        /// choose / …) resolve straight from <c>ui.json</c> — identical to a bare <see cref="Loc.T"/>.</summary>
        public static string Action(string verb)
        {
            switch (verb)
            {
                case "select": return Or(() => UIStrings.Instance.CommonTexts.Select, "action.select");
                case "cancel": return Or(() => UIStrings.Instance.CommonTexts.Cancel, "action.cancel");
                case "increase": return Or(() => UIStrings.Instance.CommonTexts.Increase, "action.increase");
                case "decrease": return Or(() => UIStrings.Instance.CommonTexts.Decrease, "action.decrease");
                case "expand": return Or(() => UIStrings.Instance.CommonTexts.Expand, "action.expand");
                case "collapse": return Or(() => UIStrings.Instance.CommonTexts.Collapse, "action.collapse");
                default: return Loc.T("action." + verb);
            }
        }
    }
}
