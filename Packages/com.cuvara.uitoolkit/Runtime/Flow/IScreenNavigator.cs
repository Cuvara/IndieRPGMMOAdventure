namespace Cuvara.UIToolkit.Flow
{
    using System;
    using Cysharp.Threading.Tasks;

    /// <summary>What Back does when there is nothing left to pop.</summary>
    /// <remarks>
    /// Explicit rather than emergent, because every alternative to stating it is a way of
    /// stranding the user. This is the single decision that determines whether the Android
    /// system Back button still exits the app.
    /// </remarks>
    public enum RootBackPolicy
    {
        /// <summary>
        /// Report NOT handled, so the press keeps propagating and the platform's own Back — on
        /// Android, exiting the app — still runs. The default.
        /// </summary>
        /// <remarks>
        /// It is the default because it is the platform behaviour a user expects and the only one
        /// of the three that cannot leave them stuck with a button that does nothing.
        /// </remarks>
        NotHandled = 0,

        /// <summary>Consume the press and do nothing. For a screen the player must not leave — a mid-match HUD.</summary>
        Consume = 1,

        /// <summary>Consume the press and raise <see cref="IScreenNavigator.RootBackRequested"/>, so the app can show a quit dialog.</summary>
        Raise = 2,
    }

    /// <summary>
    /// The one navigation abstraction. Opens, closes and orders screens.
    /// </summary>
    /// <remarks>
    /// <para>Named <c>IScreenNavigator</c> rather than the obvious alternative because the
    /// standalone gate bans that identifier — and rightly, since a type with that name here would
    /// be confusable with the frozen framework's.</para>
    ///
    /// <para><b>Every mutating operation is serialised.</b> Two rapid taps on two different
    /// buttons queue rather than interleave. That will occasionally read as input lag and it is
    /// still the right trade: concurrent stack mutation produces orderings nobody can reason
    /// about, and "which screen is on top" stops having an answer. <see cref="IsBusy"/> exposes
    /// the state so a caller can choose to skip rather than queue.</para>
    ///
    /// <para><b>Plain C# events, not a signal bus</b>, matching the rest of this package and for
    /// the same reason: a bus would be a dependency, and a host that has one forwards these in
    /// one line.</para>
    /// </remarks>
    public interface IScreenNavigator
    {
        /// <summary>How many screens are on the stack.</summary>
        int Depth { get; }

        /// <summary>The screen on top, or null when the stack is empty.</summary>
        IUIToolkitScreenPresenter Top { get; }

        /// <summary>Whether an operation is in flight. A push or pop started now would queue behind it.</summary>
        bool IsBusy { get; }

        /// <summary>What Back does at the root. See <see cref="RootBackPolicy"/>.</summary>
        RootBackPolicy RootBackPolicy { get; set; }

        /// <summary>Raised when Back is pressed at the root and the policy is <see cref="RootBackPolicy.Raise"/>.</summary>
        event Action RootBackRequested;

        /// <summary>Raised after a screen becomes the top and is interactive.</summary>
        event Action<IUIToolkitScreenPresenter> ScreenActivated;

        /// <summary>Raised after a screen stops being the top.</summary>
        event Action<IUIToolkitScreenPresenter> ScreenDeactivated;

        /// <summary>Opens a screen on top of the stack, suspending what it covers.</summary>
        UniTask<TPresenter> PushAsync<TPresenter>() where TPresenter : class, IUIToolkitScreenPresenter;

        /// <summary>Opens a screen with a model.</summary>
        UniTask<TPresenter> PushAsync<TPresenter, TModel>(TModel model) where TPresenter : class, IUIToolkitScreenPresenter<TModel>;

        /// <summary>Closes the top screen, then opens this one. What is below is never resumed, so it does not flash.</summary>
        UniTask<TPresenter> ReplaceAsync<TPresenter>() where TPresenter : class, IUIToolkitScreenPresenter;

        /// <inheritdoc cref="ReplaceAsync{TPresenter}"/>
        UniTask<TPresenter> ReplaceAsync<TPresenter, TModel>(TModel model) where TPresenter : class, IUIToolkitScreenPresenter<TModel>;

        /// <summary>Closes and disposes the top screen, resuming the one beneath.</summary>
        UniTask PopAsync();

        /// <summary>Pops everything above the bottom screen.</summary>
        UniTask PopToRootAsync();

        /// <summary>Pops everything. <see cref="Depth"/> becomes zero.</summary>
        UniTask PopAllAsync();

        /// <summary>Where a given screen is in its lifecycle, or null if it is not on the stack.</summary>
        ScreenLifecycleState? StateOf<TPresenter>() where TPresenter : class, IUIToolkitScreenPresenter;

        /// <summary>
        /// Runs one Back press. Returns true if the navigator acted on it.
        /// </summary>
        /// <remarks>
        /// <para><b>A method returning bool, not an event subscription</b> — the navigator does not
        /// know what an input source is, and does not reference one. A host wires whatever raises
        /// Back to this, which keeps the input backend a host choice and makes the whole policy
        /// testable with no panel, no focus controller and no input system.</para>
        ///
        /// <para>The return value is the contract: <c>false</c> means "I did not act, keep
        /// propagating", which is what allows the platform's own Back to run.</para>
        /// </remarks>
        bool HandleBack();
    }
}
