using System;
using System.Collections.Generic;
using Kingmaker.UI.MVVM.VM.CharGen.Phases;       // CharGenPhaseBaseVM
using Kingmaker.UI.MVVM.VM.CharGen.Phases.Stats; // CharGenAttributesItemVM
using Owlcat.Runtime.UI.SelectionGroup;          // SelectionGroupEntityVM
using Owlcat.Runtime.UI.Tooltips;                // TooltipBaseTemplate
using RTAccess.UI.Graph;
using TMPro;

namespace RTAccess.UI
{
    /// <summary>
    /// Node factories for the character-generation family (the adapter-era ProxySelectionItem /
    /// ProxyRoadmapEntry / ProxyStatStepper / ProxySequentialSelector conventions, factory-shaped —
    /// ported from WrathAccess's CharGenNodes at 6308fea and adapted to RT's VMs). Includes the
    /// TextField idiom: a graph node wrapping one of the game's live <see cref="TMP_InputField"/>s,
    /// activated into <see cref="TextEntry"/> so Unity/TMP own caret, Unicode and IME.
    /// </summary>
    public static class CharGenNodes
    {
        /// <summary>A text-entry control over one of the game's <see cref="TMP_InputField"/>s. The
        /// field is fetched live via <paramref name="acquire"/> (it lives on the active VIEW, not the
        /// VM tree); activating hands the keyboard to <see cref="TextEntry"/>. The value part reads
        /// live at announce time but is deliberately NOT a Live watch: while TextEntry owns the
        /// keyboard the per-keystroke echo IS the feedback (EnsureFocus keeps ticking, so a live
        /// watch here would double-speak every character), and the committed value reads back via
        /// TextEntry.End's AnnounceCurrent.</summary>
        public static NodeVtable TextField(string label, Func<TMP_InputField> acquire, Func<string> value)
        {
            return new NodeVtable
            {
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(() => label),
                    new NodeAnnouncement(() => Loc.T("role.edit"), kind: AnnouncementKinds.Role),
                    new NodeAnnouncement(() =>
                    {
                        var v = value?.Invoke();
                        return string.IsNullOrEmpty(v) ? Loc.T("value.blank") : v;
                    }, kind: AnnouncementKinds.Value),
                },
                SearchText = () => label,
                OnActivate = () =>
                {
                    var field = acquire?.Invoke();
                    if (field != null) TextEntry.Begin(field, label);
                    else Tts.Speak(Loc.T("text.unavailable"), interrupt: true);
                },
            };
        }

