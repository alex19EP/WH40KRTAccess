using Kingmaker.Code.UI.MVVM.VM.NewGame.Story;
using Kingmaker.DLC;
using Kingmaker.Stores.DlcInterfaces;
using RTAccess.UI.Announcements;

namespace RTAccess.UI.Proxies
{
    /// <summary>
    /// One integral-DLC on/off switch in the New Game story picker. The game draws these beneath the
    /// selected campaign (from <c>campaign.AdditionalContentDlc</c>), letting you enable/disable DLC
    /// content for this playthrough; omitting them left a blind player stuck on the base game with no
    /// way to turn any DLC on. Announced "&lt;DLC name&gt;, toggle, on/off, [disabled]" — with a status
    /// ("not owned", "downloading", "not installed", "coming soon") in place of on/off when the DLC
    /// can't be switched. Activating a switchable DLC flips its inclusion
    /// (<see cref="BlueprintDlc.SwitchDlcValue"/>) and re-announces the new value in place. The DLC's own
    /// description is shown in the shared story panel while the switch is focused (driven by NewGameScreen).
    ///
    /// Purchase/install/store actions are intentionally out of scope: those need the store/network flow
    /// and a restart, whereas the on/off switch is what a new game actually consumes. A not-owned or
    /// not-installed DLC reads as disabled with its status so the player still learns it exists.
    /// </summary>
    [AnnouncementOrder(typeof(LabelAnnouncement), typeof(RoleAnnouncement), typeof(ValueAnnouncement),
        typeof(EnabledAnnouncement), typeof(PositionAnnouncement))]
    public sealed class ProxyDlcToggle : UIElement
    {
        private readonly NewGamePhaseStoryScenarioEntityIntegralDlcVM _vm;

        public ProxyDlcToggle(NewGamePhaseStoryScenarioEntityIntegralDlcVM vm) { _vm = vm; }

        /// <summary>The backing VM (campaign + DLC blueprint) — the screen reads it to drive the shared
        /// story description panel to this DLC while it's focused, mirroring the game's focus behaviour.</summary>
        public NewGamePhaseStoryScenarioEntityIntegralDlcVM DlcVm => _vm;

        public override bool ReannounceOnActivate => true; // switching flips the value in place

        private BlueprintDlc Dlc => _vm?.BlueprintDlc;

        // The switch is live only for an owned, fully-installed DLC — matching the game's story view,
        // which shows the on/off button only then (and offers purchase/install otherwise). A DLC that is
        // NotLoaded (bought, not installed) or Loading (mid-download) isn't switchable.
        private bool Switchable
        {
            get
            {
                if (Dlc == null || !Dlc.IsPurchased) return false;
                var state = Dlc.GetDownloadState();
                return state != DownloadState.NotLoaded && state != DownloadState.Loading;
            }
        }

        private bool IsOn => Dlc != null && Dlc.GetDlcSwitchOnOffState();

        public override IEnumerable<Announcement> GetFocusAnnouncements()
        {
            yield return new LabelAnnouncement(Message.Raw(_vm?.Title ?? ""));
            yield return new RoleAnnouncement("toggle");
            yield return new ValueAnnouncement(ValueText());
            yield return new EnabledAnnouncement(Switchable);
        }

        private Message ValueText()
        {
            if (Dlc == null) return Message.Empty;
            if (Switchable)
                return Message.Localized("ui", IsOn ? "value.on" : "value.off");
            if (!Dlc.IsPurchased)
                return Message.Localized("ui",
                    Dlc.GetPurchaseState() == BlueprintDlc.DlcPurchaseState.ComingSoon
                        ? "value.coming_soon" : "value.not_owned");
            // Owned but not ready: either still downloading or not installed at all.
            return Message.Localized("ui",
                Dlc.GetDownloadState() == DownloadState.Loading ? "value.downloading" : "value.not_installed");
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            if (Switchable)
                yield return new ElementAction(ActionIds.Activate, Message.Localized("ui", "action.toggle"),
                    _ => Dlc.SwitchDlcValue(!IsOn));
        }
    }
}
