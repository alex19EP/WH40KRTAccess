using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.GroupChanger;     // GroupChangerVM, GroupChangerCharacterVM, GroupChangerCommonVM
using Kingmaker.GameCommands;                     // AcceptChangeGroup, CloseChangeGroupGameCommand
using Kingmaker.UI.Common;                        // UINetUtility
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The party picker (<c>GroupChangerContextVM.GroupChangerVm</c>, Surface OR Space) — the window a
    /// GroundOperation POI raises before loading the ground map, and capital/area-required party changes
    /// elsewhere. Without this screen the picker was a silent wall in front of every ground op.
    ///
    /// Two lists mirroring the game's: <b>In party</b> and <b>Reserve</b>; Enter on a member moves them
    /// across (the character card's own Click command → MoveCharacter), locked/required members announce and
    /// refuse like the game's lock overlay. Confirm replicates the view's own Go: the net-gated
    /// <c>AcceptChangeGroup</c> command with the VM's current lists. Escape replicates its Cancel — allowed
    /// only when the VM's own CloseCondition holds (a REQUIRED picker cannot be dismissed, by design).
    /// </summary>
    public sealed class GroupChangerScreen : Screen
    {
        public override string Key => "groupchanger";
        public override int Layer => 27;            // above the tablet (9) and its sub-modals (26)
        public override bool Exclusive => true;
        public override string ScreenName => Loc.T("groupchanger.screen");

        public override bool IsActive() => Vm() != null;

        private static GroupChangerVM Vm()
        {
            var ctx = Game.Instance?.RootUiContext;
            return ctx?.SurfaceVM?.StaticPartVM?.GroupChangerContextVM?.GroupChangerVm?.Value
                ?? ctx?.SpaceVM?.StaticPartVM?.GroupChangerContextVM?.GroupChangerVm?.Value;
        }

        public override bool BuildsGraph => true;

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "gc:" + vm.GetHashCode() + ":";

            b.BeginStop("party").PushContext(Loc.T("groupchanger.party"), role: "list");
            int i = 0;
            foreach (var ch in vm.PartyCharacter)
            {
                var c = ch; // capture
                if (c == null) continue;
                b.AddItem(ControlId.Referenced(c, k + "p:" + i++), CharacterNode(c));
            }
            b.PopContext();

            b.BeginStop("reserve").PushContext(Loc.T("groupchanger.reserve"), role: "list");
            int j = 0;
            bool anyRemote = false;
            foreach (var ch in vm.RemoteCharacter)
            {
                var c = ch; // capture
                if (c == null) continue;
                b.AddItem(ControlId.Referenced(c, k + "r:" + j++), CharacterNode(c));
                anyRemote = true;
            }
            if (!anyRemote)
                b.AddLabel(ControlId.Structural(k + "r:none"), () => Loc.T("groupchanger.reserve_empty"));
            b.PopContext();

            b.BeginStop("confirm");
            b.AddItem(ControlId.Structural(k + "go"), GraphNodes.Button(
                () => Loc.T("groupchanger.confirm"),
                () => Accept(vm)));
        }

        // One member row: Enter moves them between party and reserve — the card's own Click command
        // (GroupChangerCommonVM subscribes it to MoveCharacter). Locked = required by the operation.
        // Space reads the tooltip-only detail (active effects, an overload warning) the card conveys
        // only as icons.
        private static NodeVtable CharacterNode(GroupChangerCharacterVM c)
        {
            var vt = GraphNodes.Button(() => CharLabel(c), () => c.Click.Execute(c), () => !c.IsLock.Value);
            vt.OnTooltip = () => TooltipScreen.Open(c.CharacterName, Detail(c));
            return vt;
        }

        // The browse-label mirrors the card: name, the level number the card prints, and the badges it
        // overlays (lock = required-in-party, the level-up flag). In-party vs reserve is conveyed by
        // which list the card sits in (the two Tab-stop headers) — how the two columns read visually.
        private static string CharLabel(GroupChangerCharacterVM c)
        {
            string s = c.CharacterName + ", " + Loc.T("groupchanger.level", new { level = c.CharacterLevel });
            if (c.IsLock.Value) s += ", " + Loc.T("groupchanger.required");
            if (c.IsLevelUp) s += ", " + Loc.T("groupchanger.levelup");
            return s;
        }

        // Space detail — the tooltip-only fields the card only hints at with icons: an overload warning
        // and the active effects (the buff row). Kept off the browse-label to keep browsing terse.
        private static string Detail(GroupChangerCharacterVM c)
        {
            var lines = new List<string>();
            if (c.IsCharacterOverload) lines.Add(Loc.T("groupchanger.overloaded"));
            var names = new List<string>();
            var buffs = c.BuffPartVm?.Buffs;
            if (buffs != null)
                foreach (var b in buffs)
                {
                    string n = b?.Buff?.Name;
                    if (!string.IsNullOrWhiteSpace(n)) names.Add(n);
                }
            lines.Add(names.Count > 0
                ? Loc.T("groupchanger.effects", new { list = string.Join(", ", names) })
                : Loc.T("groupchanger.no_effects"));
            return string.Join("\n", lines);
        }

        // The view's own OnAccept: net-gate, then the AcceptChangeGroup command with the VM's live lists.
        private static void Accept(GroupChangerVM vm)
        {
            if (!UINetUtility.IsControlMainCharacterWithWarning()) return;
            Game.Instance.GameCommandQueue.AcceptChangeGroup(
                vm.PartyCharacterRef.ToList(), vm.RemoteCharacterRef.ToList(),
                vm.RequiredCharactersRef.ToList(), vm.IsCapital, vm is GroupChangerCommonVM);
        }

        // The view's own OnCancel — gated on the VM's CloseCondition (a required picker refuses, spoken).
        public override IEnumerable<ElementAction> GetActions()
        {
            var vm = Vm();
            if (vm == null) yield break;
            yield return new ElementAction(ActionIds.Back, Message.Raw(GameText.Action("close")), _ =>
            {
                if (!UINetUtility.IsControlMainCharacterWithWarning()) return;
                if (!vm.CloseEnabled.Value || !vm.CloseCondition())
                {
                    Tts.Speak(Loc.T("groupchanger.cannot_close"), interrupt: true);
                    return;
                }
                Game.Instance.GameCommandQueue.AddCommand(new CloseChangeGroupGameCommand());
            });
        }
    }
}
