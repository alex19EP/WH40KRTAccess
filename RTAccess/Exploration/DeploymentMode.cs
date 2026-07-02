using Kingmaker;                                          // Game
using Kingmaker.Controllers.Clicks.Handlers;              // ClickSurfaceDeploymentHandler (deploy-cell legality)
using Kingmaker.EntitySystem.Entities;                    // BaseUnitEntity
using Kingmaker.Pathfinding;                              // CustomGridNodeBase
using RTAccess.Accessibility;                             // CombatReads (the holographic vantage read)
using RTAccess.Speech;                                    // Speaker
using UnityEngine;                                        // Mathf

namespace RTAccess.Exploration;

/// <summary>
/// Accessible pre-combat DEPLOYMENT — the game's "preparation" turn, which was previously announce-only. It is a
/// mode-scoped aim-then-commit built on the same shape as <see cref="Targeting"/> (the class comment there invites
/// exactly this): while <see cref="Active"/>, the shared tile cursor's <b>Enter</b> PLACES the selected character on
/// the cursor tile (through <see cref="RTAccess.Combat.CommandDispatch.Deploy"/>, which drives the game's own
/// deploy-cell validation + teleport), the arrow keys and the party member-select hotkeys choose the cell and the
/// unit, and <b>B</b> starts the battle (<c>TurnController.RequestEndPreparationTurn</c>). Every cursor step also
/// reads the tile's deploy legality plus the holographic "if I stood here" vantage from that cell
/// (<see cref="CombatReads.VantageFrom"/>), so placement is a tactical decision rather than a guess.
///
/// No new cursor and no new input framework — it rides <see cref="MapCursor"/> /
/// <see cref="RTAccess.Accessibility.TileExplorer"/> exactly as ability targeting does; the entry announce is a
/// follow-up to the "Deployment phase" lifecycle cue that <see cref="RTAccess.Accessibility.CombatEvents"/> already
/// speaks (which also owns the "Battle begins" exit cue), so this only adds the controls + budget line.
/// </summary>
internal static class DeploymentMode
{
    /// <summary>True while the game is in the pre-combat preparation (deployment) turn.</summary>
    public static bool Active => Game.Instance?.TurnController?.IsPreparationTurn == true;

    /// <summary>True while repositioning is actually allowed — false on an all-surprised ambush, where the prep
    /// window opens but only "start battle" is available.</summary>
    public static bool CanPlace => Active && Game.Instance.TurnController.IsDeploymentAllowed;

    private static BaseUnitEntity Selected()
        => Game.Instance?.SelectionCharacter?.SelectedUnit?.Value as BaseUnitEntity;

    /// <summary>Enter, while deploying: place the selected character on the cursor tile. Refusals (out of zone, too
    /// close to an enemy, can't stand) are spoken by <see cref="RTAccess.Combat.CommandDispatch.Deploy"/>.</summary>
    public static void CommitAtCursor()
    {
        if (!Active) return;
        if (!CanPlace) { Speaker.Speak(Loc.T("deploy.cannot_reposition"), interrupt: true); return; }
        if (!MapCursor.Has) { Speaker.Speak(Loc.T("deploy.move_cursor_first"), interrupt: true); return; }
        var unit = Selected();
        if (RTAccess.Combat.CommandDispatch.Deploy(MapCursor.Node))
            Speaker.Speak(Loc.T("deploy.placed", new { name = unit?.CharacterName ?? "" }), interrupt: true);
    }

    /// <summary>B, while deploying: start the battle, or say why it can't start yet. Wrapped like the tile-cursor
    /// handlers because Main.OnUpdate has no top-level catch.</summary>
    public static void StartBattle()
    {
        try
        {
            if (!Active) return;   // self-gate: B does nothing outside deployment
            var tc = Game.Instance.TurnController;
            if (tc.CanFinishDeploymentPhase()) tc.RequestEndPreparationTurn();   // CombatEvents speaks "Battle begins" on prep exit
            else Speaker.Speak(Loc.T("deploy.cannot_start"), interrupt: true);
        }
        catch (Exception e) { Main.Log?.Error("DeploymentMode.StartBattle failed: " + e); }
    }

    /// <summary>The deploy-legality + vantage tail the tile cursor appends to each step readout while deploying.</summary>
    public static string CursorTail(CustomGridNodeBase node)
    {
        if (node == null) return null;
        var unit = Selected();
        if (unit == null) return null;
        var sb = new System.Text.StringBuilder();
        sb.Append(Loc.T(ClickSurfaceDeploymentHandler.CanDeployUnit(node, unit) ? "deploy.cell_ok" : "deploy.cell_no"));
        var vantage = CombatReads.VantageFrom(node.Vector3Position, unit);
        if (!string.IsNullOrWhiteSpace(vantage)) sb.Append(". ").Append(vantage);
        return sb.ToString();
    }

    // ---- per-frame ----
    private static bool _wasActive;

    /// <summary>Announce the deployment controls on entry (once, queued after the "Deployment phase" lifecycle cue
    /// that <see cref="CombatEvents"/> speaks) — mirrors <see cref="Targeting.Tick"/>. Automatic/event cue, so it
    /// QUEUEs (interrupt:false) per the speech-provenance rule. The exit "Battle begins" cue is owned by
    /// <see cref="CombatEvents"/> so it stays ordered ahead of the round / whose-turn cues. Pumped from Main.OnUpdate.</summary>
    public static void Tick()
    {
        bool active = Active;
        if (active && !_wasActive)
        {
            if (RTAccess.UI.Navigation.HasFocus) RTAccess.UI.Navigation.Blur();
            var opening = ArmAnnounce();
            if (opening != null) Speaker.Speak(opening, interrupt: false);
        }
        _wasActive = active;
    }

    /// <summary>The controls line spoken as deployment opens: the reposition budget (the blue points the prep turn
    /// grants each unit) + how to place a unit / start the battle, or the surprise variant when repositioning is
    /// blocked. Follows the "Deployment phase" cue already spoken by <see cref="CombatEvents"/>.</summary>
    private static string ArmAnnounce()
    {
        var tc = Game.Instance?.TurnController;
        if (tc == null) return null;
        if (!tc.IsDeploymentAllowed) return Loc.T("deploy.arm_surprise");
        int cells = 0;
        try { var cs = Selected()?.CombatState; if (cs != null) cells = Mathf.RoundToInt(cs.ActionPointsBlue); } catch { /* budget is a nicety; omit if the combat state isn't ready */ }
        return cells > 0 ? Loc.T("deploy.arm", new { cells }) : Loc.T("deploy.arm_nocount");
    }
}
