using Kingmaker;                       // Game
using Kingmaker.EntitySystem.Entities; // BaseUnitEntity
using RTAccess.Speech;                 // Speaker

namespace RTAccess.Exploration;

/// <summary>
/// Coordinator for accessible ability targeting — the bridge between the game's armed-but-unclickable pointer mode
/// and our keyboard cursor/scanner. While an ability is <see cref="Aiming"/>, the exploration act keys are
/// repurposed to commit/cancel through <see cref="AbilityTargeting"/>: <b>Enter</b> commits at the world cursor
/// (the unit under it, else the point), <b>I</b> commits on the scanner's review selection, <b>Backspace</b>
/// cancels. Those keys' normal jobs (interact / move-to) resume the instant aiming ends. The action bar does the
/// arming (<c>ActionBarNodes.Slot</c> → <c>OnMainClick</c> → <c>SetAbility</c>); this does the commit.
///
/// To add another aim-then-commit kind later (e.g. deployment placement), give it the same Active/CommitAt/Cancel
/// shape and fan <see cref="Aiming"/> / the commit calls across both — the input guards stay put.
/// </summary>
internal static class Targeting
{
    private static readonly AbilityTargeting Ability = new AbilityTargeting();

    /// <summary>True while an ability is armed and waiting for a target — the exploration act keys commit/cancel
    /// instead of doing their normal job while this holds.</summary>
    public static bool Aiming => Ability.Active;

    /// <summary>Enter: commit at the world cursor — the unit whose footprint it is inside, else the cursor point.</summary>
    public static void CommitAtCursor()
    {
        if (!Aiming) return;
        if (!MapCursor.Has) { Speaker.Speak(Loc.T("aim.move_cursor_first"), interrupt: true); return; }
        var target = CursorTarget.Inside();
        Ability.CommitAt(target?.TargetUnit, MapCursor.Position);
    }

    /// <summary>I: commit on the scanner's current review selection — its unit if it is one, else its point.</summary>
    public static void CommitOnSelection(ScanItem item)
    {
        if (!Aiming) return;
        if (item == null) { Speaker.Speak(Loc.T("aim.no_selection"), interrupt: true); return; }
        Ability.CommitAt(item.TargetUnit, item.Position);
    }

    /// <summary>Backspace: cancel the active aim.</summary>
    public static void Cancel()
    {
        if (Aiming) Ability.Cancel();
    }

    // ---- hit prediction (B4) woven into the scanner's target cycle (B3) ----

    /// <summary>While aiming an attack, the one-line hit prediction for firing the armed ability at a scanned unit —
    /// appended by the scanner to its readout so cycling enemies (period) doubles as picking a target and hearing
    /// the odds, and re-announcing (O) gives the full breakdown. Returns null when not aiming, the item isn't a
    /// unit, or the armed ability isn't an attack (prediction is a to-hit number — meaningless for a self-buff/heal).
    /// A pure read (<see cref="RTAccess.Accessibility.HitPredictor"/>), same numbers the sighted reticle shows.</summary>
    public static string PredictLine(ScanItem item, bool verbose)
    {
        if (!Aiming) return null;
        var target = item?.TargetUnit;
        if (target == null) return null;
        var ability = Game.Instance?.SelectedAbilityHandler?.Ability;
        if (ability == null || !ability.CanTargetEnemies) return null;   // to-hit prediction is for attacks
        var caster = ability.Caster as BaseUnitEntity
                     ?? Game.Instance?.TurnController?.CurrentUnit as BaseUnitEntity;
        if (caster == null || caster == target) return null;
        return RTAccess.Accessibility.HitPredictor.Describe(caster, ability, target, verbose);
    }

    // ---- per-frame ----

    private static bool _wasAiming;

    /// <summary>The moment aiming begins, hand the keyboard from the HUD back to exploration so the cursor/scanner
    /// commit keys work immediately — the exploration keys are live only while the HUD is unfocused, and arming an
    /// ability from the (focused) action bar would otherwise strand the player inside the HUD. Only blurs when the
    /// HUD actually holds focus; leaves focus elsewhere alone. Also announces the opening of targeting (what's armed,
    /// its range, and how to pick a target / fire / cancel) so a blind player knows they've entered aim mode.</summary>
    public static void Tick()
    {
        bool aiming = Aiming;
        if (aiming && !_wasAiming)
        {
            // Start reading the game's own affected-target broadcast for this aim; AimPointerDriver's Tick postfix
            // is already driving the pointer to our cursor so the list is computed at our aim point.
            RTAccess.Combat.AimReadTap.Instance.Begin();
            if (RTAccess.UI.Navigation.HasFocus) RTAccess.UI.Navigation.Blur();
            var opening = ArmAnnounce();
            if (opening != null) Speaker.Speak(opening, interrupt: true);
        }
        else if (!aiming && _wasAiming)
        {
            RTAccess.Combat.AimReadTap.Instance.End();
        }
        _wasAiming = aiming;
    }

    /// <summary>The opening announce when an ability arms: name + range + the controls that fit its target kind
    /// (enemies via the scanner's period cycle for attacks, the party's comma cycle for friend-only abilities, or
    /// the free cursor + Enter for point/area abilities). Null if nothing is armed (the transition raced away).</summary>
    private static string ArmAnnounce()
    {
        var ability = Game.Instance?.SelectedAbilityHandler?.Ability;
        if (ability == null) return null;

        var sb = new System.Text.StringBuilder();
        sb.Append(Loc.T("aim.aiming", new { name = ability.Name }));
        int range = 0;
        try { range = ability.RangeCells; } catch { /* range rule can throw on odd abilities; omit it */ }
        if (range > 0) sb.Append(", ").Append(Loc.T(range == 1 ? "aim.range_one" : "aim.range", new { cells = range }));

        // Effective (optimal) range: for a weapon ability the sighted reticle shows a half-range sweet-spot band
        // beyond which accuracy drops — computed as FloorToInt(MaxRangeCells / 2f) when Ability.Weapon != null (see
        // AbilitySingleTargetRange). Speak it so a blind player knows the accurate distance, not just the hard max.
        if (range > 0 && ability.Weapon != null)
        {
            int eff = UnityEngine.Mathf.FloorToInt(range / 2f);
            if (eff > 0 && eff < range) sb.Append(", ").Append(Loc.T("aim.effective_range", new { cells = eff }));
        }
        sb.Append(". ");

        // For an AoE / point ability, name the template up front ("Blast, 2-cell radius") so the player knows the
        // shape before stepping the cursor; the per-cell tail (AoEPreview.CursorTail) then adds range / caught units.
        var shape = AoEPreview.ShapeLine(ability);
        if (shape != null) sb.Append(shape).Append(". ");

        if (ability.CanTargetEnemies)
            sb.Append(Loc.T("aim.enemies_help"));
        else if (ability.CanTargetFriends)
            sb.Append(Loc.T("aim.allies_help"));
        else
            sb.Append(Loc.T("aim.point_help"));
        return sb.ToString();
    }
}
