using Kingmaker;                                          // Game
using Kingmaker.Controllers.Clicks.Handlers;              // ClickWithSelectedAbilityHandler
using Kingmaker.EntitySystem.Entities;                    // BaseUnitEntity
using RTAccess.Speech;                                    // Speaker
using UnityEngine;

namespace RTAccess.Exploration;

/// <summary>
/// The accessible commit/cancel half of action-bar ability targeting. The game's own action bar (our
/// <see cref="RTAccess.UI.Proxies.ProxyActionBarSlot"/> → <c>ActionBarSlotVM.OnMainClick</c>) already ARMS a
/// targeted ability — it calls <see cref="ClickWithSelectedAbilityHandler.SetAbility"/>, entering
/// <c>PointerMode.Ability</c> — but then dead-ends, because a blind player has no mouse to click a target with.
/// This supplies the missing commit: it routes a chosen target through the handler's <c>OnClick</c>, so ALL of the
/// game's validation, target restrictions, refusal messaging (raised as <c>IWarningNotificationUIHandler</c> and
/// spoken by the warning reader), multi-target accumulation, and the actual cast command are reused verbatim.
///
/// <see cref="Active"/> is read LIVE from the handler (<c>Ability != null</c>), so cancelling elsewhere — a
/// right-click, re-toggling the slot, a mode switch — clears us too; we never hold stale aim state.
/// </summary>
internal sealed class AbilityTargeting
{
    private static ClickWithSelectedAbilityHandler Handler => Game.Instance?.SelectedAbilityHandler;

    /// <summary>True while an action-bar ability is armed and waiting for a target (the handler has an ability).</summary>
    public bool Active => Handler?.Ability != null;

    /// <summary>Commit at a chosen target: a unit when the cursor / scanner item is on one (its GameObject), else the
    /// world point. Lets the game's <c>OnClick</c> resolve the target, validate it, and either add it (multi-target,
    /// more needed) or issue the cast. On refusal <c>OnClick</c> raises the game's own warning (spoken elsewhere) and
    /// returns false, so we say nothing. We distinguish "one more target added" from "used" by whether the handler
    /// is still armed afterwards (a single-target cast clears the pointer mode; a multi-target add re-arms).</summary>
    public void CommitAt(BaseUnitEntity unit, Vector3 point)
    {
        var h = Handler;
        if (h?.Ability == null) return;
        var go = unit != null && unit.View != null ? unit.View.gameObject : null;
        if (!h.OnClick(go, point, 0)) return; // refused → the game spoke the reason; nothing to add

        bool moreTargets = h.Ability != null; // still armed → a multi-target ability wants the next target
        if (moreTargets) Speaker.Speak(MultiTargetProgress(h), interrupt: true);
        else if (unit != null) Speaker.Speak(Loc.T("aim.firing_on", new { name = unit.CharacterName }), interrupt: true);
        else Speaker.Speak(Loc.T("aim.ability_used"), interrupt: true);
    }

    /// <summary>"Target k of n chosen, pick the next." — k is the count picked so far (post-commit), n the total the
    /// ability wants, recomputed HERE each commit (not cached at arm) because some multi-target abilities make the
    /// remaining budget a function of prior picks. Falls back to the countless "Target chosen" when n is unknown.</summary>
    private static string MultiTargetProgress(ClickWithSelectedAbilityHandler h)
    {
        var mt = h.MultiTargetHandler;
        int k = mt?.Targets?.Count ?? 0;
        int n = k;
        try { while (mt?.AbilityMultiTarget != null && mt.AbilityMultiTarget.TryGetNextTargetAbility(h.RootAbility, n, out _)) n++; }
        catch { n = 0; }
        return n > k ? Loc.T("aim.target_k_of_n", new { k, n }) : Loc.T("aim.target_added");
    }

    /// <summary>Abandon aiming — drop the armed ability / pointer mode (the same path a right-click takes).</summary>
    public void Cancel()
    {
        Game.Instance?.ClickEventsController?.ClearPointerMode();
        Speaker.Speak(Loc.T("aim.cancelled"), interrupt: true);
    }
}
