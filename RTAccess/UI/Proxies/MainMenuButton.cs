using System;
using Kingmaker.Code.UI.MVVM.VM.ContextMenu;

namespace RTAccess.UI.Proxies
{
    /// <summary>
    /// Builds a <see cref="ProxyActionButton"/> for a main-menu sidebar entry (Continue / New Game / …).
    /// An entry is just label + enabled + run-command, so it's a plain button. Enabled is read from the
    /// LIVE <see cref="ContextMenuCollectionEntity.IsEnabled"/> (which re-invokes the entry's Condition
    /// each call) rather than the VM's cached <c>IsEnabled</c> reactive (stale until RefreshEnabling); a
    /// separator entry isn't focusable. Code.dll is publicized, so the entity is reachable directly.
    /// </summary>
    public static class MainMenuButton
    {
        public static ProxyActionButton For(ContextMenuEntityVM vm)
        {
            var entity = vm?.m_Entity;
            Func<bool> enabled = () => entity != null ? entity.IsEnabled : (vm != null && vm.IsEnabled.Value);
            return new ProxyActionButton(
                () =>
                {
                    var t = vm?.Title?.Value;
                    if (!string.IsNullOrEmpty(t)) return t;
                    return entity?.Title?.Text ?? entity?.TitleText ?? "";
                },
                enabled,
                () => vm?.Execute(),
                canFocus: () => vm != null && !vm.IsSeparator,
                // Match the sidebar's real per-entry sound (Analog for Continue/New Game/…, generic for
                // License/Feedback) instead of the default generic hover/click — the game sets these on the
                // OwlcatButton via SetClickSound/SetHoverSound from the same entity fields.
                hoverSoundType: entity?.HoverSoundType,
                clickSoundType: entity?.ClickSoundType);
        }
    }
}
