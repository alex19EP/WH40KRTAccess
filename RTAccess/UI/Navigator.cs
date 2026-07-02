using System.Collections.Generic;
using RTAccess.Input;
using RTAccess.Screens;
using RTAccess.UI.Announcements;

namespace RTAccess.UI
{
    /// <summary>
    /// Owns navigation: consumes input, holds the focus path (within the screen,
    /// excluding the screen root itself — the screen name is announced separately),
    /// and centralizes focus-path diffing + announcement. Pluggable by user
    /// preference (TraditionalNavigator, TreeNavigator, …).
    ///
    /// Critical: focus mutations are silent. A navigation step snapshots the path,
    /// mutates it (including any recursive auto-descend), then announces the diff
    /// ONCE — never on each intermediate SetFocus.
    /// </summary>
    public abstract class Navigator
    {
        protected readonly List<UIElement> Path = new List<UIElement>();
        protected Screen Screen { get; private set; }

        // The screen we still owe an INITIAL-focus announcement for (set on screen entry, cleared once the
        // landing is announced or a screen takes over via Focus). Lets EnsureFocus deliver the landing even
        // when a screen's own build-time Attach established focus silently — the "read the thing you landed
        // on" after the screen name.
        private Screen _awaitingAnnounce;

        public UIElement Current => Path.Count > 0 ? Path[Path.Count - 1] : null;

        /// <summary>Bind to a screen and set initial focus (silently). Subclass decides how. A NEW screen
        /// entry arms the initial-focus announcement (delivered by EnsureFocus once focus exists); a
        /// re-attach to the SAME screen (e.g. a content rebuild) doesn't re-arm it, so rebuilds stay quiet.</summary>
        public void Attach(Screen screen)
        {
            if (!ReferenceEquals(screen, Screen)) _awaitingAnnounce = screen;
            Screen = screen;
            Path.Clear();
            if (screen != null) BuildInitialFocus();
        }

        /// <summary>Drop focus back to the screen's unfocused state — the same place Tab-off-the-end lands.
        /// Silent. On a <see cref="Screen.StartUnfocused"/> screen the keyboard returns to exploration and
        /// stays there (EnsureFocus won't re-seat it); on other screens EnsureFocus re-focuses next frame, so
        /// callers should only blur exploration-capable screens.</summary>
        public void Blur()
        {
            Path.Clear();
            Screen?.SetFocusedChild(null);
        }

        protected abstract void BuildInitialFocus();

        /// <summary>
        /// Re-establish initial focus when the focused screen has focusable content but nothing is focused.
        /// Screens that build their content lazily (an empty shell at <see cref="Attach"/>, filled a frame
        /// later in OnUpdate) have nothing to focus when first attached; without this they'd sit unfocused
        /// until the user tabbed in. <see cref="BuildInitialFocus"/> bows out for StartUnfocused screens
        /// (exploration), so they stay unfocused. Called once per frame after the screen updates; announces
        /// the landing when focus mode owns the keyboard. A no-op once something is focused.
        /// </summary>
        public void EnsureFocus()
        {
            if (Screen == null) return;
            // "No real focus" = nothing focused, OR focus stranded on a transparent Panel — which happens when
            // initial focus ran before a lazily-built screen filled its content panel (a Panel reports
            // focusable, so the descent stops on it). In both cases re-establish focus now that content exists.
            bool stranded = Current is Container c && c.Shape == ContainerShape.Panel;
            if (Current != null && !stranded)
            {
                // Focus is already established — but if a screen's build set it SILENTLY (build-time Attach)
                // and we still owe this screen's initial-focus announcement, deliver it now. This is the
                // landing read after the screen name (was missing for every Attach-only screen).
                if (ReferenceEquals(_awaitingAnnounce, Screen) && FocusMode.Active) AnnounceCurrent();
                return;
            }

            BuildInitialFocus();          // descends through the (now-populated) panels to a real leaf/cell
            var target = Current;
            Path.Clear();
            // Still no real target (content not built yet, or screen intentionally unfocused) — retry next
            // frame; don't announce or leave focus parked on a Panel.
            if (target == null || (target is Container tc && tc.Shape == ContainerShape.Panel)) return;
            // Re-seat through Focus so every intermediate cursor (e.g. a grid's cell) is set. Announce only
            // when focus mode owns the keyboard.
            Focus(target, announce: FocusMode.Active);
        }

