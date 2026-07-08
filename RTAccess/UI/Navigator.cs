using RTAccess.Input;
using RTAccess.Screens;

namespace RTAccess.UI
{
    /// <summary>
    /// The navigation contract <see cref="Navigation"/> drives: bind to a screen, consume input, keep
    /// focus established, and announce focus changes. The active implementation is
    /// <see cref="GraphNavigator"/> (the key-graph core), where announcements are PULL-based: focus is
    /// diffed per frame and a change speaks exactly once no matter what caused it — so implementations
    /// and screens never make per-callsite announce decisions. The base carries only the speech
    /// chokepoint.
    /// </summary>
    public abstract class Navigator
    {
        protected Screen Screen { get; set; }

        /// <summary>True when the navigator owns the keys (something is focused) — false in the
        /// unfocused exploration state. The single chokepoint <see cref="Navigation.HasFocus"/> reads
        /// (and through it chord claiming, the in-game screen's category flip, and every exploration
        /// gate).</summary>
        public abstract bool HasFocus { get; }

        /// <summary>Bind to a screen. Re-attaching the SAME screen means "content changed" (focus and
        /// announce memory survive); a new screen resets both.</summary>
        public abstract void Attach(Screen screen);

        /// <summary>Drop focus back to the screen's unfocused state — the same place Tab-off-the-end
        /// lands. On a <see cref="Screen.StartUnfocused"/> screen the keyboard returns to exploration
        /// and stays there; on other screens focus re-establishes next frame, so callers should only
        /// blur exploration-capable screens.</summary>
        public abstract void Blur();

        /// <summary>The per-frame pull, called after the focused screen updates: (re)establish focus
        /// when the screen has focusable content, and announce any focus change exactly once.</summary>
        public abstract void EnsureFocus();

        public abstract bool OnInputJustPressed(InputAction action);

        /// <summary>Per-frame hook for typed-character input (type-ahead search).</summary>
        public virtual void TickTypeahead() { }

        /// <summary>Announce the current focus in full — e.g. when focus mode engages.</summary>
        public abstract void AnnounceCurrent();

        /// <summary>A screen closed (stack pop without <see cref="Screen.KeepStateOnPop"/>, or a child
        /// page removed): drop its per-screen state so reopening starts fresh (and the map doesn't
        /// leak one-shot child instances). Covered-but-alive screens always keep theirs.</summary>
        public virtual void ScreenClosed(Screen screen) { }

        /// <summary>Move focus to a graph node by id — applied when the node exists in a render, with
        /// one retry frame for content that appears mid-build (e.g. focusing a node just added by an
        /// action).</summary>
        public virtual void FocusNode(Graph.ControlId id, bool announce = true) { }

        /// <summary>Move focus to the FIRST node of a Tab-stop (a wizard landing on the new page's
        /// content after Next; a screen seating a section whose node keys vary per state).</summary>
        public virtual void FocusStop(object stopKey) { }

        /// <summary>The Tab-stop the focused node belongs to, or null (screen logic that branches on
        /// where focus is — e.g. Escape drills back only from the page stop, closes from the tree).</summary>
        public virtual object FocusedStopKey => null;

        /// <summary>The domain object the focused node was declared from (a game VM — the Reference
        /// tier of its <see cref="Graph.ControlId"/>), or null. Used by screens that follow focus onto
        /// game VMs (the New Game story page driving the game's description panel to the focused
        /// campaign/DLC row).</summary>
        public virtual object FocusedNodeReference => null;

        // interrupt: true for focus MOVES (so held key-repeat reads the item you land on instead of
        // backing up a queue); false for screen-entry / landing readouts.
        protected static void Speak(string text, bool interrupt = false)
        {
            if (!string.IsNullOrEmpty(text)) Tts.Speak(text, interrupt);
        }
    }
}