        /// <summary>One choice of a game selection group (<see cref="SelectionGroupEntityVM"/> —
        /// pregen, homeworld/occupation feature, career, ship, appearance page, voice): the
        /// ProxySelectionItem contract as a vtable. "label, radio button[, selected][, disabled],
        /// n of m"; Enter selects through the game's own SetSelectedFromView (or
        /// <paramref name="onActivate"/> — e.g. replaying a voice sample when already chosen);
        /// selecting re-announces "selected" synchronously; Space opens the drill-in
        /// <paramref name="tooltip"/> (resolved live). Pass <paramref name="type"/>
        /// <see cref="ControlTypes.Tab"/> for tab-shaped groups (appearance pages).</summary>
        public static NodeVtable SelectionItem(SelectionGroupEntityVM vm, Func<string> label,
            Func<TooltipBaseTemplate> tooltip = null, Func<bool> available = null,
            Action onActivate = null, ControlType type = null)
        {
            Func<bool> isAvailable = available ?? (() => vm != null && vm.IsAvailable.Value);
            Func<bool> selected = () => vm != null && vm.IsSelected.Value;
            bool avail = isAvailable(); // fresh per render (immediate mode), like GraphNodes.Button
            return new NodeVtable
            {
                ControlType = type ?? ControlTypes.RadioButton,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(label),
                    GraphNodes.SelectedPart(selected),
                    GraphNodes.DisabledPart(isAvailable),
                },
                SearchText = label,
                // Selecting flips the item in place — re-announce the new state synchronously (the
                // ReannounceOnActivate convention). Async commits settle via the live Selected part.
                StateText = avail ? (Func<string>)(() => selected() ? Loc.T("state.selected") : null) : null,
                OnActivate = !avail ? (Action)null
                    : onActivate ?? (Action)(() => vm?.SetSelectedFromView(true)),
                OnTooltip = tooltip == null ? (Action)null
                    : () => TooltipChooser.OpenTemplate(label?.Invoke(), tooltip()),
                ActivateSound = avail
                    ? Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Buttons?.ButtonClick
                    : null,
            };
        }

        /// <summary>One step in the chargen roadmap strip: phase name, whether it's the current step,
        /// "completed" (live — finishing a phase announces under focus), "disabled" while locked.
        /// Activating jumps to the phase when reachable; it does NOT re-announce itself — the phase
        /// change re-keys the page, lands focus on the new content and CharGenAnnounce speaks the
        /// orientation line (the ProxyRoadmapEntry convention).</summary>
        public static NodeVtable RoadmapEntry(CharGenPhaseBaseVM vm)
        {
            Func<bool> available = () => vm != null && vm.IsAvailable.Value;
            Func<bool> current = () => vm != null && vm.IsSelected.Value;
            return new NodeVtable
            {
                ControlType = ControlTypes.Tab,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(() => vm?.PhaseName?.Value ?? ""),
                    GraphNodes.SelectedPart(current),
                    new NodeAnnouncement(() => vm != null && vm.IsCompleted.Value
                        ? Loc.T("value.completed") : null, live: true, kind: AnnouncementKinds.Value),
                    GraphNodes.DisabledPart(available),
                },
                SearchText = () => vm?.PhaseName?.Value ?? "",
                OnActivate = () =>
                {
                    // Can't "go to" the phase you're already on; locked phases can't be jumped to.
                    if (!available() || current()) return;
                    UiSound.Click();
                    vm.SetSelectedFromView(true);
                },
            };
        }

        /// <summary>One attribute row of the point-buy phase: "name, slider, value[, rank N of 2]
        /// [, recommended]"; Left/Right refunds/spends a point through the game's own
        /// RetreatStat/AdvanceStat (self-gated on CanRetreat/CanAdvance). Deliberately NO StateText
        /// and a non-live value part: CharGenAnnounce.OnStatAdvanced (the Harmony postfix on the
        /// game's advance handler) already speaks "name value. N points remaining." per real adjust —
        /// a second voice here would double-speak. Space opens the stat's own tooltip template.</summary>
        public static NodeVtable StatRow(CharGenAttributesItemVM vm)
        {
            Func<string> value = () =>
            {
                if (vm == null) return "";
                var s = vm.StatValue.Value.ToString();
                int ranks = vm.StatRanks.Value;
                if (ranks > 0) s += ", " + Loc.T("chargen.stat_ranks", new { ranks });
                if (vm.IsRecommended.Value) s += ", " + Loc.T("chargen.recommended");
                return s;
            };
            return new NodeVtable
            {
                ControlType = ControlTypes.Slider,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(() => vm?.DisplayName ?? ""),
                    new NodeAnnouncement(value, kind: AnnouncementKinds.Value),
                },
                SearchText = () => vm?.DisplayName ?? "",
                OnAdjust = (sign, large) =>
                {
                    if (vm == null) return;
                    if (sign < 0) vm.RetreatStat(); else vm.AdvanceStat();
                },
                OnTooltip = () => TooltipChooser.OpenTemplate(vm?.DisplayName, vm?.Tooltip?.Value),
            };
        }

        // ---- sequential (cycle) selectors ----
        // The appearance cyclers all derive from the engine's open-generic SequentialSelectorVM<T>
        // (Title / CurrentIndex / TotalCount / OnLeft / OnRight / IsAvailable — all on the generic
        // base, no shared non-generic accessor), so we drive them by reflection: the
        // ProxySequentialSelector idiom re-housed here.

        /// <summary>True if <paramref name="vm"/> is a sequential cycler (has the Left/Right + index API).</summary>
        public static bool SequentialHandles(object vm)
        {
            var t = vm?.GetType();
            return t != null && t.GetMethod("OnLeft") != null && t.GetMethod("OnRight") != null
                && t.GetProperty("CurrentIndex") != null && t.GetProperty("TotalCount") != null;
        }

        /// <summary>The cycler's IsAvailable (false = a single option — the game drops it from nav;
        /// callers skip emitting the node, the CanFocus=false parity).</summary>
        public static bool SequentialAvailable(object vm)
            => !(RpValue(Prop(vm, "IsAvailable")) is bool b) || b;

        /// <summary>A "&lt; value &gt;" cycler over one appearance component (gender, face/body type,
        /// skin/hair/…): "Title, slider, N of M" (or an explicit <paramref name="value"/> for the ones
        /// with a meaningful value — gender reads Male/Female off the doll); Left/Right cycle through
        /// the game's own OnLeft/OnRight (which play their own tick) and the step re-announces the new
        /// value synchronously.</summary>
        public static NodeVtable SequentialSelector(object vm, Func<string> value = null,
            string fallbackLabel = null)
        {
            Func<string> title = () =>
            {
                var s = RpValue(Prop(vm, "Title")) as string;
                return string.IsNullOrEmpty(s) ? (fallbackLabel ?? "") : s;
            };
            Func<string> val = value ?? (() => Loc.T("nav.position", new
            {
                index = (RpValue(Prop(vm, "CurrentIndex")) is int i ? i : 0) + 1,
                count = Prop(vm, "TotalCount") is int n ? n : 0,
            }));
            return new NodeVtable
            {
                ControlType = ControlTypes.Slider,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(title),
                    new NodeAnnouncement(val, live: true, kind: AnnouncementKinds.Value),
                },
                SearchText = title,
                StateText = val, // spoken (interrupting) after each step — keypress provenance
                OnAdjust = (sign, large) => Call(vm, sign < 0 ? "OnLeft" : "OnRight"),
            };
        }

        private static object RpValue(object rp) => rp?.GetType().GetProperty("Value")?.GetValue(rp);
        private static object Prop(object vm, string name) => vm?.GetType().GetProperty(name)?.GetValue(vm);
        private static void Call(object vm, string name) => vm?.GetType().GetMethod(name)?.Invoke(vm, null);
    }
}
