using System.Collections.Generic;
using Kingmaker.EntitySystem.Entities;   // BaseUnitEntity
using Kingmaker.PubSubSystem;            // ICellAbilityHandler
using Kingmaker.PubSubSystem.Core;       // EventBus
using Kingmaker.UnitLogic.Abilities;     // AbilityTargetUIData

namespace RTAccess.Combat;

/// <summary>
/// Reads the game's OWN aiming result. Subscribes to the aim broadcast (<see cref="ICellAbilityHandler"/>) and
/// snapshots the affected-target list the aim pipeline builds each recompute — the same
/// <c>List&lt;AbilityTargetUIData&gt;</c> the sighted per-unit hit-chance overtips bind. Paired with
/// <see cref="AimPointerDriver"/>, which drives the game's pointer to our keyboard cursor so the list is computed
/// AT our aim point; reading the game's own list (instead of re-deriving) yields the overpenetration pierce chain,
/// AoE membership, and friendly fire correct-by-construction. See docs/plans/piloted-aiming-lamport.md.
///
/// The game CLEARS AND REUSES the same list instance on the next recompute, so we snapshot (value-copy the structs)
/// on receipt and never retain the delivered reference. Lifecycle is driven from
/// <see cref="RTAccess.Exploration.Targeting"/>'s aiming edge — <see cref="Begin"/> on aim start,
/// <see cref="End"/> on aim end — so a stale list from a previous aim is never read.
/// </summary>
internal sealed class AimReadTap : ICellAbilityHandler
{
    public static readonly AimReadTap Instance = new AimReadTap();
    private AimReadTap() { }

    /// <summary>The latest affected-target snapshot for the current aim (empty when nothing is caught / not aiming).</summary>
    public readonly List<AbilityTargetUIData> Last = new List<AbilityTargetUIData>();

    private bool _listening;

    /// <summary>Start listening for this aim; drops any stale snapshot. Idempotent.</summary>
    public void Begin()
    {
        Last.Clear();
        if (_listening) return;
        EventBus.Subscribe(this);
        _listening = true;
    }

    /// <summary>Stop listening and drop the snapshot when aiming ends. Idempotent.</summary>
    public void End()
    {
        if (_listening) { EventBus.Unsubscribe(this); _listening = false; }
        Last.Clear();
    }

    public void HandleCellAbility(List<AbilityTargetUIData> abilityTargets)
    {
        Last.Clear();
        if (abilityTargets != null) Last.AddRange(abilityTargets); // snapshot — the game reuses the delivered instance
#if DEBUG
        LogChange();
#endif
    }

#if DEBUG
    // Step-1 verification aid: trace the affected list to rtaccess_log.txt whenever it changes (i.e. as the
    // keyboard cursor steps), and expose a one-line dump for /eval (DevApi.AimDump). Removed once speech lands.
    private string _lastSig;

    private void LogChange()
    {
        var sig = Signature();
        if (sig == _lastSig) return;
        _lastSig = sig;
        Main.Log?.Log("[aim] " + sig);
    }

    public string DumpLast() => Signature();

    private string Signature()
    {
        if (Last.Count == 0) return "(no targets)";
        var sb = new System.Text.StringBuilder();
        sb.Append(Last.Count).Append(" target(s): ");
        for (int i = 0; i < Last.Count; i++)
        {
            var d = Last[i];
            var u = d.Target as BaseUnitEntity;
            string nm = u != null ? u.CharacterName + (u.IsPlayerEnemy ? "[E]" : "[A]")
                                  : (d.Target != null ? d.Target.GetType().Name : "null");
            if (i > 0) sb.Append("; ");
            string hit = d.HitAlways ? "sure" : System.Math.Round(d.HitWithAvoidanceChance) + "%";
            sb.Append(nm).Append(" hit=").Append(hit).Append(" dmg=").Append(d.MinDamage).Append('-').Append(d.MaxDamage);
        }
        return sb.ToString();
    }
#endif
}
