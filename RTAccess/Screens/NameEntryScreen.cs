using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.MessageBox;
using Kingmaker.UI.MVVM.VM.CharGen;
using Kingmaker.UI.MVVM.VM.CharGen.Phases.Ship;
using Kingmaker.UI.MVVM.VM.CharGen.Phases.Summary;
using RTAccess.UI;
using RTAccess.UI.Graph;
using TMPro;

namespace RTAccess.Screens
{
    /// <summary>
    /// The character/ship name-entry modal (the chargen change-name <see cref="MessageBoxVM"/>, a TextField
    /// box created by the Summary/Ship phases), graph-native. Resolves whichever phase's box is currently
    /// open and lets the player TYPE a custom name: the name field is a <see cref="CharGenNodes.TextField"/>
    /// node driving the game's own TMP input field through <see cref="TextEntry"/> (so Unity/TMP own caret,
    /// Unicode and IME), Random rolls one (spoken), Apply confirms, Back cancels. Layer 32, Exclusive —
    /// sits above the chargen flow and owns the keyboard while open.
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

        // Back/Escape cancels the box (the game's Decline path).
        public override IEnumerable<ElementAction> GetActions()
        {
            var box = Box();
            if (box != null)
                yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.cancel"),
                    _ => box.OnDeclinePressed());
        }

        public override bool BuildsGraph => true;

        public override void Build(GraphBuilder b)
        {
            var box = Box();
            if (box == null) return;
            string k = "name:" + box.GetHashCode() + ":";

            // The name field — Enter hands the game's live TMP field to TextEntry to type; the value
            // reads back on focus (and after the edit commits via the field's game-wired onEndEdit).
            b.AddItem(ControlId.Structural(k + "field"),
                CharGenNodes.TextField(Loc.T("modal.name"),
                    () => FindInputField(box),
                    () => box.InputText.Value));

            if (box is CharGenChangeNameMessageBoxVM cn)
                b.AddItem(ControlId.Structural(k + "random"),
                    GraphNodes.Button(() => Loc.T("chargen.random_name"), () =>
                    {
                        // SetRandomName writes InputText (bound to the visible field); speak the roll —
                        // keypress-caused, so it interrupts. Game content passes through unlocalized.
                        cn.SetRandomName();
                        var name = cn.InputText.Value;
                        if (!string.IsNullOrEmpty(name)) Tts.Speak(name, interrupt: true);
                    }));

            // Apply — the game's own accept label + path (commits InputText through the box callback).
            b.AddItem(ControlId.Structural(k + "accept"),
                GraphNodes.Button(() => box.AcceptText, () => box.OnAcceptPressed()));
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
