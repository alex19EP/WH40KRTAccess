using Kingmaker;
using Kingmaker.PubSubSystem;
using Kingmaker.PubSubSystem.Core;
using Kingmaker.View;
using RTAccess.Speech;

namespace RTAccess.Accessibility;

/// <summary>
/// Long-lived <see cref="Kingmaker.PubSubSystem.Core.EventBus"/> subscriber that voices world-exploration state:
/// the currently chosen nearby interactable (as the game cycles it) and area/loading transitions. Subscribed
/// once at mod load and unsubscribed at unload (see <see cref="Main"/>) — no per-area churn.
///
/// The game raises <see cref="ISurroundingInteractableObjectsCountHandler"/> per interactable whenever the set
/// or chosen object changes; we announce only the chosen one, deduped on the entity reference so walking (which
/// continuously re-picks the closest object) doesn't repeat the same line every frame. Speech is queued
/// (interrupt:false) per [[rt-interrupt-speech-rule]].
/// </summary>
internal sealed class ExplorationEvents :
    ISurroundingInteractableObjectsCountHandler,
    IAreaActivationHandler,
    IOpenLoadingScreenHandler,
    ICloseLoadingScreenHandler
{
    internal static readonly ExplorationEvents Instance = new ExplorationEvents();

    private EntityViewBase _lastChosen;

    public void HandleSurroundingInteractableObjectsCountChanged(EntityViewBase entity, bool isInNavigation, bool isChosen)
    {
        if (entity == null) return;
        try
        {
            if (isChosen)
            {
                if (ReferenceEquals(entity, _lastChosen)) return; // already announced this pick
                _lastChosen = entity;
                Announce(entity);
            }
            else if (ReferenceEquals(entity, _lastChosen))
            {
                _lastChosen = null; // the chosen object stopped being chosen (e.g. walked out of range)
            }
        }
        catch (Exception e)
        {
            Main.Log?.Log("interactable announce failed: " + e.Message);
        }
    }

    /// <summary>Re-speak the current pick (bound to a key); says "nothing nearby" when there is none.</summary>
    public void ReannounceCurrent()
    {
        if (_lastChosen != null) Announce(_lastChosen);
        else Speaker.Speak("Nothing nearby.", interrupt: false);
    }

    private static void Announce(EntityViewBase entity)
    {
        var text = InteractableDescriber.Describe(entity);
        Speaker.Speak(string.IsNullOrWhiteSpace(text) ? "Interactable." : text, interrupt: false);
    }

    // Area / loading transitions.
    public void OnAreaActivated()
    {
        try
        {
            _lastChosen = null; // new area — drop the stale pick
            var name = Game.Instance?.CurrentlyLoadedArea?.AreaDisplayName;
            Speaker.Speak(string.IsNullOrWhiteSpace(name) ? "New area." : ("Entering " + name + "."), interrupt: false);
        }
        catch (Exception e) { Main.Log?.Log("area announce failed: " + e.Message); }
    }

    public void HandleOpenLoadingScreen() => Speaker.Speak("Loading.", interrupt: false);

    public void HandleCloseLoadingScreen() { /* OnAreaActivated announces the entered area; nothing to add here. */ }
}
