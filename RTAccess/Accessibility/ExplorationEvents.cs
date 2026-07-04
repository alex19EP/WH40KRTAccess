using Kingmaker;
using Kingmaker.PubSubSystem;
using Kingmaker.PubSubSystem.Core;
using Kingmaker.View;
using RTAccess.Speech;
using UnityEngine;

namespace RTAccess.Accessibility;

/// <summary>
/// Long-lived <see cref="Kingmaker.PubSubSystem.Core.EventBus"/> subscriber that voices world-exploration state:
/// the currently chosen nearby interactable (as the game cycles it) and area/loading transitions. Subscribed
/// once at mod load and unsubscribed at unload (see <see cref="Main"/>) — no per-area churn.
///
/// The game raises <see cref="ISurroundingInteractableObjectsCountHandler"/> per interactable whenever the set
/// or chosen object changes; we announce only the chosen one, deduped on the entity reference so walking (which
/// continuously re-picks the closest object) doesn't repeat the same line every frame. The ambient walk-by
/// announce is queued (interrupt:false) — it isn't a keypress — whereas a keyboard cycle (PageUp/Dn, via
/// <see cref="MarkUserCycle"/>) and the Home re-announce interrupt. Per [[rt-interrupt-speech-rule]].
/// </summary>
internal sealed class ExplorationEvents :
    ISurroundingInteractableObjectsCountHandler,
    IAreaActivationHandler,
    IOpenLoadingScreenHandler,
    ICloseLoadingScreenHandler
{
    internal static readonly ExplorationEvents Instance = new ExplorationEvents();

    // Max auto-announce rate while moving (the chosen=closest object churns as you walk through a cluster).
    private const float MinIntervalSeconds = 0.4f;
    // Don't re-announce the same object this soon even if it flickers in/out of "chosen".
    private const float SameObjectCooldown = 3f;

    private EntityViewBase _lastChosen;   // current pick (for Home re-announce), regardless of filter/throttle
    private EntityViewBase _lastSpoken;   // last object actually voiced (for cooldown)
    private float _lastSpokenTime;
    private int _userCycleFrame = -1;     // frame of the last keyboard cycle (PageUp/Dn), for interrupt provenance

    /// <summary>Mark the current frame as a user-driven cycle of the chosen interactable (was called by the
    /// retired ExplorationNav; retained for a future self-driven cycler) so the resulting announce (raised
    /// synchronously the same frame) interrupts — it was a keypress — while the ambient walk-by announce, which
    /// has no such mark, stays queued. Per [[rt-interrupt-speech-rule]].</summary>
    internal void MarkUserCycle() => _userCycleFrame = Time.frameCount;

    public void HandleSurroundingInteractableObjectsCountChanged(EntityViewBase entity, bool isInNavigation, bool isChosen)
    {
        if (entity == null) return;
        try
        {
            if (!isChosen)
            {
                if (ReferenceEquals(entity, _lastChosen)) _lastChosen = null; // chosen walked out of range
                return;
            }
            if (ReferenceEquals(entity, _lastChosen)) return; // unchanged pick
            _lastChosen = entity;

            // No custom filter: announce whatever the game itself lets you select. We only pace the speech.
            // Throttle the walk-through-a-crowd flood: cap the rate, and suppress the same object repeating soon.
            float now = Time.unscaledTime;
            if (now - _lastSpokenTime < MinIntervalSeconds) return;
            if (ReferenceEquals(entity, _lastSpoken) && now - _lastSpokenTime < SameObjectCooldown) return;

            _lastSpoken = entity;
            _lastSpokenTime = now;
            // Interrupt only if this change came from a keyboard cycle this frame; ambient walk-by stays queued.
            Announce(entity, interrupt: _userCycleFrame == Time.frameCount);
        }
        catch (Exception e)
        {
            Main.Log?.Log("interactable announce failed: " + e.Message);
        }
    }

    /// <summary>Re-speak the current pick (bound to the Home key); says "nothing nearby" when there is none.
    /// Key-driven, so it interrupts prior speech.</summary>
    public void ReannounceCurrent()
    {
        if (_lastChosen != null) Announce(_lastChosen, interrupt: true);
        else Speaker.Speak(Loc.T("explore.nothing_nearby"), interrupt: true);
    }

    private static void Announce(EntityViewBase entity, bool interrupt)
    {
        var text = InteractableDescriber.Describe(entity);
        Speaker.Speak(string.IsNullOrWhiteSpace(text) ? Loc.T("explore.interactable") : text, interrupt: interrupt);
    }

    // Area / loading transitions.
    public void OnAreaActivated()
    {
        try
        {
            _lastChosen = null; // new area — drop the stale pick + throttle state
            _lastSpoken = null;
            TileExplorer.Reset(); // the tile cursor pointed at a node in the old area's grid — clear it
            RTAccess.Exploration.WallTones.Reset(); // release the wall-tone voices; they rebuild against the new grid
            RTAccess.Audio.SpatialSources.Clear(); // stop tracking sonar pings anchored to the old grid's points
            RTAccess.Exploration.RoomMap.Invalidate(); // the room map was built for the old area's grid
            var name = Game.Instance?.CurrentlyLoadedArea?.AreaDisplayName;
            Speaker.Speak(string.IsNullOrWhiteSpace(name) ? Loc.T("explore.new_area") : Loc.T("explore.entering", new { name }), interrupt: false);
        }
        catch (Exception e) { Main.Log?.Log("area announce failed: " + e.Message); }
    }

    public void HandleOpenLoadingScreen() => Speaker.Speak(Loc.T("explore.loading"), interrupt: false);

    public void HandleCloseLoadingScreen() { /* OnAreaActivated announces the entered area; nothing to add here. */ }
}
