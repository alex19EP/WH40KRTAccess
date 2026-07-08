using Kingmaker;                                     // Game
using Kingmaker.Blueprints.Root.Strings;             // UIStrings (reuse the game's localized HUD labels)
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows;      // ServiceWindowsType
using Kingmaker.Stores;                              // StoreManager (Augmentations DLC gate)
using Kingmaker.Stores.DlcInterfaces;                // DlcNameEnum

namespace RTAccess.Screens
{
    /// <summary>
    /// One home for the service-window display name + availability gate, shared by the HUD windows list
    /// (<see cref="InGameScreen"/>), the star-system openers (<see cref="SystemMapScreen"/>), and the
    /// on-open announcer (<see cref="RTAccess.Accessibility.ServiceWindowAnnounce"/>). Keeps the label/gate
    /// from drifting per caller (the announcer had silently lost <c>Augmentations</c>).
    /// </summary>
    internal static class ServiceWindowInfo
    {
        /// <summary>The window's localized display name, preferring the game's own HUD string (so it follows the
        /// game's language) with a mod fallback key. Null for <see cref="ServiceWindowsType.None"/> / any unknown
        /// type — callers announcing on open stay silent, and the HUD list adds its own <c>ToString()</c> guard.</summary>
        public static string Label(ServiceWindowsType type)
        {
            switch (type)
            {
                case ServiceWindowsType.Inventory: return GameText.Or(() => UIStrings.Instance.MainMenu.Inventory, "screen.inventory");
                case ServiceWindowsType.CharacterInfo: return GameText.Or(() => UIStrings.Instance.MainMenu.CharacterInfo, "screen.character");
                case ServiceWindowsType.Journal: return GameText.Or(() => UIStrings.Instance.MainMenu.Journal, "screen.journal");
                case ServiceWindowsType.LocalMap: return GameText.Or(() => UIStrings.Instance.MainMenu.LocalMap, "screen.map");
                case ServiceWindowsType.Encyclopedia: return GameText.Or(() => UIStrings.Instance.MainMenu.Encyclopedia, "screen.encyclopedia");
                case ServiceWindowsType.ShipCustomization: return GameText.Or(() => UIStrings.Instance.MainMenu.ShipCustomization, "screen.ship");
                case ServiceWindowsType.ColonyManagement: return GameText.Or(() => UIStrings.Instance.MainMenu.ColonyManagement, "screen.colony");
                case ServiceWindowsType.CargoManagement: return GameText.Or(() => UIStrings.Instance.MainMenu.CargoManagement, "screen.cargo");
                case ServiceWindowsType.Augmentations: return GameText.Or(() => UIStrings.Instance.MainMenu.Augmentations, "screen.augmentations");
                default: return null;
            }
        }

        /// <summary>Availability gate mirroring the game's own HUD button visibility
        /// (IngameMenuNewPCView.CheckEnabled* / CheckServiceWindowsBlocked). The original five stay
        /// always-offered; the four RT windows read as disabled exactly when the game would hide their buttons.</summary>
        public static bool Enabled(ServiceWindowsType type)
        {
            var player = Game.Instance?.Player;
            if (player == null) return false;
            switch (type)
            {
                case ServiceWindowsType.ShipCustomization:
                {
                    bool canShip = player.CanAccessStarshipInventory;
                    bool blocked = player.ServiceWindowsBlocked;
                    return canShip && !blocked;
                }
                case ServiceWindowsType.ColonyManagement:
                {
                    bool canShip = player.CanAccessStarshipInventory;
                    bool forbid = player.ColoniesState.ForbidColonization;
                    return canShip && !forbid;
                }
                case ServiceWindowsType.CargoManagement:
                {
                    bool blocked = player.ServiceWindowsBlocked;
                    return !blocked;
                }
                case ServiceWindowsType.Augmentations:
                {
                    bool augBlocked = player.AugmentationsWindowBlocked;
                    return StoreManager.CheckIfDlcPurchasedAndInstalled(DlcNameEnum.DLC3TheInfiniteMuseion) && !augBlocked;
                }
                default:
                    return true;
            }
        }
    }
}
