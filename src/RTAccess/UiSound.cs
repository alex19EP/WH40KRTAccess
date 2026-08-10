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

        /// <summary>Play a themed button CLICK sound by the game's sound-type (Analog, Plastick, …),
        /// mirroring what the view's handler would play via <c>PlayButtonClickSound(type)</c>.
        /// <c>NoSound</c> is a genuine value — the game maps it to a no-op, so it silences the click.</summary>
        public static void Click(UISounds.ButtonSoundsEnum type)
        {
            try { UISounds.Instance?.PlayButtonClickSound((int)type); } catch { }
        }

        /// <summary>The game's control-hover sound — played when our focus moves to a new element
        /// (our equivalent of a mouseover). The generic <c>ButtonHover</c>.</summary>
        public static void Hover()
        {
            try { UISounds.Instance?.PlayHoverSound(); } catch { }
        }

        /// <summary>Hover sound for a specific themed button type (Analog for the main menu, Plastick
        /// for window chrome, …); null ⇒ the generic <see cref="Hover()"/>. <c>NoSound</c> silences it
        /// (the game maps type -2 to a no-op), matching the dense grids the game keeps quiet.</summary>
        public static void Hover(UISounds.ButtonSoundsEnum? type)
        {
            if (type == null) { Hover(); return; }
            try { UISounds.Instance?.PlayHoverSound((int)type.Value); } catch { }
        }

        /// <summary>Play a specific UI sound (a control's blueprint-typed activate sound); null
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
