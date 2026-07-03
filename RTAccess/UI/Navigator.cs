using RTAccess.Input;
using RTAccess.Screens;
using RTAccess.UI.Announcements;

namespace RTAccess.UI
{
    /// <summary>
    /// The navigation contract <see cref="Navigation"/> drives: bind to a screen, consume input, keep
    /// focus established, and announce focus changes. Implementations are pluggable
    /// (<see cref="TraditionalNavigator"/> today; the key-graph core over TreeGraphAdapter next), where
    /// announcements are PULL-based: focus is diffed per frame and a change speaks exactly once no
    /// matter what caused it — so implementations and screens never make per-callsite announce
    /// decisions. The base carries only the shared focus-restore rules (remembered/selected descent),
    /// the per-frame focus pump, and the speech chokepoint.
    /// </summary>
    public abstract class Navigator
    {
        protected Screen Screen { get; set; }

        /// <summary>The currently focused element, or null (e.g. an unfocused exploration screen).</summary>
        public abstract UIElement Current { get; }

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
        public virtual bool OnInputHeld(InputAction action) => false;
        public virtual bool OnInputReleased(InputAction action) => false;

        /// <summary>Per-frame hook for typed-character input (type-ahead search).</summary>
        public virtual void TickTypeahead() { }

        /// <summary>Announce the current focus in full (the container hierarchy down to the element) —
        /// e.g. when focus mode engages.</summary>
        public abstract void AnnounceCurrent();

        /// <summary>Move focus to a specific element. <paramref name="announce"/> false lands silently
        /// (the screen owns whatever is spoken instead).</summary>
        public abstract void Focus(UIElement target, bool announce = true);

        /// <summary>A screen closed (stack pop without <see cref="Screen.KeepStateOnPop"/>, or a child
        /// page removed): drop its per-screen state so reopening starts fresh (and the map doesn't
        /// leak one-shot child instances). Covered-but-alive screens always keep theirs.</summary>
        public virtual void ScreenClosed(Screen screen) { }

        /// <summary>Move focus to a graph node by id (graph-native screens' analog of
        /// <see cref="Focus"/>) — applied when the node exists in a render, with one retry frame for
        /// content that appears mid-build (e.g. focusing a node just added by an action).</summary>
        public virtual void FocusNode(Graph.ControlId id, bool announce = true) { }

        /// <summary>Move focus to the FIRST node of a Tab-stop (a wizard landing on the new page's
        /// content after Next; a screen seating a section whose node keys vary per state).</summary>
        public virtual void FocusStop(object stopKey) { }

        /// <summary>The Tab-stop the focused node belongs to, or null (screen logic that branches on
        /// where focus is — e.g. Escape drills back only from the page stop, closes from the tree).</summary>
        public virtual object FocusedStopKey => null;

        // ---- per-frame focus pump (RT extension over the WA contract: Main ticks it every frame
        //      and save-slot focus-selection rides it) ----

        // The element we last delivered OnFocusEnter to (the row's associated control when the cursor sits on
        // a value cell). Change-guards the per-frame focus pump so the hook fires exactly once per settled
        // target, across every move path (arrow/tab/jump/search/region) without threading a call through each.
        private UIElement _focusEntered;

        /// <summary>Per-frame: deliver <see cref="UIElement.OnFocusEnter"/> to the settled focus target once
        /// it changes. A value cell in an associated-element <see cref="FlowSheet"/> defers to its row's
        /// control, so focus on ANY column of a row hits that row's control — mirroring the game, whose
        /// focusable unit is the whole row (its slot view's SetFocus selects the slot). Called after input +
        /// screen resolution each frame; idempotent while focus is unchanged, so it's safe to call blindly and
        /// it catches every move path without a hook threaded through each. Elements that don't opt in (the
        /// default no-op) are unaffected, so this is inert on every screen but the ones that want it.</summary>
        public virtual void PumpFocus()
        {
            // Dormant when Focus Mode is off — the mod must not touch game VM state then (the same gate every
            // sibling per-frame hook uses; without it, focus-selects would clobber a mouse user's SelectedSaveSlot).
            if (!FocusMode.Active) return;
            var target = (Current?.Parent as FlowSheet)?.AssociatedElementForCell(Current) ?? Current;
            if (ReferenceEquals(target, _focusEntered)) return;
            _focusEntered = target;
            target?.OnFocusEnter();
        }

        /// <summary>Re-arm the focus pump so it re-fires on the next settled target — implementations
        /// call this on (re)attach, where the landing element must get OnFocusEnter again.</summary>
        protected void ResetFocusPump() => _focusEntered = null;

        // ---- shared focus-restore rules ----

        /// <summary>The child to land on when first focusing a container: the remembered focus, else — for a
        /// single-select <b>list or tree</b> (radio buttons, tabs, the deity tree) — the currently-selected
        /// DIRECT child, else the first focusable. Single-level by design; descent chains it to
        /// reach the innermost remembered/selected element (tree nodes only when expanded + justified).
        /// Tree stepping/expanding never prefers selected, so expanding a node won't yank focus to a
        /// selected descendant. Panels/grids don't prefer selected.</summary>
        protected static UIElement RepresentativeChild(Container c)
        {
            if (c == null) return null;
            if (c.FocusedChild != null && c.FocusedChild.CanFocus && !IsEmptyPanel(c.FocusedChild))
                return c.FocusedChild;
            if (c.Shape == ContainerShape.VerticalList || c.Shape == ContainerShape.HorizontalList
                || c.Shape == ContainerShape.Tree)
            {
                var selected = SelectedChild(c);
                if (selected != null) return selected;
            }
            return c.FirstFocusable();
        }

        /// <summary>A container's remembered focus, else its selected child — WITHOUT the
        /// first-focusable fallback (used to decide whether descending deeper is justified).</summary>
        protected static UIElement RememberedOrSelected(Container c)
        {
            if (c.FocusedChild != null && c.FocusedChild.CanFocus && !IsEmptyPanel(c.FocusedChild))
                return c.FocusedChild;
            return SelectedChild(c);
        }

        // A Panel with nothing focusable inside — structural only; never a valid focus target or
        // remembered-focus memory (a stranded landing on one must not be resurrected by descent).
        private static bool IsEmptyPanel(UIElement e)
            => e is Container c && c.Shape == ContainerShape.Panel && c.FirstFocusable() == null;

        private static UIElement SelectedChild(Container c)
        {
            foreach (var child in c.Children)
                if (child.CanFocus && ReportsSelected(child)) return child;
            return null;
        }

        // An element is "selected" if it yields a SelectedAnnouncement that renders non-empty (single-select
        // controls render "selected" only when selected). Checkboxes/toggles use ValueAnnouncement, not
        // SelectedAnnouncement, so they never count here.
        private static bool ReportsSelected(UIElement e)
        {
            var ctx = new AnnouncementContext(e);
            foreach (var a in e.GetFocusAnnouncements())
                if (a is SelectedAnnouncement)
                {
                    var m = a.Render(ctx);
                    if (m != null && !m.IsEmpty) return true;
                }
            return false;
        }

        // interrupt: true for focus MOVES (so held key-repeat reads the item you land on instead of
        // backing up a queue); false for screen-entry / landing readouts.
        protected static void Speak(string text, bool interrupt = false)
        {
            if (!string.IsNullOrEmpty(text)) Tts.Speak(text, interrupt);
        }
    }
}
