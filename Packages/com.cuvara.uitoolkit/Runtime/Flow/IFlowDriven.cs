namespace Cuvara.UIToolkit.Flow
{
    using System.Threading;
    using Cuvara.UIToolkit.Core;
    using Cysharp.Threading.Tasks;

    /// <summary>
    /// The members the navigator drives a presenter through. Internal on purpose.
    /// </summary>
    /// <remarks>
    /// <para><b>Why these are not on <see cref="IUIToolkitScreenPresenter"/>.</b> That interface
    /// is what a host sees; this is what the flow uses. Binding, activating and suspending are
    /// meaningful only in the order the navigator calls them, and a public
    /// <c>Bind()</c>/<c>Activate()</c> pair invites exactly the sequence that breaks the state
    /// machine — a screen bound twice, or activated while suspended. Keeping them internal makes
    /// that unwriteable from outside the package rather than merely discouraged.</para>
    ///
    /// <para><b>Why an interface rather than internal methods called directly.</b> The navigator
    /// is generic over <c>TPresenter</c> constrained to the public interface, so a host may
    /// implement that interface directly without deriving from the base class. Every call site
    /// then reads "drive it if it is flow-driven, otherwise leave it alone", and a hand-rolled
    /// presenter degrades to doing nothing rather than throwing a cast exception.</para>
    /// </remarks>
    internal interface IFlowDriven
    {
        void AttachView(IUIToolkitView view);

        void AttachNavigator(IScreenNavigator navigator);

        void SetOptions(ScreenOptions options);

        void SetState(ScreenLifecycleState state);

        UniTask Bind(ScreenSubscriptions subscriptions, CancellationToken cancellationToken);

        void Activate();

        void Deactivate();

        void Suspend();

        void Resume();

        bool Back();
    }

    /// <summary>A presenter that accepts a model of <typeparamref name="TModel"/>.</summary>
    /// <remarks>
    /// Separate from <see cref="IFlowDriven"/> because it is generic and the navigator's other
    /// plumbing is not. This is the one point where the navigator's non-generic entry type and
    /// the model-taking base class meet.
    /// </remarks>
    /// <typeparam name="TModel">The screen's input.</typeparam>
    internal interface IModelReceiver<in TModel>
    {
        void ReceiveModel(TModel model);
    }
}
