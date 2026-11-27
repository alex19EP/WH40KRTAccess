using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.ActionBar;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Entities.Base;
using Kingmaker.Networking;
using Kingmaker.PubSubSystem;
using Kingmaker.UI.Common;
using Kingmaker.UI.Models.UnitSettings;
using RTAccess.Accessibility;
using RTAccess.Screens;
using RTAccess.UI;
using UnityEngine;
using Warhammer.SpaceCombat.Blueprints.Slots;

namespace RTAccess.Exploration;

internal sealed class CoopPings : INetPingPosition, INetPingEntity, INetPingActionBarAbility, INetStopPlayingHandler
{
    internal static readonly CoopPings Instance = new();
    private static readonly Dictionary<int, MapPing> ByPlayer = new();
    private static MapPing _latest;
    internal static IEnumerable<MapPing> Items => ByPlayer.Values;

    internal sealed class MapPing : ScanItem
    {
        internal MapPing(string name, Vector3 position) { Name = name; Position = position; }
        public override object Key => this;
        public override string Name { get; }
        public override Vector3 Position { get; }
        public override string Primary => "coop.pings";
        public override IEnumerable<string> Nodes { get { yield return Primary; } }
    }

    internal static void Clear()
    {
        ByPlayer.Clear();
        _latest = null;
    }

    public void HandleStopPlaying() => Clear();

    private static bool SurfaceSession => Main.Enabled && UINetUtility.InLobbyAndPlaying &&
        Game.Instance?.RootUiContext?.IsSurface == true;

    internal static void Send()
    {
        if (!SurfaceSession) { Unavailable(); return; }
        bool localMap = ScreenManager.Current is LocalMapScreen;
        if (!localMap && !(ScreenManager.Current is InGameScreen)) { Unavailable(); return; }
        if (!localMap && Navigation.HasFocus)
        {
            var slot = Navigation.FocusedReference as ActionBarSlotVM;
            var action = slot?.MechanicActionBarSlot;
            if (action?.Unit == null || string.IsNullOrWhiteSpace(action.KeyName)) { Unavailable(); return; }
            PhotonManager.Ping.PressPing(() => PhotonManager.Ping.PingActionBarAbility(
                action.KeyName, action.Unit, slot.Index, slot.WeaponSlotType));
            return;
        }

        var picked = localMap
            ? (LocalMapReview.SelectionFrame >= LocalMapCursor.TouchedFrame ? LocalMapReview.Selected : null)
            : (Scanner.SelectionFrame >= MapCursor.TouchedFrame ? Scanner.ResolveSelected() : null);
        if (picked != null && !picked.IsVisible) picked = null;
        if (picked?.Key is Entity entity)
            PhotonManager.Ping.PressPing(() => PhotonManager.Ping.PingEntity(entity));
        else
        {
            var position = picked?.Position ?? (localMap ? LocalMapCursor.Position : MapCursor.Position);
            PhotonManager.Ping.PressPing(() => PhotonManager.Ping.PingPosition(position));
        }
    }

    internal static void Review()
    {
        if (!SurfaceSession || _latest == null) { Tts.Speak(Loc.T("coop.no_ping"), true); return; }
        if (ScreenManager.Current is LocalMapScreen) LocalMapCursor.ReviewPing(_latest.Position);
        else if (ScreenManager.Current is InGameScreen) TileExplorer.PlantOn(_latest.Position);
    }

    private static void Unavailable() => Tts.Speak(Loc.T("coop.ping_unavailable"), true);

    public void HandlePingPosition(NetPlayer player, Vector3 position)
    {
        if (SurfaceSession) Record(player, position, Loc.T("coop.tile"));
    }

    public void HandlePingPositionSound(GameObject gameObject) { }

    public void HandlePingEntity(NetPlayer player, Entity entity)
    {
        if (!SurfaceSession || entity == null) return;
        var item = WorldModel.Find(entity);
        if (item?.CurrentlySeen == true) Record(player, entity.Position, item.Name);
        else if (entity.IsVisibleForPlayer) Record(player, entity.Position, Loc.T("coop.tile"));
    }

    private static void Record(NetPlayer player, Vector3 position, string target)
    {
        string label = Loc.T("coop.map_ping", new { player = PlayerName(player), target });
        _latest = new MapPing(label, position);
        ByPlayer[player.Index] = _latest;
        Tts.Speak(label + ", " + InteractableDescriber.DirectionAndDistance(MapCursor.PlayerPosition, position));
    }

    public void HandlePingActionBarAbility(NetPlayer player, string keyName, Entity characterEntityRef,
        int slotIndex, WeaponSlotType weaponSlotType)
    {
        if (!SurfaceSession || !(characterEntityRef is BaseUnitEntity unit) || !unit.IsInPlayerParty ||
            string.IsNullOrWhiteSpace(keyName)) return;
        var displayed = InGameScreen.BarSlots().Select(row => row.vm).FirstOrDefault(slot =>
            slot?.MechanicActionBarSlot?.Unit == unit && slot.Index == slotIndex &&
            slot.WeaponSlotType == weaponSlotType && slot.MechanicActionBarSlot.KeyName == keyName);
        // The receiver may have a different character selected. Resolve the sender's slot on its owner.
        var action = displayed?.MechanicActionBarSlot ?? unit.UISettings?.Slots?.FirstOrDefault(slot =>
            slot != null && slot.KeyName == keyName);
        string title = action?.GetTitle();
        if (string.IsNullOrWhiteSpace(title) && slotIndex >= 0)
        {
            // Weapon attacks are generated from the equipped hands and aren't in UISettings.Slots.
            var hands = unit.Body?.CurrentHandsEquipmentSet;
            foreach (var hand in new[] { hands?.PrimaryHand, hands?.SecondaryHand })
            {
                var attacks = hand?.MaybeItem?.Abilities;
                if (attacks == null || slotIndex >= attacks.Count || attacks[slotIndex]?.Data == null) continue;
                var weaponSlot = new MechanicActionBarSlotAbility { Ability = attacks[slotIndex].Data, Unit = unit };
                if (weaponSlot.KeyName != keyName) continue;
                title = weaponSlot.GetTitle();
                break;
            }
        }
        Tts.Speak(Loc.T("coop.ability_ping", new { player = PlayerName(player),
            ability = string.IsNullOrWhiteSpace(title) ? Loc.T("coop.ability_unknown") : title, unit = unit.CharacterName }));
    }

    private static string PlayerName(NetPlayer player)
    {
        foreach (var info in PhotonManager.Instance.ActivePlayers)
            if (info.NetPlayer.Index == player.Index && !string.IsNullOrWhiteSpace(info.NickName)) return info.NickName;
        return Loc.T("coop.unknown_player");
    }
}
