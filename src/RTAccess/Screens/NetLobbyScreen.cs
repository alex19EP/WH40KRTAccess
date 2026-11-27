using Access.Core.Graph;
using Kingmaker;
using Kingmaker.Blueprints.Root.Strings;
using Kingmaker.UI.MVVM.View.NetLobby.PC;
using Kingmaker.UI.MVVM.VM.NetLobby;
using RTAccess.UI;

namespace RTAccess.Screens;

public sealed class NetLobbyScreen : Screen
{
    private static NetLobbyVM Vm => Game.Instance?.RootUiContext?.CommonVM?.NetLobbyVM?.Value;
    private static NetLobbyPCView View => LiveView.Find<NetLobbyPCView>();
    public override string Key => "coop.lobby";
    public override int Layer => 26;
    public override bool Exclusive => true;
    public override bool IsActive() => Vm != null;
    public override string ScreenName => UIStrings.Instance.NetLobbyTexts.NetHeader.Text;
    public NetLobbyScreen() { Wrap = true; }

    public override IEnumerable<ElementAction> GetActions()
    {
        var vm = Vm;
        if (vm != null && !vm.NetLobbyTutorialOnScreen.Value)
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => vm.OnClose());
    }

    private static void Button(GraphBuilder b, string key, string name, Action action, Func<bool> enabled = null)
        => b.AddItem(ControlId.Structural(key), GraphNodes.Button(() => name, action, enabled));

    public override void Build(GraphBuilder b)
    {
        var vm = Vm;
        var view = View;
        if (vm == null || view == null || !view.isActiveAndEnabled) return;
        var texts = UIStrings.Instance.NetLobbyTexts;
        if (vm.NetLobbyTutorialOnScreen.Value)
        {
            var tutorial = vm.NetLobbyTutorialPartVM.Value;
            if (tutorial == null) return;
            int i = 0;
            b.BeginStop("tutorial").PushContext(texts.HowToPlay.Text);
            foreach (var block in tutorial.TutorialBlocksVMs.Take(view.m_TutorialBlock.m_ShowedBlock))
                b.AddLabel(ControlId.Structural("tutorial:" + i++), () => block.BlockDescription);
            b.PopContext();
            Button(b.BeginStop("continue"), "continue", UIStrings.Instance.CommonTexts.Accept.Text, view.m_TutorialBlock.ShowBlock);
            return;
        }

        if (vm.SaveSlotCollectionVm.Value != null)
            BuildSaves(b, vm, view.m_LobbyBlock);
        else if (view.m_CreateJoinBlock.isActiveAndEnabled)
            BuildEntry(b, vm, view.m_CreateJoinBlock);
        else if (view.m_LobbyBlock.isActiveAndEnabled)
            BuildRoom(b, vm, view.m_LobbyBlock);
        else
            b.AddLabel(ControlId.Structural("connecting"), () => texts.ConnectingLabel.Text);

        Button(b.BeginStop("close"), "close", UIStrings.Instance.CommonTexts.Back.Text, vm.OnClose);
    }

    private static void BuildEntry(GraphBuilder b, NetLobbyVM vm, NetLobbyCreateJoinPartPCView entry)
    {
        var texts = UIStrings.Instance.NetLobbyTexts;
        var regions = vm.RegionDropdownVM.Value;
        b.BeginStop("connect");
        Button(b, "create", texts.CreateLobby.Text, vm.CreateLobby, () => regions != null);
        Button(b, "paste", texts.PasteLobbyId.Text, () =>
        {
            var code = (vm.GetCopiedLobbyId() ?? "").Trim();
            entry.m_LobbyCodeInputField.text = code;
            vm.SetLobbyCode(code);
            Tts.Speak(Loc.T(vm.HasCodeForLobby.Value ? "coop.valid" : "coop.invalid"), interrupt: true);
        });
        Button(b, "edit", texts.JoinLobbyCodePlaceholder.Text,
            () => TextEntry.Begin(entry.m_LobbyCodeInputField, texts.JoinLobbyCodePlaceholder.Text));
        Button(b, "reveal-entry", entry.ShowLobbyCode.Value ? texts.HideLobbyCode.Text : texts.ShowLobbyCode.Text,
            () => entry.m_ShowHideLobbyCodeButton.OnLeftClick.Invoke());
        Button(b, "join", texts.JoinLobby.Text, vm.JoinLobby, () => entry.ReadyToJoin.Value);
        if (regions == null) return;
        b.BeginStop("regions").PushContext(texts.RegionHeader.Text);
        foreach (var region in regions.VMCollection.OfType<NetLobbyRegionDropdownVM>())
        {
            Button(b, "region:" + region.Region, region.Text +
                (regions.SelectedVM.Value == region ? ", " + Loc.T("state.selected") : ""), () => regions.SetSelected(region));
        }
        b.PopContext();
    }

    internal static string PlayerName(NetLobbyPlayerVM p)
        => p.IsMe.Value ? Loc.T("coop.you", new { name = p.Name.Value }) : p.Name.Value;

    private static void BuildRoom(GraphBuilder b, NetLobbyVM vm, NetLobbyLobbyPartPCView room)
    {
        var texts = UIStrings.Instance.NetLobbyTexts;
        b.BeginStop("room");
        Button(b, "copy", texts.CopyLobbyId.Text, () =>
        {
            room.CopyLobbyId();
            Tts.Speak(Loc.T("coop.copy_done"), interrupt: true);
        });
        Button(b, "reveal-code", room.ShowCode.Value ? texts.HideLobbyCode.Text : texts.ShowLobbyCode.Text,
            room.ShowHideLobbyId);
        if (room.ShowCode.Value)
            b.AddLabel(ControlId.Structural("code"), () => vm.LobbyCode.Value);
        if (vm.CurrentSave.Value is { } save)
            b.AddLabel(ControlId.Structural("selected-save"), () => SaveLoadScreen.SlotBrowseLabel(save));
        if (room.m_ResetCurrentSave.gameObject.activeInHierarchy)
            Button(b, "reset", texts.ResetCurrentSave.Text, vm.ResetCurrentSave);
        if (room.m_EmptySaveSlotButton.gameObject.activeInHierarchy)
            Button(b, "saves", texts.ChooseSaveHeader.Text, vm.ChooseSave);
        if (room.m_LaunchButton.gameObject.activeInHierarchy)
            Button(b, "launch", room.m_LaunchButtonText.text, room.m_LaunchButton.OnLeftClick.Invoke,
                () => room.m_LaunchButton.Interactable);
        if (!vm.IsHost.Value)
            b.AddLabel(ControlId.Structural("wait"), () => Loc.T("coop.wait_host"));
        if (!vm.IsEnoughPlayersForGame.Value)
            b.AddLabel(ControlId.Structural("not-enough"), () => texts.IsNotEnoughPlayersForGame.Text);
        if (vm.IsSaveTransfer.Value)
            b.AddLabel(ControlId.Structural("transfer"), () => Loc.T("coop.transfer_progress",
                new { percent = (int)(100d * vm.SaveTransferProgress.Value / Math.Max(1, vm.SaveTransferTarget.Value)) }));
        if (!string.IsNullOrWhiteSpace(vm.PlayersDifferentMods.Value))
            b.AddLabel(ControlId.Structural("mods"), () => vm.PlayersDifferentMods.Value);

        b.BeginStop("players").PushContext(Loc.T("coop.players"));
        foreach (var player in vm.PlayerVms.Where(p => !p.IsEmpty.Value))
        {
            b.AddLabel(ControlId.Referenced(player, "player:" + player.UserId.Value), () => PlayerName(player));
            if (!string.IsNullOrWhiteSpace(player.PlayersDifferentDlcs.Value))
                b.AddLabel(ControlId.Structural("dlc:" + player.UserId.Value), () => player.PlayersDifferentDlcs.Value);
        }
        b.PopContext();
    }

    private static void BuildSaves(GraphBuilder b, NetLobbyVM vm, NetLobbyLobbyPartPCView room)
    {
        b.BeginStop("saves").PushContext(UIStrings.Instance.NetLobbyTexts.ChooseSaveHeader.Text);
        foreach (var slot in vm.m_SaveSlotVMs)
            b.AddItem(ControlId.Referenced(slot, slot.Reference.Name), GraphNodes.Button(
                () => SaveLoadScreen.SlotBrowseLabel(slot), () => vm.SetSelectSaveAction(slot.Reference)));
        if (vm.ShowWaitingSaveAnim.Value)
            b.AddLabel(ControlId.Structural("loading"), () => Loc.T("coop.loading_saves"));
        else if (vm.SaveListAreEmpty.Value)
            b.AddLabel(ControlId.Structural("empty"), () => vm.ProblemsToShowInSaveList.Count > 0
                ? room.m_EmptyListHintBecauseDlcs.text : room.m_EmptyListHint.text);
        b.PopContext();
    }

    private string _lastStatus;
    private float _nextStatusCheck;
    public override void OnPop() => _lastStatus = null;

    public override void OnUpdate()
    {
        var vm = Vm;
        if (UnityEngine.Time.unscaledTime < _nextStatusCheck) return;
        _nextStatusCheck = UnityEngine.Time.unscaledTime + 0.25f;
        if (vm == null || !vm.IsInRoom.Value || vm.NetLobbyTutorialOnScreen.Value)
        {
            _lastStatus = null;
            return;
        }
        var room = View?.m_LobbyBlock;
        var parts = vm.PlayerVms.Where(p => !p.IsEmpty.Value).Select(PlayerName).ToList();
        if (vm.IsSaveTransfer.Value) parts.Add(Loc.T("coop.transfer"));
        else if (!vm.IsHost.Value) parts.Add(Loc.T("coop.wait_host"));
        else if (room != null && room.m_LaunchButton.isActiveAndEnabled && room.m_LaunchButton.Interactable)
            parts.Add(room.m_LaunchButtonText.text);
        if (vm.CurrentSave.Value is { } save) parts.Add(save.SaveName.Value);
        if (!string.IsNullOrEmpty(vm.PlayersDifferentMods.Value)) parts.Add(vm.PlayersDifferentMods.Value);
        string status = string.Join(", ", parts);
        if (status != _lastStatus) Tts.Speak(status);
        _lastStatus = status;
    }
}
