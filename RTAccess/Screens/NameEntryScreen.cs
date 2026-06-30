using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.MessageBox;
using Kingmaker.UI.MVVM.VM.CharGen;
using Kingmaker.UI.MVVM.VM.CharGen.Phases.Ship;
using Kingmaker.UI.MVVM.VM.CharGen.Phases.Summary;
using RTAccess.UI;
using RTAccess.UI.Proxies;
using TMPro;

namespace RTAccess.Screens
{
    /// <summary>
    /// The character/ship name-entry modal (the chargen change-name <see cref="MessageBoxVM"/>, a TextField
    /// box created by the Summary/Ship phases). Resolves whichever phase's box is currently open and lets
    /// the player TYPE a custom name: "Edit" hands the game's own TMP input field to <see cref="TextEntry"/>
    /// (so Unity/TMP own caret, Unicode and IME), Random rolls one, Apply confirms, Back cancels. Layer 32,
    /// Exclusive — sits above the chargen flow and owns the keyboard while open.
    /// </summary>
    public sealed class NameEntryScreen : Screen
    {
        public override string Key => "overlay.nameentry";
        public override int Layer => 32;
        public override bool Exclusive => true;
        public override string ScreenName => Loc.T("chargen.name_entry");

        // The change-name box of whichever chargen phase is current (Summary = character, Ship = vessel).
        private static MessageBoxVM Box()
        {
            var cg = Game.Instance?.RootUiContext?.MainMenuVM?.CharGenContextVM?.CharGenVM?.Value;
            var phase = cg?.CurrentPhaseVM.Value;
            if (phase is CharGenSummaryPhaseVM s) return s.CharGenNameVM?.MessageBoxVM?.Value;
            if (phase is CharGenShipPhaseVM sh) return sh.MessageBoxVM?.Value;
            return null;
        }

        public override bool IsActive() => Box() != null;

        private MessageBoxVM _box;

        public override void OnPush() { _box = Box(); Build(); }
        public override void OnPop() { Clear(); _box = null; }

        public override void OnUpdate()
        {
            var box = Box();
            if (!ReferenceEquals(box, _box))
            {
                _box = box;
                Build();
                Navigation.Attach(this);
                if (FocusMode.Active) Navigation.AnnounceCurrent();
            }
        }

        private void Build()
        {
            Clear();
            var box = _box;
            if (box == null) return;

            // Edit — label is the live current name; activating hands the game's field to TextEntry to type.
            Add(new ProxyActionButton(() => Loc.T("chargen.character_name", new { value = box.InputText.Value ?? "" }),
                () => true, StartEditing, actionVerb: "edit"));
            if (box is CharGenChangeNameMessageBoxVM cn)
                Add(new ProxyActionButton(() => Loc.T("chargen.random_name"), () => true, () => cn.SetRandomName()));
            Add(new ProxyActionButton(() => box.AcceptText, () => true, () => box.OnAcceptPressed()));
        }

        // Back/Escape cancels the box (the game's Decline path).
        public override IEnumerable<ElementAction> GetActions()
        {
            var box = _box;
            if (box != null)
                yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.cancel"),
                    _ => box.OnDeclinePressed());
        }

        private void StartEditing()
        {
            var field = FindInputField(_box);
            if (field != null) TextEntry.Begin(field, Loc.T("modal.name"));
            else Tts.Speak(Loc.T("text.unavailable"));
        }

        // The game shows the modal with a real TMP_InputField bound to InputText; grab the active one (prefer
        // the field whose text already matches, in case any other input field is alive).
        private static TMP_InputField FindInputField(MessageBoxVM box)
        {
            var all = UnityEngine.Object.FindObjectsByType<TMP_InputField>(UnityEngine.FindObjectsSortMode.None);
            TMP_InputField fallback = null;
            foreach (var f in all)
            {
                if (f == null || !f.isActiveAndEnabled || !f.IsInteractable()) continue;
                if (box != null && f.text == box.InputText.Value) return f;
                if (fallback == null) fallback = f;
            }
            return fallback;
        }
    }
}
