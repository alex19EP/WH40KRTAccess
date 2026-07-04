using UnityEngine;
using Kingmaker;
using Kingmaker.Settings;
using Kingmaker.Settings.Entities;

namespace RTAccess.Input
{
    /// <summary>
    /// Moves a handful of the GAME's own hotkeys off their bare letter keys and onto Ctrl+letter, using the
    /// game's real keybinding-settings path (<see cref="SettingsEntityKeyBindingPair.SetKeyBindingDataAndConfirm"/>).
    /// This is the "merge, don't re-implement" half of the input model: instead of the mod stealing the bare
    /// letters and muting the whole game keyboard, we make the game itself vacate C/I/J/M/L/Y/V/B/N (the nine
    /// service-window openers) plus X (ChangeWeaponSet) so the mod's own bare-letter verbs can own them cleanly —
    /// and the game's functions stay reachable on Ctrl+letter. See <see cref="Vacated"/> for the exact set and why
    /// the other P/X/R collisions are deliberately left alone.
    ///
    /// Because we drive the game's OWN rebind path, two things come for free: the window still registers in
    /// <c>KeyboardAccess</c> (on Ctrl+letter), and every UI/tutorial key-hint auto-updates, because
    /// <c>RenewRegisteredBindings</c> raises <c>IKeybindChanged</c> — the same event the in-game Controls screen
    /// fires when the player rebinds. See docs/input-system-architecture-review.md and the
    /// <c>rt-input-system-verdict</c> memory.
    ///
    /// NOTE (persistence): confirming the value writes it to the player's Controls config (the only way the hint
    /// text follows), so it persists across sessions and is visible/re-bindable in the game's Controls screen.
    /// <see cref="Revert"/> restores the defaults (for a future revert-on-disable hook). We only shift a binding
    /// that is still on its expected BARE key, so a player's own custom rebind is never clobbered.
    ///
    /// Coupling: while <see cref="FocusMode"/> holds <c>KeyboardAccess.Disabled</c> the whole game keyboard is
    /// muted, so the Ctrl+letter windows only fire once the blanket mute is replaced by per-chord arbitration.
    /// This class just frees the letters + updates the hints; it is safe (non-regressing) on its own.
    /// </summary>
    public static class GameKeybinds
    {
        // Game keys the mod vacates onto Ctrl+letter, paired with the bare KeyCode each defaults to, so the mod's
        // own bare-letter verbs can own them. Two kinds:
        //  - the nine service-window openers (General group);
        //  - ChangeWeaponSet (ActionBar group) — the one P/X/R read-key collision that actually mattered: it's
        //    combat-critical yet was suppressed on bare X (the mod's "where am I" claims X). Moving it to Ctrl+X
        //    makes weapon-set swap reachable again (the mod doesn't claim Ctrl+X). NOTE: the swap itself is still
        //    silent (the game gives no audio) — a follow-up could wrap it with an announce.
        // The other bare-key collisions are left alone on purpose: ShowHideCombatLog (P), CameraRotateToPointNorth
        // (X) and FlipZoneStrategist (R) are visual/redundant for a blind player, so the mod's party-read (P) /
        // where-am-I (X) / status (R) keep those keys. Everything else (movement, save/load, end-turn, tabs,
        // character switch) keeps its native binding.
        private static readonly (Func<ControlsKeybindingsSettings, SettingsEntityKeyBindingPair> pick, KeyCode key, string label)[] Vacated =
        {
            (k => k.General.OpenCharacterScreen,   KeyCode.C, "OpenCharacterScreen"),
            (k => k.General.OpenInventory,         KeyCode.I, "OpenInventory"),
            (k => k.General.OpenJournal,           KeyCode.J, "OpenJournal"),
            (k => k.General.OpenMap,               KeyCode.M, "OpenMap"),
            (k => k.General.OpenEncyclopedia,      KeyCode.L, "OpenEncyclopedia"),
            (k => k.General.OpenColonyManagement,  KeyCode.Y, "OpenColonyManagement"),
            (k => k.General.OpenShipCustomization, KeyCode.V, "OpenShipCustomization"),
            (k => k.General.OpenCargoManagement,   KeyCode.B, "OpenCargoManagement"),
            (k => k.General.OpenFormation,         KeyCode.N, "OpenFormation"),
            // DLC3 Augmentations opener — missed by the original nine (M0 keybind dump found it live on
            // bare U in StarSystem mode); U stays a free letter for mod verbs.
            (k => k.General.OpenAugmentations,     KeyCode.U, "OpenAugmentations"),
            (k => k.ActionBar.ChangeWeaponSet,     KeyCode.X, "ChangeWeaponSet"),
        };

