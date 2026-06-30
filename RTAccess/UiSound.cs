using Kingmaker.UI.Sound;

namespace RTAccess
{
    /// <summary>
    /// Plays the game's UI sounds. Most controls' click sounds live in the view's click handler, which we
    /// bypass by driving the VM directly — so we replay them here for consistent feedback. RT routes UI
    /// sounds through <see cref="UISounds"/> (a service); the WotR <c>Game.UI.UISound</c> path doesn't exist.
    /// </summary>
    public static class UiSound
    {
        public static void Click()
        {
            try { UISounds.Instance?.PlayButtonClickSound(); } catch { }
        }

        /// <summary>The game's control-hover sound — played when our focus moves to a new element
        /// (our equivalent of a mouseover).</summary>
        public static void Hover()
        {
            try { UISounds.Instance?.PlayHoverSound(); } catch { }
        }

        /// <summary>Play a specific UI sound (an element's <see cref="UI.UIElement.ActivateSound"/>); null
        /// = play nothing.</summary>
        public static void Play(BlueprintUISound.UISound sound)
        {
            try { if (sound != null) UISounds.Instance?.Play(sound); }
            catch { /* Sound is non-essential; never let it break navigation. */ }
        }

        /// <summary>The page-turn the game plays when a wizard advances a phase — replayed from the
        /// VM-driven path (which bypasses the view's own click handler).</summary>
        public static void PageTurn() => Play(UISounds.Instance?.Sounds?.Dialogue?.BookPageTurn);

        /// <summary>The completion sting the game plays when character generation finishes.</summary>
        public static void ChargenComplete() => Play(UISounds.Instance?.Sounds?.Chargen?.ChargenCompleteClick);
    }
}
