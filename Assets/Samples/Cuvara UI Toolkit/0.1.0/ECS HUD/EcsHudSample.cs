namespace Cuvara.UIToolkit.Samples.EcsHud
{
    using System;
    using Cuvara.UIToolkit.Ecs;
    using Cuvara.UIToolkit.View;
    using Unity.Entities;
    using UnityEngine;
    using UnityEngine.UIElements;

    // ---------------------------------------------------------------------------------
    // The layering this sample exists to demonstrate, top to bottom:
    //
    //     ECS world  ->  bridge (adapter)  ->  ViewModel  ->  Presenter  ->  View  ->  UXML
    //
    // Each arrow is one-way and each layer knows only the one below it. In particular the
    // bridge never sees a VisualElement and the Presenter never sees one either — that is
    // the project's UI architecture contract, and it is also the only arrangement that
    // survives the fact that VisualElement cannot be touched off the main thread.
    // ---------------------------------------------------------------------------------

    #region 1. Simulation — unmanaged, Burst-friendly, knows nothing about UI

    /// <summary>What the simulation writes. Unmanaged, as every IComponentData must be.</summary>
    public struct PlayerVitals : IComponentData
    {
        public int Health;
        public int MaxHealth;
    }

    #endregion

    #region 2. ViewModel — a plain value, and the only thing that crosses the boundary

    /// <summary>
    /// No <c>VisualElement</c>, no <c>VisualTreeAsset</c>, no <c>UIDocument</c>, no
    /// <c>GameObject</c>. A readonly struct makes that visible at a glance.
    /// </summary>
    public readonly struct VitalsViewModel
    {
        public readonly string Caption;
        public readonly float  Fraction;

        public VitalsViewModel(string caption, float fraction)
        {
            this.Caption  = caption;
            this.Fraction = fraction;
        }
    }

    #endregion

    #region 3. Adapter — main thread, converts, pushes on change, touches no UI

    /// <summary>
    /// Turns <see cref="PlayerVitals"/> into a <see cref="VitalsViewModel"/>.
    /// </summary>
    /// <remarks>
    /// Everything interesting is inherited: the change filter, the enabled-only-when-a-sink
    /// -is-registered behaviour, and the push. A host bridge is usually just a
    /// <see cref="Convert"/> override, which is the point.
    /// </remarks>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class VitalsBridge : EcsViewModelBridge<PlayerVitals, VitalsViewModel>
    {
        protected override VitalsViewModel Convert(in PlayerVitals component)
        {
            var fraction = component.MaxHealth <= 0 ? 0f : Mathf.Clamp01((float)component.Health / component.MaxHealth);

            return new($"{component.Health}/{component.MaxHealth}", fraction);
        }
    }

    #endregion

    #region 4. View — the ONLY layer that knows UI Toolkit exists

    /// <summary>What the Presenter is allowed to say to the View.</summary>
    /// <remarks>
    /// The contract requires a Presenter to be testable as a plain C# class with no scene,
    /// no <c>UIDocument</c> and no <c>VisualElement</c>. This interface is what makes that
    /// true: a test implements it with two fields.
    /// </remarks>
    public interface IVitalsView
    {
        void Render(string caption, float fraction);
    }

    /// <summary>
    /// Queries the UXML once, holds the elements, renders what it is told.
    /// </summary>
    /// <remarks>
    /// No business rules, no ECS query, no service call — the contract is explicit that a
    /// View is "the adapter between UI Toolkit and MVP" and nothing more. Element references
    /// are cached in the constructor rather than re-queried per render, which the contract's
    /// performance section also asks for.
    /// </remarks>
    public sealed class VitalsView : BaseUIToolkitView, IVitalsView
    {
        private readonly Label         caption;
        private readonly VisualElement fill;

        public VitalsView(VisualTreeAsset visualTreeAsset) : base(visualTreeAsset)
        {
            this.StretchToParent();

            this.caption = this.Root.Q<Label>("health-caption");
            this.fill    = this.Root.Q<VisualElement>("health-fill");
        }

        public void Render(string captionText, float fraction)
        {
            this.caption.text  = captionText;
            this.fill.style.width = Length.Percent(fraction * 100f);
        }
    }

    #endregion

    #region 5. Presenter — the sink. Knows an IView, never a VisualElement.

    /// <summary>
    /// Receives ViewModels from the bridge and tells the View what to display.
    /// </summary>
    /// <remarks>
    /// It implements <see cref="IViewModelSink{TViewModel}"/>, which is the package's entire
    /// coupling to MVP — the bridge knows it as "a sink", not as a Presenter. Note what it
    /// does NOT reference: <c>UIDocument</c>, <c>VisualElement</c>, <c>Button</c>,
    /// <c>Label</c>, UXML or USS. Those belong to the View boundary, and injecting a
    /// <c>UIDocument</c> here is called out by name in the architecture contract as
    /// something never to do.
    /// </remarks>
    public sealed class VitalsPresenter : IViewModelSink<VitalsViewModel>
    {
        private readonly IVitalsView view;

        public VitalsPresenter(IVitalsView view)
        {
            this.view = view ?? throw new ArgumentNullException(nameof(view));
        }

        public void Push(in VitalsViewModel viewModel)
        {
            this.view.Render(viewModel.Caption, viewModel.Fraction);
        }
    }

    #endregion

    #region 6. Bootstrap — the one GameObject a pure-ECS scene still needs

    /// <summary>
    /// Wires the five layers together and owns their lifetime.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a MonoBehaviour is unavoidable.</b> <see cref="UIDocument"/> is a
    /// MonoBehaviour and there is no ECS equivalent, so even a scene whose simulation is
    /// entirely unmanaged needs one GameObject to host the panel. That is a fact about UI
    /// Toolkit, not a compromise in this sample.</para>
    ///
    /// <para>In a real project this wiring is a VContainer child scope rather than a
    /// <c>Start</c> method, and <c>scope.Dispose()</c> replaces <c>OnDestroy</c> — see
    /// <c>EcsSinkRegistration</c>'s remarks. It is written out longhand here so the
    /// dependency order is visible without knowing a container.</para>
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    public sealed class EcsHudBootstrap : MonoBehaviour
    {
        [SerializeField] private UIDocument      uiDocument;
        [SerializeField] private VisualTreeAsset hudAsset;

        private VitalsView                                          view;
        private EcsSinkRegistration<PlayerVitals, VitalsViewModel>   registration;

        private void Start()
        {
            if (this.uiDocument == null) this.uiDocument = this.GetComponent<UIDocument>();

            this.view = new(this.hudAsset);
            this.uiDocument.rootVisualElement.Add(this.view.Root);
            this.view.Show();

            var presenter = new VitalsPresenter(this.view);
            var bridge    = World.DefaultGameObjectInjectionWorld.GetOrCreateSystemManaged<VitalsBridge>();

            // Registering is what enables the bridge; before this it is disabled and costs
            // the world nothing.
            this.registration = EcsSinkRegistration.Bind(bridge, presenter);
        }

        private void OnDestroy()
        {
            // Unregister BEFORE dropping the view. A sink left registered keeps the
            // Presenter alive, which keeps the View alive, which keeps the visual tree
            // alive — the standard UI leak, and a silent one.
            this.registration?.Dispose();
            this.view?.DestroySelf();
        }
    }

    #endregion
}
