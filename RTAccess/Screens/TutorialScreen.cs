using Kingmaker;
using Kingmaker.Blueprints.Root.Strings; // UIStrings
using Kingmaker.Tutorial;
using Kingmaker.UI.MVVM.VM.Tutorial;
using RTAccess.UI;
using RTAccess.UI.Graph;
using UnityEngine;

namespace RTAccess.Screens
{
    /// <summary>
    /// A tutorial popup reader. Handles BOTH window kinds off the shared <see cref="TutorialWindowVM"/> base —
    /// the modal "big" window (<see cref="TutorialModalWindowVM"/>, multi-page) and the "small"/hint window
    /// (<see cref="TutorialHintWindowVM"/>). The basic-controls tutorials (movement/camera) render full-size
    /// but are the *small* kind (their blueprint's Windowed flag is false), so both go through here.
    ///
    /// Reads the current page's text, offers page navigation (modal, multi-page), a "don't show again"
    /// checkbox (only when the tutorial is bannable), and a Dismiss. The text is spoken on appearance even
    /// when Focus Mode is off — a blocking popup must never be silent; navigating the controls needs Focus
    /// Mode as usual.
    ///
    /// RT has no <c>ShowWindow</c> flag (unlike WotR's TutorialWindowVM): a window is "shown" while its
    /// reactive slot (<c>BigWindowVM</c>/<c>SmallWindowVM</c>) holds a VM with Data, and closing means
    /// <see cref="TutorialWindowVM.Hide"/> — whose callback disposes the slot, so our poll goes inactive and
    /// the screen pops.
    ///
    /// Graph-native: node keys carry the VM identity AND the page, so a new tutorial or a page flip drops
    /// the old keys — focus re-homes onto the text and the differ reads it (replacing the old
    /// rebuild+AnnounceCurrent dance). The focus-mode-OFF fallback delivery stays (the differ only speaks
    /// under Focus Mode), tracked by the spoken-VM/page markers in <see cref="OnUpdate"/>.
    /// </summary>
    public sealed class TutorialScreen : Screen
    {
        public TutorialScreen() { Wrap = true; } // Tab cycles text ↔ controls (modal-overlay convention, like MessageBox)

        public override string Key => "overlay.tutorial";
        // Modal popup: above windows/dialogue/settings, below the generic confirm modal (30).
        public override int Layer => 28;

        // The BIG window (TutorialModalWindowVM) is a blocking modal the game renders over gameplay — own the
        // keyboard like the game's other blocking modals (MessageBox/NameEntry) so exploration/scanner keys
        // don't leak under it. The small HINT window (TutorialHintWindowVM) is non-blocking, so it stays
        // non-exclusive and exploration keeps working beneath it. Polled live by InputManager.RebuildLive.
        public override bool Exclusive => Vm() is TutorialModalWindowVM;

        private bool _banOnClose;
        private TutorialWindowVM _spokenVm;    // focus-mode-OFF fallback delivery markers
        private TutorialData.Page _spokenPage;

        private static TutorialWindowVM Vm()
        {
            var tv = Game.Instance?.RootUiContext?.CommonVM?.TutorialVM;
            if (tv == null) return null;
            var big = tv.BigWindowVM.Value;
            if (big != null && big.Data != null) return big;
            var small = tv.SmallWindowVM.Value;
            if (small != null && small.Data != null) return small;
            return null;
        }

        public override bool IsActive() => Vm() != null;

        public override void OnPush() { _banOnClose = false; }
        public override void OnPop() { _banOnClose = false; _spokenVm = null; _spokenPage = null; }

        public override void OnFocus()
        {
            base.OnFocus();
            // With Focus Mode on, the differ reads the landing for us; cover the off case so a blocking
            // tutorial is never silent.
            if (!FocusMode.Active) SpeakText();
        }

        public override void OnUpdate()
        {
            // A new tutorial replacing the current one (Show fires back-to-back) resets the checkbox; the
            // focus-mode-off fallback speaks new content (under Focus Mode the key change re-homes focus
            // and the differ announces it).
            var vm = Vm();
            if (vm == null) return;
            if (vm != _spokenVm) _banOnClose = false;
            if (!FocusMode.Active && (vm != _spokenVm || CurrentPageOf(vm) != _spokenPage)) SpeakText();
            _spokenVm = vm;
            _spokenPage = CurrentPageOf(vm);
        }


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            var modal = vm as TutorialModalWindowVM;
            int page = modal != null ? modal.CurrentPageIndex.Value : 0;
            string k = "tutorial:" + vm.GetHashCode() + ":" + page + ":";

