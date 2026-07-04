using Kingmaker;
using Kingmaker.GameModes;
using Kingmaker.Globalmap.Blueprints;
using Kingmaker.Globalmap.SystemMap;
using Kingmaker.PubSubSystem;
using Kingmaker.PubSubSystem.Core;
using RTAccess.Speech;
using UnityEngine;

namespace RTAccess.Accessibility;

/// <summary>
/// Long-lived EventBus subscriber + per-frame poll voicing SYSTEM-MAP travel state (the in-system space map,
/// <see cref="GameModeType.StarSystem"/>): ship movement start/stop, object-scan completion, system research
/// progress, and the game's proximity "noise" cues. Subscribed once at mod load, unsubscribed at unload (see
/// <see cref="Main"/>); <see cref="Tick"/> rides Main.OnUpdate. Plan: docs/plans/orbital-listing-wilkes.md (M1).
///
/// All lines are passive/event speech → QUEUED (interrupt: false), per [[rt-interrupt-speech-rule]]. The one
/// exception is handled by omission: when the move was commanded from our own screen (which already spoke
/// "Traveling to X" on the keypress), <see cref="MarkCommandedMove"/> suppresses the redundant started-line.
/// XP from scanning is NOT voiced here — the game logs it and LogTap already speaks the log (M0 finding).
/// </summary>
internal sealed class SpaceEvents :
    IStarSystemShipMovementHandler,
    IScanStarSystemObjectHandler,
    IStarSystemMapResearchProgress
{
    internal static readonly SpaceEvents Instance = new SpaceEvents();

    /// <summary>Last research % the game recomputed for the current system (already 0–100, the value
    /// SystemTitleView renders as "{value}%"). −1 until the first recalc. Read by the screen's status line.</summary>
    internal static int ResearchPercent = -1;

    private static float _commandedMoveTime = -10f;

    // Rising-edge state for the proximity cues (the game's three HUD "interference" icons).
    private bool _nearAnomaly, _nearPoi, _nearResources;

    /// <summary>Called by the screen right before it issues a MoveShip command it has already announced —
    /// the game's movement-started event (raised when the command executes, frames later) then stays silent.</summary>
    internal static void MarkCommandedMove() => _commandedMoveTime = Time.unscaledTime;

    private static bool OnSystemMap => Game.Instance?.CurrentMode == GameModeType.StarSystem;

    // ---- ship movement ----

    public void HandleStarSystemShipMovementStarted()
    {
        if (Time.unscaledTime - _commandedMoveTime < 1f) return; // our screen already spoke the destination
        Speaker.Speak(Loc.T("systemmap.traveling"), interrupt: false);
    }

    public void HandleStarSystemShipMovementEnded()
        => Speaker.Speak(Loc.T("systemmap.ship_stopped"), interrupt: false);

    // ---- object scan ----

    public void HandleStartScanningStarSystemObject() { /* the completion line carries everything */ }

    /// <summary>Scan finished: the sighted deltas are the revealed name, the POI icons appearing, and the
    /// resource list — speak their summary. (Raised with the entity as the event invoker.)</summary>
    public void HandleScanStarSystemObject()
    {
        try
        {
            var entity = EventInvokerExtensions.Entity as StarSystemObjectEntity;
            if (entity == null) return;
            string name = entity.Blueprint?.Name;
            var line = new System.Text.StringBuilder(
                Loc.T("systemmap.scan_done", new { name = string.IsNullOrWhiteSpace(name) ? "???" : name }));
            int pois = 0;
            if (entity.PointOfInterests != null)
                foreach (var poi in entity.PointOfInterests)
                    if (poi != null && poi.IsVisible()
                        && poi.Status != Kingmaker.Globalmap.Interaction.BasePointOfInterest.ExplorationStatus.Explored)
                        pois++;
            if (pois == 1) line.Append(' ').Append(Loc.T("systemmap.scan_pois_one"));
            else if (pois > 1) line.Append(' ').Append(Loc.T("systemmap.scan_pois", new { n = pois }));
            int res = entity.ResourcesOnObject?.Count ?? 0;
            if (res == 1) line.Append(' ').Append(Loc.T("systemmap.scan_res_one"));
            else if (res > 1) line.Append(' ').Append(Loc.T("systemmap.scan_res", new { n = res }));
            Speaker.Speak(line.ToString(), interrupt: false);
        }
        catch (Exception e) { Main.Log?.Log("scan announce failed: " + e.Message); }
    }

    // ---- research progress ----

    public void HandleResearchPercentRecalculate(BlueprintStarSystemMap areaBlueprint, float value)
    {
        int pct = Mathf.RoundToInt(value);
        int previous = ResearchPercent;
        ResearchPercent = pct;
        // First recalc after load/seed is a baseline, not a change; only announce real progress on the map.
        if (previous >= 0 && pct != previous && OnSystemMap)
            Speaker.Speak(Loc.T("systemmap.research_change", new { pct }), interrupt: false);
    }

    // ---- proximity noises (per-frame poll; the VM itself distance-checks each frame) ----

    /// <summary>Called every frame from Main.OnUpdate. Speaks each proximity cue once on its rising edge —
    /// mirroring the game's three interference icons lighting up near an anomaly / unexplored-POI planet /
    /// resource object.</summary>
    internal void Tick()
    {
        if (!OnSystemMap)
        {
            _nearAnomaly = _nearPoi = _nearResources = false;
            return;
        }
        try
        {
            var noises = Game.Instance?.RootUiContext?.SpaceVM?.StaticPartVM?.SystemMapNoisesVM;
            if (noises == null) return;
            Edge(ref _nearAnomaly, noises.AnomalyIsNear.Value, "systemmap.near_anomaly");
            Edge(ref _nearPoi, noises.PoiIsNear.Value, "systemmap.near_poi");
            Edge(ref _nearResources, noises.ResourcesIsNear.Value, "systemmap.near_resources");
        }
        catch (Exception e) { Main.Log?.Log("noise cue failed: " + e.Message); }
    }

    private static void Edge(ref bool last, bool now, string key)
    {
        if (now && !last) Speaker.Speak(Loc.T(key), interrupt: false);
        last = now;
    }
}
