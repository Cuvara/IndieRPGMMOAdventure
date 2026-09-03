namespace Cuvara.UIToolkit.Samples.EcsHud
{
    using System;
    using Cuvara.UIToolkit.Ecs;
    using Cuvara.UIToolkit.View;
    using Cuvara.UIToolkit.ViewModel;
    using Unity.Entities;
    using Unity.Properties;
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
    //
    // This is also the package's REFERENCE HYBRID SCREEN: the values that change during
    // the screen's life flow through runtime data binding (a BindableViewModel the View
    // binds with SetBinding), while the framework around it — lifecycle, sinks, and any
    // commands — stays MVP. The binding is a View-internal implementation detail: nothing
    // above the View knows or cares that it exists. See
    // Documentation~/HYBRID-DATA-BINDING.md.
    // ---------------------------------------------------------------------------------

    #region 1. Simulation — unmanaged, Burst-friendly, knows nothing about UI

    /// <summary>What the simulation writes. Unmanaged, as every IComponentData must be.</summary>
    public struct PlayerVitals : IComponentData
    {
        public int Health;
        public int MaxHealth;
    }

    #endregion

    #region 2. Boundary ViewModel — a plain value, the only thing that crosses from ECS

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
    /// <see cref="Convert"/> override, which is the point. Note that the hybrid retrofit
    /// changed NOTHING here — the adapter neither knows nor cares how the View renders.
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

    #region 4. Bindable ViewModel — what the View's bindings observe

    /// <summary>
    /// The screen's live state, as notifying properties the View binds to.
    /// </summary>
    /// <remarks>
    /// <para>Still plain C#: no <c>VisualElement</c>, no <c>UIDocument</c>, no panel — the
    /// UIElements types it touches through <see cref="BindableViewModel"/> are an interface
    /// and an event-args struct. It is testable with NUnit alone, exactly like the
    /// readonly-struct ViewModel above it.</para>
    ///
    /// <para>Every setter routes through <c>Set</c>, so a write of an unchanged value
    /// raises nothing and the binding system re-evaluates nothing. That notify-on-change
    /// discipline is MANDATORY in this package: a data source that does not notify is
    /// version-polled by the binding system on every UI update, which is the per-frame
    /// work the contract forbids. <c>[CreateProperty]</c> is what makes each property
    /// visible to the binding system's property bags; <c>nameof</c> on the View side is
    /// what makes a rename a compile error.</para>
    /// </remarks>
    public sealed class VitalsHudViewModel : BindableViewModel
    {
        private string caption = string.Empty;
        private float  fraction;

        /// <summary>The "50/100" text over the bar.</summary>
        [CreateProperty]
        public string Caption
        {
            get => this.caption;
            set => this.Set(ref this.caption, value);
        }

        /// <summary>Health as 0..1. A plain float — converting it to a width is the View's job.</summary>
        [CreateProperty]
        public float Fraction
        {
            get => this.fraction;
            set => this.Set(ref this.fraction, value);
        }
    }

    #endregion

    #region 5. View — the ONLY layer that knows UI Toolkit exists

    /// <summary>What the Presenter is allowed to say to the View.</summary>
    /// <remarks>
    /// The contract requires a Presenter to be testable as a plain C# class with no scene,
    /// no <c>UIDocument</c> and no <c>VisualElement</c>. This interface is what makes that
    /// true — and it is also what makes the data binding a View-internal detail: the
    /// Presenter hands over a ViewModel once and never learns whether the View renders it
    /// imperatively or through <c>SetBinding</c>.
    /// </remarks>
    public interface IVitalsView
    {
        /// <summary>Makes <paramref name="viewModel"/> the state this view displays, now and as it changes.</summary>
        void Bind(VitalsHudViewModel viewModel);
    }

    /// <summary>
    /// Wires the UXML to the ViewModel with runtime data binding, once, in <see cref="Bind"/>.
    /// </summary>
    /// <remarks>
    /// <para>The other half of this <c>partial</c> is the generated
    /// <c>Generated/VitalsView.uxml.g.cs</c>: one typed property per named element in
    /// <c>VitalsView.uxml</c>, resolved through <c>Require&lt;T&gt;</c> by
    /// <c>AssignQueries</c> — so a UXML rename fails loudly at construction, before any
    /// binding is even created.</para>
    ///
    /// <para>There is no <c>Render</c> method any more, and that is the retrofit: after
    /// <see cref="Bind"/>, a property write on the ViewModel reaches the elements through
    /// the binding system, on notification, with no call into this class. All bindings are
    /// <see cref="BindingMode.ToTarget"/> — data flows toward the UI only. Had this HUD
    /// any buttons, their clicks would stay on <c>ScreenSubscriptions</c>; binding is for
    /// values, never for commands.</para>
    ///
    /// <para>The fraction-to-width conversion lives here, as a converter on the binding:
    /// the ViewModel exposes a plain 0..1 float and <c>StyleLength</c> — a UI Toolkit
    /// type — never leaks above the View.</para>
    /// </remarks>
    public sealed partial class VitalsView : BaseUIToolkitView, IVitalsView
    {
        public VitalsView(VisualTreeAsset visualTreeAsset) : base(visualTreeAsset)
        {
            this.StretchToParent();
            this.AssignQueries(this.Root);
        }

        public void Bind(VitalsHudViewModel viewModel)
        {
            if (viewModel == null) throw new ArgumentNullException(nameof(viewModel));

            this.Root.dataSource = viewModel;

            this.HealthCaption.SetBinding(nameof(Label.text), new DataBinding
            {
                dataSourcePath = new PropertyPath(nameof(VitalsHudViewModel.Caption)),
                bindingMode    = BindingMode.ToTarget,
            });

            var fillBinding = new DataBinding
            {
                dataSourcePath = new PropertyPath(nameof(VitalsHudViewModel.Fraction)),
                bindingMode    = BindingMode.ToTarget,
            };
            fillBinding.sourceToUiConverters.AddConverter((ref float fraction) => new StyleLength(Length.Percent(fraction * 100f)));

            this.HealthFill.SetBinding("style.width", fillBinding);
        }
    }

    #endregion

    #region 6. Presenter — the sink. Knows an IView and a ViewModel, never a VisualElement.

    /// <summary>
    /// Receives boundary ViewModels from the bridge and writes them onto the bindable one.
    /// </summary>
    /// <remarks>
    /// It implements <see cref="IViewModelSink{TViewModel}"/>, which is the package's entire
    /// coupling to MVP — the bridge knows it as "a sink", not as a Presenter. Note what it
    /// does NOT reference: <c>UIDocument</c>, <c>VisualElement</c>, <c>Button</c>,
    /// <c>Label</c>, <c>DataBinding</c>, UXML or USS. It sets two plain properties; the
    /// <c>Set</c> guard in <see cref="BindableViewModel"/> means an identical push (the
    /// bridge's catch-up pass, say) raises nothing and costs the UI nothing.
    /// </remarks>
    public sealed class VitalsPresenter : IViewModelSink<VitalsViewModel>
    {
        private readonly VitalsHudViewModel viewModel;

        public VitalsPresenter(IVitalsView view, VitalsHudViewModel viewModel)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));

            this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            view.Bind(this.viewModel);
        }

        public void Push(in VitalsViewModel boundary)
        {
            this.viewModel.Caption  = boundary.Caption;
            this.viewModel.Fraction = boundary.Fraction;
        }
    }

    #endregion

    #region 7. Bootstrap — the one GameObject a pure-ECS scene still needs

    /// <summary>
    /// Wires the layers together and owns their lifetime.
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

            var presenter = new VitalsPresenter(this.view, new VitalsHudViewModel());
            var bridge    = World.DefaultGameObjectInjectionWorld.GetOrCreateSystemManaged<VitalsBridge>();

            // Registering is what enables the bridge; before this it is disabled and costs
            // the world nothing.
            this.registration = EcsSinkRegistration.Bind(bridge, presenter);
        }

        private void OnDestroy()
        {
            // Unregister BEFORE dropping the view. A sink left registered keeps the
            // Presenter alive, which keeps the ViewModel alive, which — through the
            // panel's dataSource — keeps the visual tree alive: the standard UI leak,
            // and a silent one.
            this.registration?.Dispose();
            this.view?.DestroySelf();
        }
    }

    #endregion
}
