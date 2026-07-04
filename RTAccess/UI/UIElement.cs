using System;
using System.Collections.Generic;
using RTAccess.UI.Announcements;

namespace RTAccess.UI
{
    /// <summary>
    /// A navigable element. Leaves (proxies) yield typed Announcements that compose
    /// into the spoken focus message; they expose activation handlers but do NOT
    /// handle input keys or navigation — the Navigator does.
    /// </summary>
    public abstract class UIElement
    {
        public Container Parent { get; internal set; }

        public virtual bool CanFocus => true;

        /// <summary>Convenience name used by the default GetFocusAnnouncements; containers/proxies set it.</summary>
        public virtual string Label => null;

        /// <summary>Convenience role used by the default GetFocusAnnouncements (e.g. "button").</summary>
        public virtual string Role => null;

        /// <summary>The type whose [AnnouncementOrder] governs composition (composite proxies can delegate).</summary>
        public virtual Type AnnouncementOrderType => GetType();

        /// <summary>
        /// The actions this element advertises (activate, increase, setValue, …).
        /// Navigators discover and invoke these by id rather than knowing element types.
        /// </summary>
        public virtual IEnumerable<ElementAction> GetActions() { yield break; }

        /// <summary>
        /// True if activating changes this element's value in place (checkbox, tab-select,
        /// slider) and should be re-announced. False for navigation buttons that open a new
        /// screen (the screen change announces itself; re-reading the button is noise).
        /// </summary>
        public virtual bool ReannounceOnActivate => false;

        /// <summary>Called once, on the frame this element becomes the SETTLED focus (after a navigation move
        /// completes) — never on the intermediate descent hops a single move passes through. Default no-op.
        /// Controls that mirror the game's focus-selects behaviour override it to COMMIT a selection on focus
        /// (e.g. a save slot: the game's own gamepad view selects the slot in <c>SetFocus</c>). Keep it
        /// non-destructive — selection, never activation — since it fires on mere navigation.</summary>
        public virtual void OnFocusEnter() { }

        /// <summary>
        /// A "complex" (brick) tooltip template for this element, or null. The tooltip key (Space)
        /// opens the reader screen with it. Override on elements that carry a game tooltip.
        /// </summary>
        public virtual Owlcat.Runtime.UI.Tooltips.TooltipBaseTemplate GetTooltipTemplate() => null;

        /// <summary>Plain tooltip/description text for the lightweight tooltip reader (Space opens it),
        /// or null. Settings controls return their Description here. (The rich brick/glossary tooltip via
        /// <see cref="GetTooltipTemplate"/> is a separate later feature.)</summary>
        public virtual string GetTooltipText() => null;

        /// <summary>Extra tooltip SECTIONS beyond this element's own tooltip — each an already-rendered
        /// (label, body) pair. When present, Space opens the drill menu with the main tooltip first, then
        /// these, then any glossary links. Used to surface an inventory item's compare-vs-equipped readouts
        /// (mirroring the game's hover, which shows the item card plus the equipped items it would replace).
        /// Default none.</summary>
        public virtual System.Collections.Generic.IReadOnlyList<(string label, string body)> GetTooltipSections() => null;

        /// <summary>Resolve a NON-glossary inline link this element specifically knows how to handle —
        /// e.g. a dialogue skill-check link, built from the element's own check data — given the link's
        /// raw id and the keys the game parsed from it (<c>UIUtility.GetKeysFromLink</c>). Default null
        /// ⇒ the standard glossary/encyclopedia resolution applies. Tried BEFORE the glossary probe.</summary>
        public virtual Owlcat.Runtime.UI.Tooltips.TooltipBaseTemplate ResolveLink(string id, string[] keys) => null;

        /// <summary>Like <see cref="ReannounceOnActivate"/>, but for the secondary (context) action —
        /// e.g. clearing a key binding should re-announce the now-empty value.</summary>
        public virtual bool ReannounceOnContext => false;

        /// <summary>The game UI sound to play when this element is activated (its sound normally
        /// lived in the view's click handler, which we bypass). Default is a generic button click;
        /// controls override for their real sound (e.g. toggles → SettingsSwitchToggle), or return
        /// null when the element already plays its own sound (e.g. portraits) so we don't double it.
        /// Superseded by <see cref="ClickSoundType"/> when that is set (themed OwlcatButtons).</summary>
        public virtual Kingmaker.UI.Sound.BlueprintUISound.UISound ActivateSound =>
            Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Buttons?.ButtonClick;