        public abstract bool OnInputJustPressed(InputAction action);
        public virtual bool OnInputHeld(InputAction action) => false;
        public virtual bool OnInputReleased(InputAction action) => false;

        /// <summary>Per-frame hook for typed-character input (type-ahead search). Called from the main
        /// frame loop after action dispatch; the base navigator has no search.</summary>
        public virtual void TickTypeahead() { }

        private static readonly List<UIElement> EmptyPath = new List<UIElement>();

        /// <summary>
        /// Announce the full focus path (e.g. when focus mode engages or the screen
        /// changes) — diff from empty, so container labels + the focused leaf are read,
        /// e.g. "Main Menu, Continue".
        /// </summary>
        public void AnnounceCurrent()
        {
            _awaitingAnnounce = null; // announcing settles the initial-focus debt
            AnnounceDelta(EmptyPath);
        }

        /// <summary>Move focus to a specific element (e.g. a node just inserted into the tree) and announce
        /// the change. Queues (doesn't interrupt) so a preceding feedback line still plays.</summary>
        public void Focus(UIElement target, bool announce = true)
        {
            if (target == null) return;
            _awaitingAnnounce = null; // the screen set focus itself — it owns the initial announce (or its silence)
            var snapshot = new List<UIElement>(Path);
            BuildPathTo(target);
            if (announce) AnnounceDelta(snapshot);
        }

        /// <summary>Append an element to the path; if it's a container, descend to the INNERMOST
        /// remembered/selected element. A tree NODE is only descended into when it's expanded AND
        /// actually remembers (or has selected) a deeper target — otherwise focus lands on the node
        /// itself (we never auto-dive into expanded nodes via the first-focusable fallback).</summary>
        protected void AppendWithDescend(UIElement element)
        {
            if (element == null) return;
            Path.Add(element);
            DescendFrom(element);
        }

        /// <summary>Continue descending from an element ALREADY on the path to the innermost
        /// remembered/selected element (same rules as <see cref="AppendWithDescend"/>). Used after a
        /// Tab lands on a stop, so re-entering a tree restores the deep position, not the top node.</summary>
        protected void DescendFrom(UIElement element)
        {
            while (true)
            {
                var container = element as Container;
                if (container == null) return;

                UIElement next;
                // A tree NODE (a tree-shaped container nested inside the tree; the tree ROOT always
                // exposes its children).
                bool isTreeNode = container.Shape == ContainerShape.Tree
                    && container.Parent is Container parent && parent.Shape == ContainerShape.Tree;
                if (isTreeNode)
                {
                    if (!container.Expanded) return; // collapsed → its children aren't navigable
                    next = RememberedOrSelected(container);
                    if (next == null) return;        // nothing remembered/selected → stay on the node
                }
                else
                {
                    next = RepresentativeChild(container);
                    if (next == null) return;
                }
                container.SetFocusedChild(next);
                Path.Add(next);
                element = next;
            }
        }

        /// <summary>A container's remembered focus, else its selected child — WITHOUT the
        /// first-focusable fallback (used to decide whether descending deeper is justified).</summary>
        private static UIElement RememberedOrSelected(Container c)
        {
            if (c.FocusedChild != null && c.FocusedChild.CanFocus && !IsEmptyPanel(c.FocusedChild))
                return c.FocusedChild;
            return SelectedChild(c);
        }

        // A Panel with nothing focusable inside — structural only; never a valid focus target or
        // remembered-focus memory (a stranded landing on one must not be resurrected by descent).
        private static bool IsEmptyPanel(UIElement e)
            => e is Container c && c.Shape == ContainerShape.Panel && c.FirstFocusable() == null;