            // The same labeled level the old ListContainer provided ("Tutorial, list").
            b.PushContext(Loc.T("tutorial.title"), Loc.T("role.list"));
            b.AddItem(ControlId.Structural(k + "text"), GraphNodes.Text(() => PageText(Vm())));
            if (modal != null && modal.MultiplePages)
            {
                b.AddItem(ControlId.Structural(k + "prev"),
                    GraphNodes.Button(() => Loc.T("tutorial.prev_page"), () => StepPage(-1), CanPrev));
                b.AddItem(ControlId.Structural(k + "next"),
                    GraphNodes.Button(() => Loc.T("tutorial.next_page"), () => StepPage(1), CanNext));
            }
            if (vm.CanBeBanned)
                // The game silences this "don't show again" checkbox's hover (TutorialWindowBaseView NoSound)
                // and plays BanTutorialType on its click (TutorialWindowPCView) — our local flip bypasses both,
                // so replay them through the vtable sound slots.
                b.AddItem(ControlId.Structural(k + "ban"), GraphNodes.Toggle(
                    () => DontShowLabel(Vm()),
                    () => _banOnClose, () => _banOnClose = !_banOnClose,
                    hoverSound: Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum.NoSound,
                    activateSound: Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Tutorial?.BanTutorialType));
            b.AddItem(ControlId.Structural(k + "dismiss"),
                GraphNodes.Button(() => Loc.T("tutorial.dismiss"), Dismiss));
            b.PopContext();
        }

        private static void SpeakText()
        {
            var vm = Vm();
            // A modal popup demands attention → interrupt (unlike passive event speech).
            if (vm != null) Tts.Speak(Loc.T("tutorial.prefix", new { text = PageText(vm) }), interrupt: true);
        }

        private static TutorialData.Page CurrentPageOf(TutorialWindowVM vm)
        {
            if (vm is TutorialModalWindowVM m) return m.CurrentPage.Value;
            var pages = vm?.Pages;
            return pages != null && pages.Count > 0 ? pages[0] : null;
        }

        // The checkbox label the game's card shows: DontShowThisTutorial when the trigger bans the
        // tutorial itself, else the tag variant formatted with the tag's name (TutorialWindowBaseView).
        private static string DontShowLabel(TutorialWindowVM vm)
        {
            var t = UIStrings.Instance?.Tutorial;
            if (vm == null || t == null) return "";
            if (vm.BanTutorInsteadOfTag) return t.DontShowThisTutorial.Text;
            var tag = vm.TutorialTag;
            return string.Format(t.DontShowTutorialTag.Text,
                tag.HasValue ? t.TagNames.GetTagName(tag.Value)?.Text : null);
        }

        private static bool CanPrev() => Vm() is TutorialModalWindowVM m && m.CurrentPageIndex.Value > 0;
        private static bool CanNext() => Vm() is TutorialModalWindowVM m && m.CurrentPageIndex.Value < m.PageCount - 1;

        private static void StepPage(int dir)
        {
            if (!(Vm() is TutorialModalWindowVM m)) return;
            // The page index is part of every node key: this flip re-keys the graph, focus re-homes onto
            // the text node and the differ reads the new page — no manual speech.
            m.CurrentPageIndex.Value = Mathf.Clamp(m.CurrentPageIndex.Value + dir, 0, m.PageCount - 1);
        }

        private void Dismiss()
        {
            var vm = Vm();
            if (vm == null) return;
            if (_banOnClose) vm.BanTutor();
            vm.Hide(); // userInitiated: true — closes the game's window; its callback disposes the VM slot
        }

        private static string PageText(TutorialWindowVM vm)
        {
            if (vm == null) return "";
            if (vm is TutorialModalWindowVM m)
            {
                var prefix = m.MultiplePages
                    ? Loc.T("tutorial.page_of", new { index = m.CurrentPageIndex.Value + 1, count = m.PageCount }) + " "
                    : "";
                return prefix + FormatPage(m.CurrentPage.Value);
            }
            var pages = vm.Pages;
            if (pages == null || pages.Count == 0) return "";
            var parts = new List<string>();
            foreach (var p in pages) { var t = FormatPage(p); if (!string.IsNullOrEmpty(t)) parts.Add(t); }
            return string.Join(". ", parts.ToArray());
        }

        private static string FormatPage(TutorialData.Page page)
        {
            if (page == null) return "";
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(page.Title)) parts.Add(page.Title);
            if (!string.IsNullOrEmpty(page.TriggerText)) parts.Add(page.TriggerText);
            if (!string.IsNullOrEmpty(page.Description)) parts.Add(page.Description);
            if (!string.IsNullOrEmpty(page.SolutionText)) parts.Add(page.SolutionText);
            return string.Join(". ", parts.ToArray());
        }
    }
}
