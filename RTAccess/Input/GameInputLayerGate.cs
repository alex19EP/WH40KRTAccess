using HarmonyLib;
using Owlcat.Runtime.UI.ConsoleTools.GamepadInput;

namespace RTAccess.Input
{
    /// <summary>
    /// Suppresses the game's console-navigation input layers while our navigator owns the keyboard —
    /// the FOURTH input path, after raw KeyboardAccess (KeyboardArbitration), bare-letter keybinds
    /// (GameKeybinds) and Unity EventSystem Submit (DialogChoiceGate): game views push
    /// <c>GridConsoleNavigationBehaviour</c> input layers onto <c>GamePad.Instance</c> even on PC
    /// (e.g. <c>EscMenuBaseView.BuildNavigation</c>), and those layers read the KEYBOARD through
    /// Rewired — Enter is mapped to Confirm, arrows move the layer's own focus ring. So with our
    /// overlay focused, one physical Enter fired twice: our node activate AND the game ring's
    /// <c>OnConfirmClick</c> on whatever button the (invisible) ring sat on. Symptom that exposed it:
    /// declining the esc-menu quit confirm closed the box while the ring's confirm enqueued a fresh
    /// one via <c>CommonVM.m_MessageQueue</c> — a box that never dies.
    ///
    /// Every layer bind (confirm / decline / ring movement / hints) funnels through
    /// <c>BindDescription.Handler</c>, invoked by Rewired as a delegate (so a prefix applies despite
    /// the method's size). Gating here rather than per-view kills the whole leak class. Suppression is
    /// all-source (a gamepad would double-drive our focused overlay the same way); when nothing of
    /// ours is focused the game's layers work untouched, so sighted/parallel play is unaffected.
    /// </summary>
    [HarmonyPatch(typeof(BindDescription), nameof(BindDescription.Handler))]
    internal static class GameInputLayerGate
    {
        private static bool Prefix() => !UI.Navigation.HasFocus;
    }
}
