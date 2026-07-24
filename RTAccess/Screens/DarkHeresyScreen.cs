using Kingmaker;
using Kingmaker.Blueprints.Root.Strings;
using Kingmaker.Code.UI.MVVM.VM.DarkHeresyPopUp;
using Kingmaker.Code.UI.MVVM.View.DarkHeresyPopUp;
using RTAccess.UI;
using RTAccess.UI.Graph;

namespace RTAccess.Screens
{
    /// <summary>
    /// The Dark Heresy promo popup (<c>MainMenuVM.DarkHeresyPopUpVM</c>) — the ad the main menu raises once
    /// after the game's version changes (<c>MainMenuVM.IsVersionUpdated</c>). Two text lines and two
    /// buttons: wishlist (opens this store's product page) and close.
    ///
    /// <para><b>The VM outlives the window.</b> Closing is a VIEW operation (<c>DarkHeresyPopUpView.Hide</c>
    /// — a fade, then the object deactivates); nothing ever nulls <c>DarkHeresyPopUpVM.Value</c>. So
    /// "is it showing" must be read off the live view, and both this screen and
    /// <see cref="MainMenuScreen"/> gate on <see cref="IsShowing"/> — otherwise dismissing the popup would
    /// leave the whole main menu unreachable for the rest of the session (the menu excludes itself while a
    /// popup is up).</para>
    /// </summary>
    public sealed class DarkHeresyScreen : Screen
    {
        public DarkHeresyScreen() { Wrap = true; } // small modal — Tab wraps

        public override string Key => "overlay.darkheresy";
        public override string ScreenName => GameText.Or(() => UIStrings.Instance.UIDarkHeresyPopUp.Label, "darkheresy.title");
        public override int Layer => 27; // over the menu; below the message modal (30)
        public override bool Exclusive => true;

        private static DarkHeresyPopUpVM Vm()
            => Game.Instance?.RootUiContext?.MainMenuVM?.DarkHeresyPopUpVM?.Value;

        private static DarkHeresyPopUpView View() => LiveView.Find<DarkHeresyPopUpView>();

        /// <summary>Is the promo actually on screen? (The VM alone is not the answer — see the class note.)</summary>
        public static bool IsShowing()
        {
            if (Vm() == null) return false;
            var view = View();
            // Before the view has bound once there is nothing to read; the VM having just been created is
            // then the best signal we have, and the view's own Show() is one frame away.
            return view == null || view.m_IsShowed;
        }

        public override bool IsActive() => IsShowing();

        public override System.Collections.Generic.IEnumerable<ElementAction> GetActions()
        {
            yield return new ElementAction(ActionIds.Back, Message.Localized("ui", "action.close"), _ => Close());
        }


        public override void Build(GraphBuilder b)
        {
            var vm = Vm();
            if (vm == null) return;
            var texts = UIStrings.Instance.UIDarkHeresyPopUp;

            b.BeginStop("text").PushContext(
                GameText.Or(() => texts.Label, "darkheresy.title"), Loc.T("role.list"));
            b.AddItem(ControlId.Structural("dh:label"),
                GraphNodes.Text(() => GameText.Or(() => texts.Label, "darkheresy.title")));
            b.AddItem(ControlId.Structural("dh:sublabel"),
                GraphNodes.Text(() => texts.SubLabel));
            b.PopContext();

            // The two buttons, doing exactly what the view's click handlers do (hide, then open the store).
            b.BeginStop("actions").PushContext(Loc.T("hud.actions"), Loc.T("role.list"));
            b.AddItem(ControlId.Structural("dh:wishlist"), GraphNodes.Button(
                () => GameText.Or(() => texts.WishlistButtonLabel, "darkheresy.wishlist"),
                () => { Close(); Vm()?.OpenStoreToWishlist(); }));
            b.AddItem(ControlId.Structural("dh:close"), GraphNodes.Button(
                () => Loc.T("action.close"), Close));
            b.PopContext();
        }

        // The game's own dismissal: the view's Hide (fade out + deactivate). IsActive follows it.
        private static void Close() => View()?.Hide();
    }
}
