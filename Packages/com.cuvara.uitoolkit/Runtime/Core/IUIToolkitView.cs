namespace Cuvara.UIToolkit.Core
{
    using System;
    using Cysharp.Threading.Tasks;
    using UnityEngine.UIElements;

    /// <summary>
    /// A screen-sized piece of UI with a lifecycle: it can be opened, closed, hidden,
    /// shown, and destroyed, and it can be parented into a layer.
    /// </summary>
    /// <remarks>
    /// <para>This is the contract the package OWNS, and owning it is the whole point. It
    /// was extracted from a host framework where the equivalent interface required a
    /// <c>RectTransform</c> and an <c>IsReadyToUse</c> flag — both of them uGUI facts that
    /// a <see cref="VisualElement"/> cannot satisfy, and both of them reasons the package
    /// could not compile without the host present.</para>
    ///
    /// <para>What is deliberately absent:</para>
    /// <list type="bullet">
    /// <item>No <c>Transform</c> of any kind. A <see cref="VisualElement"/> has no
    /// GameObject behind it. Reparenting goes through <see cref="ViewSurface"/>.</item>
    /// <item>No "is the view ready yet" flag. A UI Toolkit view's hierarchy is built by a
    /// synchronous <c>CloneTree</c> before its constructor returns, so it is usable the
    /// instant it exists. The uGUI world needs such a flag because of the frame gap
    /// between <c>Instantiate</c> and <c>Awake</c>; there is no gap here.</item>
    /// <item>No model, no presenter, no signal bus. Who owns a view and what it binds to
    /// is the host's business. A host with presenters writes its presenter base against
    /// this interface.</item>
    /// </list>
    ///
    /// <para>The three events are plain C# events rather than a pub/sub bus, for the same
    /// reason: a bus would be a dependency, and a host that has one can forward these to
    /// it in one line.</para>
    /// </remarks>
    public interface IUIToolkitView
    {
        /// <summary>Raised after <see cref="Open"/> has finished, including its transition.</summary>
        event Action ViewDidOpen;

        /// <summary>Raised after <see cref="Close"/> has finished, including its transition.</summary>
        event Action ViewDidClose;

        /// <summary>Raised after <see cref="DestroySelf"/> has detached the view.</summary>
        event Action ViewDidDestroy;

        /// <summary>The root element of this view. Never null after construction.</summary>
        VisualElement Root { get; }

        /// <summary>Where this view sits in the tree, as something a layer can adopt.</summary>
        IViewSurface ViewSurface { get; }

        /// <summary>Makes the view visible and interactive, running the intro transition.</summary>
        UniTask Open();

        /// <summary>Runs the outro transition, then makes the view invisible and inert.</summary>
        UniTask Close();

        /// <summary>Makes the view invisible and inert immediately, with no transition.</summary>
        void Hide();

        /// <summary>Makes the view visible and interactive immediately, with no transition.</summary>
        void Show();

        /// <summary>Detaches the view from its layer and releases what it registered.</summary>
        void DestroySelf();
    }
}
