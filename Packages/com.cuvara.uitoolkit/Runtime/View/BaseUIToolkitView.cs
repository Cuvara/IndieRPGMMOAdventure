namespace Cuvara.UIToolkit.View
{
    using System;
    using Cuvara.UIToolkit.Core;
    using Cysharp.Threading.Tasks;
    using UnityEngine.UIElements;

    /// <summary>
    /// The base for a UI Toolkit screen: a plain C# class, no MonoBehaviour, no
    /// <c>[SerializeField]</c>, no GameObject.
    /// </summary>
    /// <remarks>
    /// <para>Subclass it, query your elements in the constructor, and hold them. The
    /// <c>(VisualTreeAsset)</c> constructor is the one <see cref="UIToolkitViewFactory"/>
    /// calls, so a subclass that changes its shape becomes unconstructable at runtime.</para>
    ///
    /// <para><b>Show/hide is opacity plus picking, not just opacity.</b> <c>style.opacity</c>
    /// alone would leave an invisible view still swallowing every click aimed at what is
    /// behind it, so <c>pickingMode</c> moves with it — <c>Position</c> when interactive,
    /// <c>Ignore</c> when not. This is the exact analogue of a uGUI <c>CanvasGroup</c>'s
    /// <c>alpha</c> plus <c>blocksRaycasts</c> pair.</para>
    ///
    /// <para><b>It starts invisible.</b> The constructor sets alpha 0, so a view is created
    /// unseen and <see cref="Open"/> is what reveals it. Building a view straight into a
    /// visible layer would otherwise flash one un-transitioned frame.</para>
    ///
    /// <para><b>Transitions are hooks, not a framework.</b>
    /// <see cref="PlayIntroAnim"/> and <see cref="PlayOutroAnim"/> default to completing
    /// immediately. Override them with USS transitions, <c>schedule</c>, or a tween library
    /// of your choosing — the package deliberately ships none.</para>
    /// </remarks>
    public abstract class BaseUIToolkitView : IUIToolkitView
    {
        public event Action ViewDidClose;
        public event Action ViewDidOpen;
        public event Action ViewDidDestroy;

        /// <summary>The root element of this view. Never null after construction.</summary>
        public VisualElement Root { get; }

        private IViewSurface viewSurface;

        // Cached: a screen flow reparents on every open and close, and allocating a wrapper
        // per call would put garbage on a path that runs during transitions.
        public IViewSurface ViewSurface => this.viewSurface ??= new VisualElementViewSurface(this.Root);

        /// <summary>Builds a view around an already-constructed root element.</summary>
        protected BaseUIToolkitView(VisualElement root)
        {
            this.Root = root ?? throw new ArgumentNullException(nameof(root));

            this.UpdateAlpha(0);
        }

        /// <summary>Builds a view by cloning <paramref name="visualTreeAsset"/>.</summary>
        /// <remarks>
        /// <c>CloneTree</c> is synchronous, which is why a UI Toolkit view needs no
        /// "is it ready yet" flag: by the time this constructor returns, the hierarchy
        /// exists and every <c>Q&lt;&gt;</c> in a subclass constructor resolves.
        /// </remarks>
        protected BaseUIToolkitView(VisualTreeAsset visualTreeAsset)
            : this(CloneRoot(visualTreeAsset))
        {
        }

        private static VisualElement CloneRoot(VisualTreeAsset visualTreeAsset)
        {
            if (visualTreeAsset == null) throw new ArgumentNullException(nameof(visualTreeAsset));
            return visualTreeAsset.CloneTree();
        }

        public virtual async UniTask Open()
        {
            this.UpdateAlpha(1f);
            await this.PlayIntroAnim();
            this.ViewDidOpen?.Invoke();
        }

        public virtual async UniTask Close()
        {
            await this.PlayOutroAnim();
            this.UpdateAlpha(0);
            this.ViewDidClose?.Invoke();
        }

        public void Hide() { this.UpdateAlpha(0); }

        public void Show() { this.UpdateAlpha(1); }

        public void DestroySelf()
        {
            // A VisualElement is not a Unity object; "destroy" means detaching it from
            // whatever layer holds it and dropping the last reference to it.
            this.Root.RemoveFromHierarchy();
            this.OnDestroySelf();
            this.ViewDidDestroy?.Invoke();
        }

        /// <summary>Override to release anything the view registered (callbacks, schedulers).</summary>
        protected virtual void OnDestroySelf()
        {
        }

        /// <summary>Intro transition. Defaults to none.</summary>
        protected virtual UniTask PlayIntroAnim() { return UniTask.CompletedTask; }

        /// <summary>Outro transition. Defaults to none.</summary>
        protected virtual UniTask PlayOutroAnim() { return UniTask.CompletedTask; }

        /// <summary>
        /// Stretches the view's root to fill whatever layer it is parented into.
        /// </summary>
        /// <remarks>
        /// <c>CloneTree</c> returns a <c>TemplateContainer</c>, which is a plain flex item
        /// with no size of its own. A screen parented into a layer is almost always meant
        /// to cover it, so this is offered here rather than left for every subclass to
        /// rediscover — call it from your constructor. It is not automatic, because a view
        /// that is deliberately smaller than its layer is a legitimate thing to want.
        /// </remarks>
        protected void StretchToParent()
        {
            this.Root.style.position = Position.Absolute;
            this.Root.style.left     = 0;
            this.Root.style.top      = 0;
            this.Root.style.right    = 0;
            this.Root.style.bottom   = 0;
        }

        protected void UpdateAlpha(float value)
        {
            this.Root.style.opacity = value;
            this.Root.pickingMode   = value >= 1 ? PickingMode.Position : PickingMode.Ignore;
        }
    }
}
