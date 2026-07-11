using Kingmaker;
using Kingmaker.AreaLogic.Etudes;                 // EtudeTriggerActionInWarpDelayed
using Kingmaker.Blueprints;                        // BlueprintComponentReference
using Kingmaker.GameModes;
using Kingmaker.Globalmap.SectorMap;               // SectorMapPassageEntity, SectorMapPassageView
using Kingmaker.PubSubSystem;
using Kingmaker.PubSubSystem.Core;
using RTAccess.Localization;
using RTAccess.Speech;

namespace RTAccess.Accessibility;

/// <summary>
/// Long-lived EventBus subscriber voicing SECTOR-MAP / warp-travel state (<see cref="GameModeType.GlobalMap"/>):
/// entering / leaving warp, pause / resume, scan lifecycle + reveals, and warp-route charting. The
/// <see cref="RTAccess.Accessibility.SpaceEvents"/> sibling for the galaxy layer. Subscribed once at mod load,
/// unsubscribed at unload (see <see cref="Main"/>); the tiny <see cref="Tick"/> only resets edge state off-map.
/// Plan: docs/plans/warp-sector-map-accessibility.md.
///
/// All lines are passive/event speech → QUEUED (interrupt: false), per [[rt-interrupt-speech-rule]]. The one
/// exception is handled by omission: when travel was commanded from our own screen (which already spoke
/// "Travel to X" on the keypress), <see cref="MarkCommandedTravel"/> suppresses the redundant started-line.
/// Navigator-Resource spend/gain is deliberately NOT voiced here — the game logs it and LogTap already speaks
/// the log (its NavigatorResource channel), so voicing it here would double.
///
/// Status: BUILT FROM THE DECOMPILE, UNTESTED IN-HARNESS (warp travel is quest-gated). The mid-jump scripted-
/// event cue in particular (<see cref="HandleStartEventInTheMiddleOfJump"/>) may be redundant with LogTap /
/// dialogue narration — verify and prune in-harness. See the plan's test checklist.
/// </summary>
internal sealed class WarpEvents :
    ISectorMapWarpTravelHandler,
    ISectorMapScanHandler,
    ISectorMapStarSystemChangeHandler,
    ISectorMapPassageChangeHandler,
    ISectorMapWarpTravelEventHandler,
    ISectorMapWarpTravelRepeatedEventHandler
{
    internal static readonly WarpEvents Instance = new WarpEvents();

    // Set true by our own screen right before it commands a jump it already announced; consumed by the next
    // HandleWarpTravelStarted (robust to the several-second start animation, unlike a timestamp window).
    private static bool _suppressNextStart;

    // Announce resume only when we actually announced a pause (resume also fires on load / area re-enable).
    private bool _announcedPause;

    // Newly-revealed contacts between HandleScanStarted and HandleScanStopped.
    private int _scanRevealed;

    /// <summary>Called by the screen right before it issues a travel command it has already spoken.</summary>
    internal static void MarkCommandedTravel() => _suppressNextStart = true;

    private static bool OnSectorMap => Game.Instance?.CurrentMode == GameModeType.GlobalMap;

    // ---- warp travel ----

    public void HandleWarpTravelBeforeStart() { /* nothing spoken before the animation resolves */ }

    public void HandleWarpTravelStarted(SectorMapPassageEntity passage)
    {
        if (_suppressNextStart) { _suppressNextStart = false; return; } // our screen already said the destination
        _announcedPause = false;
        try
        {
            string dest = Game.Instance?.SectorMapTravelController?.To?.View?.Name;
            if (passage != null)
                Speaker.Speak(Loc.T("sectormap.evt_entering_diff", new
                {
                    name = string.IsNullOrWhiteSpace(dest) ? "?" : dest,
                    difficulty = SectorMapScreenDifficulty(passage),
                }), interrupt: false);
            else
                Speaker.Speak(Loc.T("sectormap.evt_entering", new
                {
                    name = string.IsNullOrWhiteSpace(dest) ? "?" : dest,
                }), interrupt: false);
        }
        catch (Exception e) { Main.Log?.Log("warp started announce failed: " + e.Message); }
    }

    public void HandleWarpTravelStopped()
    {
        _announcedPause = false;
        try
        {
            string name = Game.Instance?.SectorMapController?.CurrentStarSystem?.View?.Name;
            Speaker.Speak(Loc.T("sectormap.evt_arrived", new
            {
                name = string.IsNullOrWhiteSpace(name) ? "?" : name,
            }), interrupt: false);
        }
        catch (Exception e) { Main.Log?.Log("warp stopped announce failed: " + e.Message); }
    }

    public void HandleWarpTravelPaused()
    {
        if (_announcedPause) return;
        _announcedPause = true;
        Speaker.Speak(Loc.T("sectormap.evt_paused"), interrupt: false);
    }

    public void HandleWarpTravelResumed()
    {
        if (!_announcedPause) return; // resume fires on load/enable too; only echo a pause we announced
        _announcedPause = false;
        Speaker.Speak(Loc.T("sectormap.evt_resumed"), interrupt: false);
    }

    // A star-system arrival raises HandleStarSystemChanged just before HandleWarpTravelStopped, so the arrival
    // line above already covers it — kept as a no-op to avoid a double announce.
    public void HandleStarSystemChanged() { }

    // ---- scan ----

    public void HandleScanStarted(float range, float duration)
    {
        _scanRevealed = 0;
        Speaker.Speak(Loc.T("sectormap.evt_scanning"), interrupt: false);
    }

    public void HandleSectorMapObjectScanned(SectorMapPassageView passageToStarSystem)
    {
        _scanRevealed++;
    }

    public void HandleScanStopped()
    {
        int n = _scanRevealed;
        _scanRevealed = 0;
        Speaker.Speak(n > 0
                ? Loc.T("sectormap.evt_scan_done_n", new { n })
                : Loc.T("sectormap.evt_scan_done"),
            interrupt: false);
    }

    // ---- warp routes (Navigator-Resource spends) ----

    public void HandleNewPassageCreated()
        => Speaker.Speak(Loc.T("sectormap.evt_route_created"), interrupt: false);

    public void HandlePassageLowerDifficulty()
        => Speaker.Speak(Loc.T("sectormap.evt_route_safer"), interrupt: false);

    // ---- mid-jump scripted events ----

    // A scripted event fires past the halfway point of a jump. Some run silent etude action lists (not a dialog),
    // so a sighted player at least sees the ship react; we voice a neutral cue. TODO(harness): this may be
    // redundant with LogTap / dialogue narration for events that DO log or open a dialog — verify and gate/remove.
    public void HandleStartEventInTheMiddleOfJump(BlueprintComponentReference<EtudeTriggerActionInWarpDelayed> etudeTrigger)
    {
        if (!OnSectorMap) return;
        Speaker.Speak(Loc.T("sectormap.evt_warp_event"), interrupt: false);
    }

    // Fires once per jump right after Started (a per-jump repeated etude hook). Started already announced; no-op.
    public void HandleStartJumpEvent() { }

    // ---- tick (edge-state housekeeping only; the reader is otherwise event-driven) ----

    /// <summary>Called every frame from Main.OnUpdate. Clears transient edge state when we leave the sector map
    /// so a stale pause/scan flag can't leak into the next visit.</summary>
    internal void Tick()
    {
        if (OnSectorMap) return;
        _announcedPause = false;
        _scanRevealed = 0;
    }

    private static string SectorMapScreenDifficulty(SectorMapPassageEntity passage)
        => RTAccess.Screens.SectorMapScreen.DifficultyWord(passage.CurrentDifficulty);
}
