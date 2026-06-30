#if DEBUG
using HarmonyLib;
using Kingmaker.UI.Legacy.MainMenuUI;

namespace RTAccess.Dev;

/// <summary>
/// Dev-only: skip the company-logo splash sequence at boot to shave a few seconds off every dev relaunch.
///
/// The game's <see cref="SplashScreenController"/> already has the exact branch we want — when
/// <c>GameStarter.IsSkippingMainMenu()</c> is true it runs <c>SkipWaitingSplashScreens()</c> instead of
/// playing the logos. But the two built-in flags that set that (<c>-start_from</c> / <c>skipmainmenu</c>)
/// ALSO skip the main menu, and <c>skipmainmenu</c> auto-starts a new game (see GameMainMenu.Start). We
/// want the logos gone yet still land on the main menu (the dev harness loads saves from there), so we take
/// only the splash-skip coroutine and leave IsSkippingMainMenu false.
///
/// Gating: prefix runs only when the dev gate is open (<see cref="DevServer.IsEnabled"/> — RTACCESS_DEV=1 or
/// the marker file the dev launcher arms), and the whole file is compiled out of Release, so shipping
/// players always see the logos.
///
/// Timing: UMM loads RTAccess during GameStarter's <c>ModInitializer.InitializeMods()</c>, which runs before
/// <c>OnInitComplete</c> fires <see cref="SplashScreenController.ShowSplashScreen"/> — so PatchAll has
/// already run by the time the splash starts and this prefix catches it.
/// </summary>
[HarmonyPatch(typeof(SplashScreenController), nameof(SplashScreenController.ShowSplashScreen))]
internal static class SkipSplashPatch
{
    private static bool Prefix(SplashScreenController __instance)
    {
        if (!DevServer.IsEnabled) return true; // dev gate closed → original logos play

        // Mirror the game's own skip path exactly (Code.dll is publicized, so the private coroutine is
        // callable): fade nothing, wait for the loading screen, then kick the main-menu load.
        __instance.StartCoroutine(__instance.SkipWaitingSplashScreens());
        return false; // suppress the original splash sequence
    }
}
#endif
