using System;
using System.Collections.Generic;
using RTAccess.UI.Announcements;

namespace RTAccess.UI
{
    /// <summary>
    /// A passive structural blueprint: holds children, remembers its focused child
    /// (for restore), and exposes shape geometry. Navigation/input policy lives in
    /// the Navigator. See <see cref="Panel"/> (Tab-traversed) and
    /// <see cref="ListContainer"/> (arrow-traversed, single Tab-stop).
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement))]
    public class Container : UIElement
    {
        private readonly List<UIElement> _children = new List<UIElement>();
        private string _label;

        public IReadOnlyList<UIElement> Children => _children;

        /// <summary>Remembered focus within this container, for restore on re-entry.</summary>
        public UIElement FocusedChild { get; private set; }

        public ContainerShape Shape { get; protected set; } = ContainerShape.VerticalList;

        /// <summary>When set on a screen root, navigators that respect it wrap Tab past the ends.</summary>
        public bool Wrap { get; set; }

        // ---- Tree expand/collapse (only meaningful for Shape == Tree) ----

        /// <summary>Whether a Tree node is currently expanded (its children participate in nav).</summary>
        public bool Expanded { get; protected set; }

        /// <summary>True if this Tree node can be expanded to reveal children. Cheap — must NOT build
        /// lazy children; a subclass with a deferred factory (e.g. TooltipNode drill-in) overrides.</summary>
        public virtual bool Expandable => Shape == ContainerShape.Tree && _children.Count > 0;

        /// <summary>Accordion mode (set on the PARENT): only one child node may be expanded at a time —
        /// expanding a child collapses its expanded siblings first. Used where the expansion mirrors a
        /// visual panel swap (the rest screen's role panels), keeping the on-screen UI in step with
        /// where the tree focus is.</summary>
        public bool ExclusiveExpansion { get; set; }

        /// <summary>Expand this Tree node. Subclasses with lazy children override to build first
        /// (call base.Expand() FIRST so accordion siblings collapse before the new content builds).</summary>
        public virtual void Expand()
        {
            if (Parent != null && Parent.ExclusiveExpansion)
                foreach (var sib in Parent.Children)
                    if (sib != this && sib is Container c && c.Expanded) c.Collapse();
            Expanded = true;
        }

        /// <summary>Collapse this Tree node. Virtual so accordion nodes can tear down (e.g. close the
        /// game panel their expansion opened).</summary>
        public virtual void Collapse() => Expanded = false;

        /// <summary>Live label override (wins over the static label) — for nodes whose name changes
        /// without a rebuild, e.g. an overlay node that shows "(standard)" based on live order.</summary>
        public Func<string> LabelProvider { get; set; }

        public override string Label => LabelProvider != null ? LabelProvider() : _label;

        /// <summary>Lists/grids/trees announce their children's position; panels (pure structure) don't.</summary>
        public virtual bool AnnouncePosition => Shape != ContainerShape.Panel;

        /// <summary>"index of count" among focusable children, or null if not found.</summary>
        public Message GetPositionString(UIElement child)
        {
            int count = 0, ci = -1;
            for (int i = 0; i < _children.Count; i++)
            {
                if (!_children[i].CanFocus) continue;
                count++;
                if (_children[i] == child) ci = count;
            }
            return ci < 0 ? null : Message.Localized("ui", "nav.position", new { index = ci, count });
        }

        // Unlabeled structural containers stay silent; labeled ones announce name (+ a role: a Tree
        // node reads its expanded/collapsed state; other lists/grids read "list").
        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            if (string.IsNullOrEmpty(Label)) yield break;
            yield return new LabelAnnouncement(Message.Raw(Label));
            if (Shape == ContainerShape.Tree)
            {
                if (Expandable) yield return new RoleAnnouncement(Expanded ? "expanded" : "collapsed");
            }
            else if (Shape != ContainerShape.Panel) yield return new RoleAnnouncement("list");
        }

        public Container() { }

        public Container(ContainerShape shape, string label = null)
        {
            Shape = shape;
            _label = label;
        }

        protected void SetContainerLabel(string label) => _label = label;

        public void Add(UIElement element)
        {
            element.Parent = this;
            _children.Add(element);
        }

        /// <summary>Insert a child at a position (clamped). For live tree mutation — no full rebuild.</summary>
        public void Insert(int index, UIElement element)
        {
            if (element == null) return;
            element.Parent = this;
            if (index < 0) index = 0;
            if (index > _children.Count) index = _children.Count;
            _children.Insert(index, element);
        }

        /// <summary>Remove a child (live tree mutation). Clears the remembered focus if it was this child.</summary>
        public void Remove(UIElement element)
        {
            if (element != null && _children.Remove(element))
            {
                element.Parent = null;
                if (FocusedChild == element) FocusedChild = null;
            }
        }

        public int IndexOf(UIElement element) => _children.IndexOf(element);

        public void Clear()
        {
            _children.Clear();
            FocusedChild = null;
        }

        public void SetFocusedChild(UIElement element) => FocusedChild = element;

        /// <summary>First child the navigator may land on (skips non-focusable). Also skips a Panel
        /// with nothing focusable inside (recursively): panels are structural, so descending into an
        /// empty one strands focus on a silent node — and on a lazily-built screen (e.g. a chargen
        /// phase whose list materializes frames later) the stranded empty SIBLING would keep winning
        /// the descent forever, so EnsureFocus could never re-home onto the content once it appeared.</summary>
        public virtual UIElement FirstFocusable()
        {
            for (int i = 0; i < _children.Count; i++)
            {
                if (!_children[i].CanFocus) continue;
                if (_children[i] is Container c && c.Shape == ContainerShape.Panel && c.FirstFocusable() == null)
                    continue;
                return _children[i];
            }
            return null;
        }

        /// <summary>Last child the navigator may land on (the Home/End jump's End target); skips
        /// non-focusable children and empty Panels, mirroring <see cref="FirstFocusable"/>.</summary>
        public virtual UIElement LastFocusable()
        {
            for (int i = _children.Count - 1; i >= 0; i--)
            {
                if (!_children[i].CanFocus) continue;
                if (_children[i] is Container c && c.Shape == ContainerShape.Panel && c.FirstFocusable() == null)
                    continue;
                return _children[i];
            }
            return null;
        }

        /// <summary>Next focusable child from <paramref name="from"/> in a direction (list shapes only).</summary>
        public virtual UIElement GetNeighbor(UIElement from, NavDirection dir)
        {
            int step = StepFor(dir);
            if (step == 0) return null;
            int idx = _children.IndexOf(from);
            if (idx < 0) return null;
            for (int i = idx + step; i >= 0 && i < _children.Count; i += step)
                if (_children[i].CanFocus) return _children[i];
            return null;
        }

        private int StepFor(NavDirection dir)
        {
            if (Shape == ContainerShape.VerticalList)
                return dir == NavDirection.Down ? 1 : dir == NavDirection.Up ? -1 : 0;
            if (Shape == ContainerShape.HorizontalList)
                return dir == NavDirection.Right ? 1 : dir == NavDirection.Left ? -1 : 0;
            return 0; // Panel uses Tab traversal; Grid overrides.
        }
    }
}
