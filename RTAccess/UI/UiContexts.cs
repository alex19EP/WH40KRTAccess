using System;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.VM.Dialog;                    // DialogContextVM
using Kingmaker.Code.UI.MVVM.VM.GameOver;                  // GameOverVM
using Kingmaker.Code.UI.MVVM.VM.GroupChanger;              // GroupChangerVM
using Kingmaker.Code.UI.MVVM.VM.Loot;                      // LootVM
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows;            // ServiceWindowsVM
using Kingmaker.Code.UI.MVVM.VM.ServiceWindows.Inventory;  // InventoryVM
using Kingmaker.Code.UI.MVVM.VM.Space;                     // SpaceStaticPartVM
using Kingmaker.Code.UI.MVVM.VM.Surface;                   // SurfaceStaticPartVM
using Kingmaker.Code.UI.MVVM.VM.Transition;                // TransitionVM

namespace RTAccess.UI
{
    /// <summary>
    /// Resolves the service-window / loot / dialog / group-changer / transition VMs that hang off whichever
    /// static part is LIVE — Surface (in-area) OR Space (star-system). Every window that opens in both contexts
    /// hangs off its own <c>*StaticPartVM</c>, and exactly one of Surface/Space exists at a time (RootUIContext
    /// creates one per UI scene), so the correct read is "the surface one, else the space one" — the shape
    /// RootUIContext's own <c>HasDialog</c>/<c>IsInventoryShow</c>/… accessors use. Centralized here so the ~14
    /// screens that need it can't forget the Space half (which silently breaks a window in the star-system
    /// context) and a change to the context-tree shape is a one-site edit. <c>InGameScreen.StaticPart()</c>
    /// stays surface-only on purpose — the surface HUD is a surface-only concept.
    /// </summary>
    internal static class UiContexts
    {
        /// <summary>Read a VM off whichever static part is live: the surface selector first, else the space
        /// one. Null when neither context is up (main menu / loading) or the selector yields null.</summary>
        public static T FromLiveStaticPart<T>(Func<SurfaceStaticPartVM, T> surface, Func<SpaceStaticPartVM, T> space)
            where T : class
        {
            var rc = Game.Instance?.RootUiContext;
            if (rc == null) return null;
            var surf = rc.SurfaceVM?.StaticPartVM;
            var v = surf != null ? surface(surf) : null;
            if (v != null) return v;
            var sp = rc.SpaceVM?.StaticPartVM;
            return sp != null ? space(sp) : null;
        }

        public static ServiceWindowsVM ServiceWindows()
            => FromLiveStaticPart<ServiceWindowsVM>(s => s.ServiceWindowsVM, s => s.ServiceWindowsVM);

        public static InventoryVM Inventory()
            => FromLiveStaticPart<InventoryVM>(
                s => s.ServiceWindowsVM?.InventoryVM?.Value,
                s => s.ServiceWindowsVM?.InventoryVM?.Value);

        public static LootVM Loot()
            => FromLiveStaticPart<LootVM>(
                s => s.LootContextVM?.LootVM?.Value,
                s => s.LootContextVM?.LootVM?.Value);

        public static DialogContextVM Dialog()
            => FromLiveStaticPart<DialogContextVM>(s => s.DialogContextVM, s => s.DialogContextVM);

        public static GroupChangerVM GroupChanger()
            => FromLiveStaticPart<GroupChangerVM>(
                s => s.GroupChangerContextVM?.GroupChangerVm?.Value,
                s => s.GroupChangerContextVM?.GroupChangerVm?.Value);

        public static TransitionVM Transition()
            => FromLiveStaticPart<TransitionVM>(s => s.TransitionVM?.Value, s => s.TransitionVM?.Value);

        // The defeat screen (GameModeType.GameOver). Surface exposes it as its own ReactiveProperty; Space
        // routes it through the static-component dictionary (SpaceStaticComponentType.GameOver), so read it
        // back with the same cast the game's own consumers use.
        public static GameOverVM GameOver()
            => FromLiveStaticPart<GameOverVM>(
                s => s.GameOverVM?.Value,
                s => s.TryGetComponentVM(SpaceStaticComponentType.GameOver) as GameOverVM);
    }
}
