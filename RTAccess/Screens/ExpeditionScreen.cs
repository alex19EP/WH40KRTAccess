using System.Collections.Generic;
using Kingmaker.Blueprints.Root.Strings;          // UIStrings (expedition labels)
using Kingmaker.Code.UI.MVVM.VM.Exploration;      // ExplorationExpeditionVM
using RTAccess.UI;
using RTAccess.UI.Graph;
using UnityEngine;

namespace RTAccess.Screens
{
    /// <summary>
    /// The expedition sub-dialog an Expedition POI opens on the exploration tablet: choose how many people to
    /// send (the game's slider, 1..Max — larger crews unlock higher reward tiers) and Send. Mirrors
    /// <see cref="ExplorationExpeditionVM"/> — view-owned (<c>ExplorationExpeditionPCView.ViewModel</c>), so
    /// the instance is located through the scene view and cached. Left/Right adjust the crew size through the
    /// VM's own SetPeopleCount; Send runs SendExpedition (the game's own confirm toast follows); Escape hides.
    /// </summary>
    public sealed class ExpeditionScreen : Screen
    {
        public override string Key => "expedition";
        public override int Layer => 26;            // a modal on the tablet (9)
        public override bool Exclusive => true;
        public override string ScreenName
            => GameText.Or(() => UIStrings.Instance.ExplorationTexts.ExpeditionHeader, "expedition.screen");

        public override bool IsActive() => Vm()?.ShouldShow.Value == true;

        private static Kingmaker.UI.MVVM.View.Exploration.PC.ExplorationExpeditionPCView _view;
        private static ExplorationExpeditionVM Vm()
        {
            if (_view == null)
                _view = UnityEngine.Object.FindAnyObjectByType<Kingmaker.UI.MVVM.View.Exploration.PC.ExplorationExpeditionPCView>(
                    FindObjectsInactive.Include);
            return _view != null ? _view.ViewModel : null;
        }

        public override bool BuildsGraph => true;

        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            string k = "exped:" + vm.GetHashCode() + ":";

            // The crew-size slider: Left/Right step through the VM's own setter; the live value part speaks
            // each step ("people: N of Max"). Reward tiers unlock as the count crosses their thresholds —
            // the tier line below reads the live unlocked index.
            Func<string> value = () => Loc.T("expedition.people",
                new { n = vm.PeopleCount.Value, max = vm.MaxPeopleCount.Value });
            b.AddItem(ControlId.Structural(k + "count"), new NodeVtable
            {
                ControlType = ControlTypes.Slider,
                Announcements = new List<NodeAnnouncement>
                {
                    GraphNodes.LabelPart(() => Loc.T("expedition.crew")),
                    new NodeAnnouncement(value, live: true, kind: AnnouncementKinds.Value),
                },
                StateText = value,
                OnAdjust = (sign, large) =>
                    vm.SetPeopleCount(Mathf.Clamp(vm.PeopleCount.Value + sign, 1, vm.MaxPeopleCount.Value)),
            });
            b.AddLabel(ControlId.Structural(k + "tier"), () =>
                Loc.T("expedition.tier", new { n = vm.UnlockedRewardIndex.Value + 1 }));
            b.AddItem(ControlId.Structural(k + "send"), GraphNodes.Button(
                () => GameText.Or(() => UIStrings.Instance.ExplorationTexts.ExpeditionSendButtonLabel, "expedition.send"),
                () => vm.SendExpedition()));
        }

        public override IEnumerable<ElementAction> GetActions()
        {
            var vm = Vm();
            if (vm == null) yield break;
            yield return new ElementAction(ActionIds.Back, Message.Raw(GameText.Action("close")), _ => vm.Hide());
        }
    }
}
