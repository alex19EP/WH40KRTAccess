using Access.Core.Graph;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings;
using Kingmaker.UI.MVVM.VM.NetRoles;
using RTAccess.UI;

namespace RTAccess.Screens;

public sealed class NetRolesScreen : Screen
{
    private static NetRolesVM Vm => Game.Instance?.RootUiContext?.CommonVM?.NetRolesVM?.Value;
    public override string Key => "coop.roles";
    public override int Layer => 27;
    public override bool Exclusive => true;
    public override string ScreenName => UIStrings.Instance.EscapeMenu.EscMenuRoles.Text;
    public override bool IsActive() => Vm != null;
    public NetRolesScreen() { Wrap = true; }

    public override IEnumerable<ElementAction> GetActions()
    {
        var vm = Vm;
        if (vm != null)
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => vm.OnClose());
    }

    public override void Build(GraphBuilder b)
    {
        var vm = Vm;
        if (vm == null) return;
        for (int i = 0; i < vm.PlayerVms.Count; i++)
        {
            var owner = vm.PlayerVms[i];
            string ownerKey = "player:" + owner.UserId.Value;
            b.BeginStop(ownerKey).PushContext(NetLobbyScreen.PlayerName(owner));
            bool any = false;
            foreach (var role in owner.Players)
            {
                if (!role.PlayerRoleMe.Value || !(role.Character.Entity is Kingmaker.EntitySystem.Entities.BaseUnitEntity unit)) continue;
                any = true;
                var name = unit.CharacterName;
                string key = ownerKey + ":" + role.Character.Id;
                b.AddLabel(ControlId.Structural(key), () => name);
                if (role.CanUp.Value && i > 0)
                    AddMove(b, key + ":up", name, vm.PlayerVms[i - 1], role.MoveRoleCharacterUp);
                if (role.CanDown.Value && i + 1 < vm.PlayerVms.Count)
                    AddMove(b, key + ":down", name, vm.PlayerVms[i + 1], role.MoveRoleCharacterDown);
            }
            if (!any) b.AddLabel(ControlId.Structural(ownerKey + ":empty"), () => Loc.T("coop.no_units"));
            b.PopContext();
        }
        b.BeginStop("close").AddItem(ControlId.Structural("roles:close"), GraphNodes.Button(
            () => Loc.T(vm.IsRoomOwner ? "coop.roles_done" : "action.close"), vm.OnClose));
    }

    private static void AddMove(GraphBuilder b, string key, string character, NetRolesPlayerVM target, Action move)
        => b.AddItem(ControlId.Structural(key), GraphNodes.Button(
            () => Loc.T("coop.assign", new { character, player = NetLobbyScreen.PlayerName(target) }), move));
}
