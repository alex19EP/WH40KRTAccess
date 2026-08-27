using Kingmaker.Blueprints.Root.Strings;
using Kingmaker.Localization;

namespace RTAccess.Localization
{
    /// <summary>
    /// Mod locale keys whose concept the GAME already ships a string for, mapped to the game's own
    /// <see cref="UIStrings"/> accessor. <see cref="LocalizationManager.Get"/> consults this between the
    /// player's own mod translation and our English fallback, so every one of these reads in the player's
    /// language on all nine locales the game ships — instead of falling back to English because only
    /// <c>enGB</c> exists in <c>assets/locale/</c>.
    ///
    /// <para><b>Entries are hand-verified, not text-matched.</b> They were found by dumping every
    /// <see cref="LocalizedString"/> under <c>UIStrings</c> from the live game and matching English text,
    /// but a text match alone is a trap: the game says "Actions" in
    /// <c>Tooltips.AttackAbilityGroupCooldownShort</c> (a cooldown label) and "Exit" in
    /// <c>MainMenu.Exit</c> (quit the game) — reuse either and non-English players hear nonsense, which
    /// the mod cannot detect because the fallback only triggers on an EMPTY game string. So an entry
    /// belongs here only when the accessor's own DOMAIN and SENSE match the mod's use, not merely its
    /// English spelling. When in doubt, leave the key out; our English is the safe answer.</para>
    ///
    /// <para><b>This table is the ONLY source for these keys</b> — their <c>ui.json</c> entries were
    /// removed once the mapping was verified, so deleting a line here without restoring the key to
    /// <c>ui.json</c> makes the mod speak the raw key. That is a deliberate trade: the accessors are
    /// typed, so a game patch that renames a field breaks the BUILD rather than rotting silently at
    /// runtime, and every entry was checked to be non-empty in all nine shipped locales. The residual
    /// risk is a read before the blueprint root is up; all mapped keys are screen labels that cannot be
    /// reached that early. See <see cref="GameText"/> for the same idea at an individual call site (that
    /// one DOES keep a <c>ui.json</c> fallback key).</para>
    /// </summary>
    internal static class GameStrings
    {
        // Only the "ui" table has entries today; the key is the table's own key, unchanged.
        private static readonly Dictionary<string, Func<LocalizedString>> Ui =
            new Dictionary<string, Func<LocalizedString>>(StringComparer.Ordinal)
        {
            // --- character sheet / character creation ---
            { "charinfo.biography", () => UIStrings.Instance?.CharacterSheet?.Biography },                  // Biography
            { "charinfo.character", () => UIStrings.Instance?.MainMenu?.CharacterInfo },                    // Character
            { "inv.character", () => UIStrings.Instance?.MainMenu?.CharacterInfo },                         // Character
            { "chargen.edit_name", () => UIStrings.Instance?.CharGen?.EditName },                           // Edit name
            { "chargen.name_entry", () => UIStrings.Instance?.CharGen?.EditName },                          // Edit name
            { "levelup.background", () => UIStrings.Instance?.CharGen?.Background },                        // Background
            { "levelup.button", () => UIStrings.Instance?.MainMenu?.LevelUp },                              // Level Up
            { "levelup.title", () => UIStrings.Instance?.MainMenu?.LevelUp },                               // Level up

            // --- inventory ---
            { "inv.equipment", () => UIStrings.Instance?.CharacterSheet?.Equipment },                       // Equipment
            { "inv.inventory", () => UIStrings.Instance?.MainMenu?.Inventory },                             // Inventory
            { "stash.inventory", () => UIStrings.Instance?.MainMenu?.Inventory },                           // Inventory
            { "inv.search", () => UIStrings.Instance?.CommonTexts?.Search },                                // Search
            { "item.grade.quest", () => UIStrings.Instance?.QuestNotificationTexts?.Quest },                // quest
            // NOT mapped: InventoryScreen.FilterTextNotable is "Notable" in English but a FILTER-TAB
            // label — ruRU renders it "Квестовые" (plural "quest ones"). ItemNodes uses item.notable as a
            // per-item badge, so it would read "Меч (Квестовые, квест)" and duplicate the grade badge.
            // A live readout caught this after the English diff passed: check the TRANSLATION too.

            // --- journal / "new" badges ---
            { "journal.quest", () => UIStrings.Instance?.QuestNotificationTexts?.Quest },                   // Quest
            { "systemmap.has_quest", () => UIStrings.Instance?.QuestNotificationTexts?.Quest },             // quest
            { "systemmap.has_rumour", () => UIStrings.Instance?.QuestNotificationTexts?.Rumour },           // rumour
            { "ency.unread", () => UIStrings.Instance?.QuestNotificationTexts?.New },                       // new
            { "cargo.new", () => UIStrings.Instance?.QuestNotificationTexts?.New },                         // new
            { "vendor.cargo_new", () => UIStrings.Instance?.QuestNotificationTexts?.New },                  // new

            // --- menus / screen names ---
            { "label.continue", () => UIStrings.Instance?.MainMenu?.Continue },                             // Continue
            { "screen.settings", () => UIStrings.Instance?.MainMenu?.Settings },                            // Settings
            { "mods.settings", () => UIStrings.Instance?.MainMenu?.Settings },                              // Settings
            { "screen.credits", () => UIStrings.Instance?.MainMenu?.Credits },                              // Credits
            { "titles.screen", () => UIStrings.Instance?.MainMenu?.Credits },                               // Credits
            { "save.mode.load", () => UIStrings.Instance?.SaveLoadTexts?.LoadLabel },                       // Load
            { "tutorial.next_page", () => UIStrings.Instance?.Credits?.NextPage },                          // Next page
            { "tutorial.prev_page", () => UIStrings.Instance?.Credits?.PreviousPage },                      // Previous page

            // --- combat / inspect ---
            { "cover.spot", () => UIStrings.Instance?.CombatTexts?.Cover },                                 // Cover
            { "taxonomy.cover", () => UIStrings.Instance?.CombatTexts?.Cover },                             // Cover
            { "unit.dead", () => UIStrings.Instance?.CombatTexts?.HPDead },                                 // dead
            { "stat.damage", () => UIStrings.Instance?.Tooltips?.Damage },                                  // Damage
            { "tooltip.armor_deflection", () => UIStrings.Instance?.Inspect?.DamageDeflection },            // Deflection
            { "log.tab.combat", () => UIStrings.Instance?.CombatTexts?.CombatLogCombatFilter },             // Combat
            { "log.tab.dialogue", () => UIStrings.Instance?.CombatTexts?.CombatLogDialogueFilter },         // Dialogue
            { "log.tab.all", () => UIStrings.Instance?.InventoryScreen?.FilterTextAll },                    // All

            // --- exploration / scanner ---
            { "scan.singular.door", () => UIStrings.Instance?.Tooltips?.Door },                             // Door
            { "scan.singular.trap", () => UIStrings.Instance?.Tooltips?.Trap },                             // Trap
            { "scan.unit_talk", () => UIStrings.Instance?.ActionTexts?.Talk },                              // talk
            { "loot.container", () => UIStrings.Instance?.Tooltips?.Loot },                                 // Loot
            { "marker.loot", () => UIStrings.Instance?.Tooltips?.Loot },                                    // loot
            // NOT mapped, though the English matches after normalisation: the game's own wording carries
            // punctuation or a placeholder our label must not speak — ExploPointsOfInterest is
            // "Points of interest:", ExploObjectResources is "Resources:", and SystemMap.PercentExplored
            // is the template "{0} explored". Any new entry must keep the EXACT English wording.

            // --- colony projects ---
            { "exploration.open_projects", () => UIStrings.Instance?.ColonyProjectsTexts?.HeaderDefault },  // Colony projects
            { "exploration.projects", () => UIStrings.Instance?.ColonyProjectsTexts?.OpenProjectsButton },  // Projects
            { "exploration.show_finished", () => UIStrings.Instance?.ColonyProjectsTexts?.ShowFinishedProjectsButton }, // Show finished projects

            // --- sector-map passage danger tiers ---
            { "sectormap.tier_safe", () => UIStrings.Instance?.GlobalMapPassages?.Safe },                   // safe
            { "sectormap.tier_unsafe", () => UIStrings.Instance?.GlobalMapPassages?.Unsafe },               // unsafe
            { "sectormap.tier_dangerous", () => UIStrings.Instance?.GlobalMapPassages?.Dangerous },         // dangerous

            // --- voidship combat ---
            { "spacecombat.sector_fore", () => UIStrings.Instance?.Inspect?.Fore },                         // fore
            { "spacecombat.sector_aft", () => UIStrings.Instance?.Inspect?.Aft },                           // aft
            { "spacecombat.sector_starboard", () => UIStrings.Instance?.Inspect?.WeaponSlotStarboard },     // starboard
            { "spacecombat.arc_keel", () => UIStrings.Instance?.Inspect?.WeaponSlotKeel },                  // keel
            { "spacecombat.effects_none", () => UIStrings.Instance?.Inspect?.NoStatusEffects },             // No effects
            { "spacecombat.posts", () => UIStrings.Instance?.HUDTexts?.PostsBar },                          // Posts

            // --- misc ---
            { "formation.name.default", () => UIStrings.Instance?.SettingsUI?.Default },                    // Default
            { "statcheck.confirm_unit", () => UIStrings.Instance?.SettingsUI?.MenuConfirm },                // Confirm
            { "value.coming_soon", () => UIStrings.Instance?.DlcManager?.ComingSoon },                      // coming soon
        };

        /// <summary>How many keys are served from the game (for the boot log / diagnostics).</summary>
        public static int Count => Ui.Count;

        /// <summary>The game's own text for this key, or null when there is no mapping, the blueprint
        /// root isn't loaded yet, or the game shipped the string blank — every one of which must fall
        /// through to the mod's table rather than speak an empty line.</summary>
        public static string Resolve(string table, string key)
        {
            if (table != "ui" || key == null) return null;
            Func<LocalizedString> get;
            if (!Ui.TryGetValue(key, out get)) return null;
            try
            {
                var s = get()?.Text;
                return string.IsNullOrEmpty(s) ? null : s;
            }
            catch (Exception e)
            {
                Main.Log?.Error("[loc] game string for " + key + ": " + e.Message);
                return null;
            }
        }
    }
}
