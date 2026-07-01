using System;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Pathfinding;
using Kingmaker.UI.Common;            // IsDirectlyControllable() extension
using Kingmaker.UnitLogic;            // UnitHelper, TryCreateMoveCommandTB, MoveCommandSettings
using Kingmaker.UnitLogic.Abilities;  // AbilityData
using RTAccess.Speech;
using UnityEngine;

namespace RTAccess.Combat;

/// <summary>
/// The single funnel every combat ACTION routes through (§2.1 of the combat plan), so the turn/selection guard,
/// refusal messaging, and the netcode-buffered command dispatch live in one place instead of being duplicated by
/// each facet (targeting, action bar, movement cursor, scanner).
///
/// All paths are the game's OWN command paths, chosen because they survive mouse mode (the
/// <c>!IsControllerMouse</c> gate on <c>SurfaceMainInputLayer</c> only touches the interactable input LAYER, not
/// pointer-controller calls — see [[rt-mouse-mode-engine-gate]]) and are verified to execute end-to-end: a
/// dispatched pistol shot dropped the target's HP and was narrated by the combat log (<see cref="Accessibility.CombatLogReader"/>).
///
/// Abilities go through <c>ClickWithSelectedAbilityHandler</c> (<c>SetAbility</c> → <c>OnClick</c>) — byte-for-byte
/// the mouse-click path, so ALL of the game's range/LOS/target-restriction validation runs and refusals surface as
/// <c>IWarningNotificationUIHandler</c> (spoken by the warning reader). Do NOT hand-build
/// <c>PlayerUseAbilityParams</c> + <c>Commands.Run</c>: that under-initialises the params and NREs in
/// <c>IsDirectionCorrect</c>. Movement reuses the proven turn-based path (<c>TryCreateMoveCommandTB</c> +
/// <c>Commands.Run</c>). Object interaction stays on the shipped <c>ClickMapObjectHandler</c> path.
/// </summary>
public static class CommandDispatch
{
    /// <summary>The player-controlled unit whose turn-based turn it is — the only unit that may act. Returns it,
    /// or null (speaking the reason unless <paramref name="speak"/> is false). Hand-rolled TB commands bypass the
    /// engine guards <c>UnitCommandsRunner</c> would enforce, so every act path checks this first.</summary>
    public static BaseUnitEntity ActingUnit(bool speak = true)
    {
        var game = Game.Instance;
        var tc = game?.TurnController;
        if (tc == null || !tc.TurnBasedModeActive)
        { if (speak) Speaker.Speak("Not in turn-based combat.", interrupt: true); return null; }
        if (!tc.IsPlayerTurn)
        { if (speak) Speaker.Speak("Not your turn.", interrupt: true); return null; }
        var unit = game.SelectionCharacter?.SelectedUnit?.Value as BaseUnitEntity;
        if (unit == null || unit != tc.CurrentUnit as BaseUnitEntity || !unit.IsDirectlyControllable())
        { if (speak) Speaker.Speak("Select your active character.", interrupt: true); return null; }
        return unit;
    }

    // ---- Abilities (the game's own pointer path — proven to execute and to narrate via the combat log) ----

    /// <summary>Cast at a unit. Returns true if the game accepted it (a single-target cast fired, or a multi-target
    /// ability took this target and wants more — the handler stays armed; callers distinguish via
    /// <see cref="AbilityArmed"/>). Returns false on refusal — the game already spoke the reason.</summary>
    public static bool UseAbilityOnUnit(AbilityData ability, BaseUnitEntity target)
    {
        if (ability == null || target == null) return false;
        var view = target.View;
        return DispatchAbility(ability, view != null ? view.gameObject : null, target.Position);
    }

    /// <summary>Cast at a grid point (AoE / blast / point-anchored). No GameObject, so the handler resolves the
    /// target from the world point. (AoE orientation defaults to the caster→point facing; the finer
    /// <c>TryUnitUseAbility(TargetWrapper)</c> path is deferred to AoE targeting where orientation is chosen.)</summary>
    public static bool UseAbilityOnPoint(AbilityData ability, CustomGridNodeBase node)
    {
        if (ability == null || node == null) return false;
        return DispatchAbility(ability, null, node.Vector3Position);
    }

    /// <summary>Cast a self / owner-anchored ability on the acting unit.</summary>
    public static bool UseSelfAbility(AbilityData ability)
    {
        var caster = ActingUnit(speak: false) ?? (ability?.Caster as BaseUnitEntity);
        return UseAbilityOnUnit(ability, caster);
    }

