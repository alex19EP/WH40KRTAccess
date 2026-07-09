using Kingmaker.Blueprints.Root.Strings;      // UIStrings.GameOverScreen (the view's own label source)
using Kingmaker.Code.UI.MVVM.VM.GameOver;     // GameOverVM
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The defeat / game-over screen (<see cref="GameOverVM"/>, raised on <c>GameModeType.GameOver</c> — party
    /// defeated, an essential unit finally dead, kingdom destroyed, or a quest failed). Terminal modal: it reads
    /// the defeat <c>Reason</c> and exposes the game's own recovery actions so a blind player learns they lost
    /// and can reload without sighted help.
    ///
    /// The VM hangs off whichever static part is live (Surface's <c>GameOverVM</c> property, or Space's
    /// <c>SpaceStaticComponentType.GameOver</c> component), resolved through <see cref="UiContexts.GameOver"/>.
    /// Buttons are declared straight off the VM's command methods, labels passing through the game's own card
    /// strings (<c>UIStrings.GameOverScreen</c> — the view's <c>SetButtonsLabel</c> source, so they follow the
    /// game's language), and the SAME visibility gating the view's <c>SetButtonVisible</c> applies:
    /// <list type="bullet">
    /// <item>Normal run: Quick Load (enabled only when <c>CanQuickLoad</c> has settled true), Load, Main Menu.</item>
    /// <item>Iron Man with a downgraded save available: the description line + Delete Save + Continue Game
    /// (Main Menu hidden — mirroring the view).</item>
    /// <item>Iron Man with no save left: Main Menu only.</item>
    /// </list>
    /// Every node (the reason line and each button) is its OWN Tab-stop, so Shift+Tab / Tab cycle between them
    /// (arrows never cross a stop) — the terminal-modal convention shared with <see cref="MessageBoxScreen"/>,
    /// with <c>Wrap</c> making the cycle loop. Button labels prefer the game's own <c>UIStrings.GameOverScreen</c>
    /// strings and fall back to mod keys only if the game ships one blank; Main Menu reuses the shared
    /// <c>screen.main_menu</c> fallback (the same one <see cref="EscMenuScreen"/> uses).
    ///
    /// Graph-native / IMMEDIATE MODE: declared fresh each render, so <c>CanQuickLoad</c> (set asynchronously by
    /// the VM's save-scan coroutine) just settles into the Quick Load enabled state with no rebuild bookkeeping.
    ///
    /// Layer 21, Exclusive — above the in-game context (0), service windows (10) and the Esc menu (20), below the
    /// Save/Load window (22) the Load button raises on top of it. No Back action: the game makes Escape inert here
    /// (the PC view binds it to a no-op), and the screen only leaves when an action changes the game mode
    /// (load / reset to main menu), which drops the VM and pops us.
    /// </summary>
    public sealed class GameOverScreen : Screen
    {
        public GameOverScreen() { Wrap = true; } // Tab cycles reason ↔ buttons

        public override string Key => "ctx.gameover";
        public override string ScreenName => Loc.T("screen.game_over");
        public override int Layer => 21;
        public override bool Exclusive => true; // a terminal modal owns the keyboard

        public override bool IsActive() => Vm() != null;

        private static GameOverVM Vm() => UiContexts.GameOver();


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "gameover:" + vm.GetHashCode() + ":";

            bool ironMan = vm.IsIronMan;
            bool downgraded = ironMan && vm.HasDowngradedIronManSave;

            // The defeat reason (already localized game content) — first, focusable so it can be re-read; it
            // is what the frame differ announces on landing, so the player hears why the run ended.
            if (!string.IsNullOrEmpty(vm.Reason.Value))
                b.BeginStop("reason").AddItem(ControlId.Structural(k + "reason"),
                    GraphNodes.Text(() => vm.Reason.Value));

            // Iron Man note (shown by the view only when a downgraded save exists).
            if (downgraded)
                b.BeginStop("desc").AddItem(ControlId.Structural(k + "desc"), GraphNodes.Text(
                    () => GameText.Or(() => UIStrings.Instance.GameOverScreen.GameOverIronManDescription,
                        "gameover.ironman_description")));

            // Each button is its OWN Tab-stop (BeginStop per node), so Shift+Tab / Tab cycle
            // reason → buttons — arrows never cross a stop. This is the modal convention shared with
            // MessageBoxScreen, NOT the single-stop arrow list an Esc-style menu uses.

            // Normal run: Quick Load (gated on the async CanQuickLoad) + Load.
            if (!ironMan)
            {
                b.BeginStop("quickload").AddItem(ControlId.Structural(k + "quickload"), GraphNodes.Button(
                    () => GameText.Or(() => UIStrings.Instance.GameOverScreen.QuickLoadLabel, "gameover.quick_load"),
                    () => vm.OnQuickLoad(), () => vm.CanQuickLoad.Value));
                b.BeginStop("load").AddItem(ControlId.Structural(k + "load"), GraphNodes.Button(
                    () => GameText.Or(() => UIStrings.Instance.GameOverScreen.LoadLabel, "gameover.load"),
                    () => vm.OnButtonLoadGame()));
            }

            // Main Menu — hidden only in the Iron-Man-with-a-downgraded-save case (view: !isIronMan || !downgraded).
            // Fallback reuses the shared screen.main_menu key (the same one EscMenuScreen's Main Menu falls back to).
            if (!downgraded)
                b.BeginStop("mainmenu").AddItem(ControlId.Structural(k + "mainmenu"), GraphNodes.Button(
                    () => GameText.Or(() => UIStrings.Instance.GameOverScreen.MainMenuLabel, "screen.main_menu"),
                    () => vm.OnButtonMainMenu()));

            // Iron Man recovery pair — delete the corrupted-run save (→ main menu) or downgrade it to continue.
            if (downgraded)
            {
                b.BeginStop("irondelete").AddItem(ControlId.Structural(k + "irondelete"), GraphNodes.Button(
                    () => GameText.Or(() => UIStrings.Instance.GameOverScreen.IronManDeleteSaveLabel,
                        "gameover.ironman_delete"),
                    () => vm.OnIronManDeleteSave()));
                b.BeginStop("ironcontinue").AddItem(ControlId.Structural(k + "ironcontinue"), GraphNodes.Button(
                    () => GameText.Or(() => UIStrings.Instance.GameOverScreen.IronManContinueGameLabel,
                        "gameover.ironman_continue"),
                    () => vm.OnIronManContinueGame()));
            }
        }
    }
}
