using System.Collections.Generic;
using Kingmaker.Code.UI.MVVM.VM.Settings.Entities;
using Kingmaker.Code.UI.MVVM.VM.Settings.Entities.Decorative;
using Owlcat.Runtime.UI.MVVM;
using RTAccess.UI;
using RTAccess.UI.Proxies;

namespace RTAccess.Screens
{
    /// <summary>
    /// Builds nav elements from a settings-entity collection (the SettingsEntity* VMs), grouping runs of
    /// entities under their headers into labeled sections. Key bindings / difficulty / accessibility-image
    /// entities fall back to <see cref="ProxyUnsupportedSetting"/>. ADAPTER-ERA: only the New Game
    /// difficulty phase still builds through here — the Settings screen migrated to the graph path
    /// (<see cref="SettingsEntityGraph"/>); this dies with the wizard's migration.
    /// </summary>
    internal static class SettingsEntityBuilder
    {
        /// <param name="tree">When true, header runs become collapsible <see cref="TreeGroup"/>s (one
        /// Tab-stop, Down/Up over expanded controls, Right/Left expand/collapse). When false, they're
        /// <see cref="Panel"/> sections (each control its own Tab-stop).</param>
        public static void BuildInto(Container content, IEnumerable<VirtualListElementVMBase> entities,
            bool tree = false)
        {
            if (entities == null) return;
            Container section = null;
            foreach (var e in entities)
            {
                if (e is SettingsEntityHeaderVM header)
                {
                    string label = header.Tittle?.Text;
                    section = tree ? (Container)new TreeGroup(label) : new Panel(label);
                    content.Add(section);
                    continue;
                }
                var proxy = MakeProxy(e);
                if (proxy != null) (section ?? content).Add(proxy);
            }
        }

        public static UIElement MakeProxy(VirtualListElementVMBase e)
        {
            if (e is SettingsEntityBoolVM b) return new ProxyToggle(b);
            if (e is SettingsEntitySliderVM s) return new ProxySlider(s);
            if (e is SettingsEntityDropdownVM d) return new ProxyDropdown(d); // difficulty dropdown is a subclass — caught here, generic for now
            if (e is SettingsEntityVM sv) return new ProxyUnsupportedSetting(sv.Title?.Text); // keybinding / opt-out / images
            return null;
        }
    }
}