    /// <summary>True while an ability is armed and waiting for (more) targets — read live from the handler.</summary>
    public static bool AbilityArmed => Game.Instance?.SelectedAbilityHandler?.Ability != null;

    // SetAbility enters PointerMode.Ability; OnClick resolves the target, runs the full cast validation, and either
    // accumulates a multi-target or issues the command. Returns OnClick's verdict (false = refused/invalid).
    private static bool DispatchAbility(AbilityData ability, GameObject targetGo, Vector3 point)
    {
        try
        {
            var h = Game.Instance?.SelectedAbilityHandler;
            if (h == null) return false;
            h.SetAbility(ability);
            return h.OnClick(targetGo, point, 0);
        }
        catch (Exception e) { Main.Log?.Log("ability dispatch failed: " + e.Message); return false; }
    }

    // ---- Movement & turn ----

    /// <summary>Commit a turn-based move to a grid node. Returns true on success (caller gives the "Moving." cue);
    /// on failure speaks the specific reason and returns false. Reachability/AP are the engine's call, not ours.</summary>
    public static bool MoveTo(CustomGridNodeBase node)
    {
        var unit = ActingUnit();
        if (unit == null || node == null) return false;
        try
        {
            var cmd = unit.TryCreateMoveCommandTB(
                new MoveCommandSettings { Destination = node.Vector3Position, DisableApproachRadius = true },
                showMovePrediction: false, out var status);
            if (cmd != null) { unit.Commands.Run(cmd); return true; }
            Speaker.Speak(MoveFailure(status), interrupt: true);
            return false;
        }
        catch (Exception e) { Main.Log?.Log("move dispatch failed: " + e.Message); return false; }
    }

    /// <summary>End the player's turn (no-op if the game won't allow it right now).</summary>
    public static void EndTurn()
    {
        var tc = Game.Instance?.TurnController;
        if (tc != null && tc.CanEndTurn) tc.TryEndPlayerTurnManually();
    }

    private static string MoveFailure(UnitHelper.MoveCommandStatus status)
    {
        switch (status)
        {
            case UnitHelper.MoveCommandStatus.NotEnoughMovementPoints: return "Not enough movement points.";
            case UnitHelper.MoveCommandStatus.DestinationUnreachable: return "Path blocked.";
            case UnitHelper.MoveCommandStatus.CannotMove: return "Can't move.";
            case UnitHelper.MoveCommandStatus.SamePath: return "Already moving there.";
            default: return "Can't reach that tile.";
        }
    }

#if DEBUG
    /// <summary>Dev harness self-test (proven the dispatch executes): the acting unit fires its longest-range
    /// enemy-target ability at the nearest visible enemy. Watch /speech for the combat-log narration and the
    /// target's HP drop. Call via /eval: <c>RTAccess.Combat.CommandDispatch.DevTestAttackNearestEnemy()</c>.</summary>
    public static string DevTestAttackNearestEnemy()
    {
        var unit = ActingUnit(speak: false);
        if (unit == null) return "no acting unit (need player TB turn with the active character selected)";

        AbilityData best = null;
        int bestRange = -1;
        foreach (var ab in unit.Abilities.RawFacts)
        {
            var d = ab.Data;
            if (d == null || !d.CanTargetEnemies) continue;
            if (d.RangeCells > bestRange) { bestRange = d.RangeCells; best = d; }
        }
        if (best == null) return "no enemy-target ability";

        BaseUnitEntity nearest = null;
        float bestDist = float.MaxValue;
        foreach (var o in (System.Collections.IEnumerable)Game.Instance.State.AllBaseAwakeUnits)
        {
            if (!(o is BaseUnitEntity u) || !u.IsInCombat || !u.IsPlayerEnemy || !u.IsVisibleForPlayer) continue;
            float dist = (u.Position - unit.Position).sqrMagnitude;
            if (dist < bestDist) { bestDist = dist; nearest = u; }
        }
        if (nearest == null) return "no visible enemy";

        int hpBefore = nearest.Health.HitPointsLeft;
        bool ok = UseAbilityOnUnit(best, nearest);
        return $"dispatched {best.Name} at {nearest.CharacterName} (hp {hpBefore}) ok={ok} armed={AbilityArmed}";
    }
#endif
}