        /// <summary>The game's themed button "sound type" for this control's HOVER (main-menu Analog,
        /// window Plastick, …), or null for the generic button hover. The game sets this per
        /// OwlcatSelectable via <c>UISounds.SetHoverSound</c> and replays it on mouse-over as
        /// <c>PlayHoverSound(type)</c>; we replay the same on a focus MOVE because we bypass the view.
        /// <c>NoSound</c> is a real value here — it silences hover (matching dense item/ability grids
        /// the game deliberately keeps quiet). Null ⇒ the generic <c>ButtonHover</c>.</summary>
        public virtual Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum? HoverSoundType => null;

        /// <summary>Like <see cref="HoverSoundType"/> but for activation: the themed
        /// <c>PlayButtonClickSound(type)</c>. When set it takes precedence over the blueprint-typed
        /// <see cref="ActivateSound"/> (the two model the game's two distinct sound mechanisms —
        /// themed-type vs. one-off blueprint — and an element uses one or the other). Null ⇒ fall back
        /// to <see cref="ActivateSound"/>.</summary>
        public virtual Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum? ClickSoundType => null;

        /// <summary>Find an advertised action by id and execute it. Returns true if found.</summary>
        public bool InvokeAction(string id, object args = null)
        {
            foreach (var a in GetActions())
            {
                if (a.Id == id) { a.Execute(args); return true; }
            }
            return false;
        }

        /// <summary>The announcement parts this element contributes. Default: label + role.</summary>
        public virtual IEnumerable<Announcement> GetFocusAnnouncements()
        {
            if (!string.IsNullOrEmpty(Label)) yield return new LabelAnnouncement(Message.Raw(Label));
            if (!string.IsNullOrEmpty(Role)) yield return new RoleAnnouncement(Role);
        }

        /// <summary>
        /// Just this element's changed state ("checked", "selected", a slider amount…), for
        /// re-announcing after an in-place activation — we already know which control we're on,
        /// so we don't repeat the whole focus message.
        /// </summary>
        public Message GetStateMessage()
        {
            var ctx = new AnnouncementContext(this);
            var parts = new List<Message>();
            foreach (var a in GetFocusAnnouncements())
                if (a is ValueAnnouncement || a is SelectedAnnouncement)
                    parts.Add(a.Render(ctx));
            return Message.Join(", ", parts.ToArray());
        }

        /// <summary>This element's label text (from its LabelAnnouncement), or null. Used to
        /// de-duplicate a container whose label matches the control directly beneath it.</summary>
        public string GetLabelText()
        {
            var ctx = new AnnouncementContext(this);
            foreach (var a in GetFocusAnnouncements())
                if (a is LabelAnnouncement) { var m = a.Render(ctx); return m != null ? m.Resolve() : null; }
            return null;
        }

        /// <summary>The raw game text this element shows, MARKUP INTACT (pre-strip) — the source for
        /// inline glossary <c>&lt;link&gt;</c> extraction (Space surfaces them). Defaults to the
        /// element's own label rendered WITHOUT stripping, so any element whose label is game text
        /// exposes its links for free; override where the element knows a richer/different source.
        /// Returns null/markup-free text for our own UI labels (no links → nothing extracted).</summary>
        public virtual string GetLinkSourceText()
        {
            var ctx = new AnnouncementContext(this);
            foreach (var a in GetFocusAnnouncements())
                if (a is LabelAnnouncement) { var m = a.Render(ctx); return m != null ? m.ResolveRaw() : null; }
            return null;
        }

        /// <summary>This element's focus announcements rendered to comma-joined text, optionally limited to
        /// certain announcement types (null = all, in declared order). Used by a table's associated-element
        /// readout to speak the row's control ("Fireball, radio button, selected") on up/down.</summary>
        public string GetFocusText(System.Type[] include = null)
        {
            var ctx = new AnnouncementContext(this);
            var parts = new List<Message>();
            foreach (var a in GetFocusAnnouncements())
            {
                if (include != null)
                {
                    bool keep = false;
                    foreach (var t in include) if (t != null && t.IsInstanceOfType(a)) { keep = true; break; }
                    if (!keep) continue;
                }
                var m = a.Render(ctx);
                if (m != null) parts.Add(m);
            }
            var joined = Message.Join(", ", parts.ToArray());
            return joined != null ? joined.Resolve() : null;
        }

        /// <summary>The composed spoken focus message (parts + parent-supplied position).</summary>
        public Message GetFocusMessage()
        {
            var anns = new List<Announcement>(GetFocusAnnouncements());
            if (Parent != null && Parent.AnnouncePosition)
            {
                var pos = Parent.GetPositionString(this);
                if (pos != null) anns.Add(new PositionAnnouncement(pos));
            }
            return AnnouncementComposer.Compose(this, anns);
        }
    }
}