        private static bool _applied;

        /// <summary>Idempotent; retries until the settings + game keyboard are ready. Safe to call every frame.</summary>
        public static void ApplyWindowOpenerRebinds()
        {
            if (_applied) return;
            var kb = ReadyKeybindings();
            if (kb == null) return; // settings/keyboard not up yet — retry next frame

            int moved = 0;
            foreach (var o in Vacated)
                moved += Shift(o.pick(kb), o.key, ctrlTarget: true) ? 1 : 0;

            RenewAll();
            _applied = true;
            Main.Log?.Log($"GameKeybinds: moved {moved}/{Vacated.Length} game key(s) to Ctrl+letter.");
        }

        /// <summary>Restore the vacated keys to their bare-letter defaults (revert-on-disable hook).</summary>
        public static void Revert()
        {
            var kb = ReadyKeybindings();
            if (kb == null) return;
            foreach (var o in Vacated)
                Shift(o.pick(kb), o.key, ctrlTarget: false);
            RenewAll();
            _applied = false;
            Main.Log?.Log("GameKeybinds: reverted game keys to bare letters.");
        }

        // The keybinding settings root (General + ActionBar + …), or null if the settings root / game keyboard
        // isn't ready yet.
        private static ControlsKeybindingsSettings ReadyKeybindings()
        {
            try
            {
                if (!SettingsRoot.Initialized) return null;
                if (Game.Instance?.Keyboard == null) return null;       // RenewRegisteredBindings needs it
                if (Game.Instance?.UISettingsManager == null) return null;
                return SettingsRoot.Controls?.Keybindings;
            }
            catch { return null; }
        }

        // Shift Binding1 between the bare key and Ctrl+key. Only touches it when it currently sits on the exact
        // slot we expect (bare when moving to Ctrl, Ctrl when moving back), so a player's custom rebind is left
        // alone and repeated calls are no-ops. Returns whether it changed anything.
        private static bool Shift(SettingsEntityKeyBindingPair e, KeyCode key, bool ctrlTarget)
        {
            if (e == null) return false;
            var b = e.GetValue().Binding1;
            bool bareNow = b.Key == key && !b.IsCtrlDown && !b.IsAltDown && !b.IsShiftDown;
            bool ctrlNow = b.Key == key && b.IsCtrlDown && !b.IsAltDown && !b.IsShiftDown;

            if (ctrlTarget)
            {
                if (ctrlNow || !bareNow) return false; // already moved, or player-customized → leave it
                e.SetKeyBindingDataAndConfirm(new KeyBindingData { Key = key, IsCtrlDown = true }, 0);
                return true;
            }
            if (bareNow || !ctrlNow) return false;     // already bare, or player-customized → leave it
            e.SetKeyBindingDataAndConfirm(new KeyBindingData { Key = key }, 0);
            return true;
        }

        // Re-register every keybinding into KeyboardAccess and raise IKeybindChanged so UI/tutorial hints refresh.
        // Belt-and-suspenders: SetKeyBindingDataAndConfirm already does this per-entity via the linked
        // UISettingsEntityKeyBinding's OnTempValueChanged subscription, but this guarantees it even if an asset
        // wasn't linked when we ran.
        private static void RenewAll()
        {
            try
            {
                foreach (var kb in Game.Instance.UISettingsManager.KeyBindings)
                    kb.RenewRegisteredBindings();
            }
            catch (Exception e) { Main.Log?.Log("GameKeybinds.RenewAll: " + e.Message); }
        }
    }
}