        /// <summary>The child to land on when first focusing a container: the remembered focus, else — for a
        /// single-select <b>list or tree</b> (radio buttons, tabs, the deity tree) — the currently-selected
        /// DIRECT child, else the first focusable. Single-level by design; AppendWithDescend chains it to
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

        /// <summary>
        /// Diff a pre-move snapshot against the settled path and speak the delta:
        /// newly-entered nodes in path order (descend/sibling), or just the new
        /// innermost element (ascend). Called once, after the move is complete.
        /// </summary>
        protected void AnnounceDelta(List<UIElement> oldPath, bool interrupt = false)
        {
            // interrupt == true marks an actual focus MOVE (arrow/tab), not a screen-entry readout —
            // so it's where the game would play its control-hover sound. Current is already the
            // destination here, so its HoverSoundType picks the themed sound (Analog/Plastick/…) the
            // mouse would produce on that control (null ⇒ generic hover).
            if (interrupt) RTAccess.UiSound.Hover(Current?.HoverSoundType);

            int i = 0;
            while (i < oldPath.Count && i < Path.Count && oldPath[i] == Path[i]) i++;

            if (i < Path.Count)
            {
                var sb = new List<string>();
                for (int j = i; j < Path.Count; j++)
                {
                    // Skip a container whose label just duplicates the node beneath it (e.g. a
                    // "Game difficulty" section wrapping the "Game difficulty" control).
                    if (j + 1 < Path.Count)
                    {
                        var label = Path[j].GetLabelText();
                        if (!string.IsNullOrEmpty(label) && label == Path[j + 1].GetLabelText())
                            continue;
                    }
                    // The leaf cell of an associated-element table reads as the whole row (element focus +
                    // columns), so first-focus / focus-restore match arrowing. Its FlowSheet container is
                    // already a path node above, so the region label is omitted here.
                    var d = (j == Path.Count - 1 ? (Path[j].Parent as FlowSheet)?.ComposeAssociatedReadout(Path[j], false) : null)
                            ?? Path[j].GetFocusMessage().Resolve();
                    if (!string.IsNullOrEmpty(d)) sb.Add(d);
                }
                if (sb.Count > 0) Speak(string.Join(", ", sb), interrupt);
            }
            else if (Current != null)
            {
                var ar = (Current.Parent as FlowSheet)?.ComposeAssociatedReadout(Current, false);
                Speak(ar ?? Current.GetFocusMessage().Resolve(), interrupt); // ascended: announce the now-innermost focus
            }
        }

        /// <summary>Rebuild the focus path as the ancestor chain from the screen down to <paramref name="target"/>.</summary>
        protected void BuildPathTo(UIElement target)
        {
            Path.Clear();
            if (target == null) return;
            var chain = new List<UIElement>();
            var e = target;
            while (e != null && e != Screen)
            {
                chain.Add(e);
                if (e.Parent != null) e.Parent.SetFocusedChild(e);
                e = e.Parent;
            }
            chain.Reverse();
            Path.AddRange(chain);
        }

        /// <summary>
        /// Ordered Tab-stops for the screen: descend through Panels; a List/Grid is a
        /// single stop (its current/first item); leaves under a Panel are stops.
        /// </summary>
        protected List<UIElement> ComputeTabStops()
        {
            var stops = new List<UIElement>();
            if (Screen != null) AddStops(Screen, stops);
            return stops;
        }

        private static void AddStops(Container c, List<UIElement> stops)
        {
            if (c.Shape != ContainerShape.Panel)
            {
                var item = RepresentativeChild(c);
                if (item != null) stops.Add(item);
                return;
            }
            var children = c.Children;
            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child is Container cc) AddStops(cc, stops);
                else if (child.CanFocus) stops.Add(child);
            }
        }

        // interrupt: true for focus MOVES (so held key-repeat reads the item you land on
        // instead of backing up a queue); false for screen-entry "where am I" readouts.
        protected static void Speak(string text, bool interrupt = false)
        {
            if (!string.IsNullOrEmpty(text)) Tts.Speak(text, interrupt);
        }
    }
}
