using RTAccess.Speech; // Speaker

namespace RTAccess.Exploration;

/// <summary>
/// Coordinator for accessible ability targeting — the bridge between the game's armed-but-unclickable pointer mode
/// and our keyboard cursor/scanner. While an ability is <see cref="Aiming"/>, the exploration act keys are
/// repurposed to commit/cancel through <see cref="AbilityTargeting"/>: <b>Enter</b> commits at the world cursor
/// (the unit under it, else the point), <b>I</b> commits on the scanner's review selection, <b>Backspace</b>
/// cancels. Those keys' normal jobs (interact / move-to) resume the instant aiming ends. The action bar does the
/// arming (<c>ProxyActionBarSlot.OnMainClick</c> → <c>SetAbility</c>); this does the commit.
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
        if (!MapCursor.Has) { Speaker.Speak("Move the cursor to a target first.", interrupt: true); return; }
        var target = CursorTarget.Inside();
        Ability.CommitAt(target?.TargetUnit, MapCursor.Position);
    }

    /// <summary>I: commit on the scanner's current review selection — its unit if it is one, else its point.</summary>
    public static void CommitOnSelection(ScanItem item)
    {
        if (!Aiming) return;
        if (item == null) { Speaker.Speak("No selection to fire on.", interrupt: true); return; }
        Ability.CommitAt(item.TargetUnit, item.Position);
    }

    /// <summary>Backspace: cancel the active aim.</summary>
    public static void Cancel()
    {
        if (Aiming) Ability.Cancel();
    }

    // ---- per-frame ----

    private static bool _wasAiming;

    /// <summary>The moment aiming begins, hand the keyboard from the HUD back to exploration so the cursor/scanner
    /// commit keys work immediately — the exploration keys are live only while the HUD is unfocused, and arming an
    /// ability from the (focused) action bar would otherwise strand the player inside the HUD. Only blurs when the
    /// HUD actually holds focus; leaves focus elsewhere alone.</summary>
    public static void Tick()
    {
        bool aiming = Aiming;
        if (aiming && !_wasAiming && RTAccess.UI.Navigation.HasFocus)
            RTAccess.UI.Navigation.Blur();
        _wasAiming = aiming;
    }
}
