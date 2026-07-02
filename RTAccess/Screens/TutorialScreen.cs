using Kingmaker;
using Kingmaker.Tutorial;
using Kingmaker.UI.MVVM.VM.Tutorial;
using RTAccess.UI;
using RTAccess.UI.Proxies;
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
    /// the screen pops. Everything reads through the live <see cref="Vm"/>, so a page step or a VM swap needs
    /// no rebuild (labels/enabled/text are all live delegates).
    /// </summary>
    public sealed class TutorialScreen : Screen
    {
        public TutorialScreen() { Wrap = true; } // Tab cycles text ↔ controls

        public override string Key => "overlay.tutorial";
        // Modal popup: above windows/dialogue/settings, below the generic confirm modal (30).
        public override int Layer => 28;

        // The BIG window (TutorialModalWindowVM) is a blocking modal the game renders over gameplay — own the
        // keyboard like the game's other blocking modals (MessageBox/NameEntry) so exploration/scanner keys
        // don't leak under it. The small HINT window (TutorialHintWindowVM) is non-blocking, so it stays
        // non-exclusive and exploration keeps working beneath it. Polled live by InputManager.RebuildLive.
        public override bool Exclusive => Vm() is TutorialModalWindowVM;

        private TutorialWindowVM _builtVm;
        private bool _banOnClose;

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

        public override void OnPush() { _banOnClose = false; Build(); }
        public override void OnPop() { Clear(); _builtVm = null; _banOnClose = false; }

        public override void OnFocus()
        {
            base.OnFocus();
            // With Focus Mode on, ScreenManager lands focus and reads the text element for us; cover the
            // off case so a blocking tutorial is never silent.
            if (!FocusMode.Active) SpeakText();
        }

        public override void OnUpdate()
        {
            var vm = Vm();
            if (vm == null || vm == _builtVm) return;
            // A new tutorial replaced the current one without the screen popping (Show fires back-to-back) —
            // rebuild and re-home focus.
            _banOnClose = false;
            Build();
            Navigation.Attach(this);
            if (FocusMode.Active) Navigation.AnnounceCurrent();
            else SpeakText();
        }

        private void Build()
        {
            Clear();
            var vm = Vm();
            _builtVm = vm;
            if (vm == null) return;

            var list = new ListContainer(Loc.T("tutorial.title"));
            list.Add(new TextElement(() => PageText(Vm()))); // live current-page text — focus to re-read
            if (vm is TutorialModalWindowVM modal && modal.MultiplePages)
            {
                list.Add(new ProxyActionButton(() => Loc.T("tutorial.prev_page"), CanPrev, () => StepPage(-1)));
                list.Add(new ProxyActionButton(() => Loc.T("tutorial.next_page"), CanNext, () => StepPage(1)));
            }
            if (vm.CanBeBanned)
                // The game silences this "don't show again" checkbox's hover (TutorialWindowBaseView NoSound)
                // and plays BanTutorialType on its click (TutorialWindowPCView) — our local flip bypasses both,
                // so replay them: NoSound hover + the ban sting on toggle.
                list.Add(new ProxyBoolToggle(Loc.T("tutorial.dont_show"),
                    () => _banOnClose, () => _banOnClose = !_banOnClose,
                    hoverSoundType: Kingmaker.UI.Sound.UISounds.ButtonSoundsEnum.NoSound,
                    activateSound: () => Kingmaker.UI.Sound.UISounds.Instance?.Sounds?.Tutorial?.BanTutorialType));
            list.Add(new ProxyActionButton(Loc.T("tutorial.dismiss"), null, Dismiss));
            Add(list);
        }

        private static void SpeakText()
        {
            var vm = Vm();
            // A modal popup demands attention → interrupt (unlike passive event speech).
            if (vm != null) Tts.Speak(Loc.T("tutorial.prefix", new { text = PageText(vm) }), interrupt: true);
        }

        private static bool CanPrev() => Vm() is TutorialModalWindowVM m && m.CurrentPageIndex.Value > 0;
        private static bool CanNext() => Vm() is TutorialModalWindowVM m && m.CurrentPageIndex.Value < m.PageCount - 1;

        private static void StepPage(int dir)
        {
            if (!(Vm() is TutorialModalWindowVM m)) return;
            m.CurrentPageIndex.Value = Mathf.Clamp(m.CurrentPageIndex.Value + dir, 0, m.PageCount - 1);
            // Keypress-driven page change → interrupt and read the new page (focus stays on the step button).
            Tts.Speak(PageText(m), interrupt: true);
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
