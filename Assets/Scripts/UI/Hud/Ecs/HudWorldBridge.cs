namespace Scripts.UI.Hud.Ecs
{
    using Cuvara.UIToolkit.Ecs;
    using Scripts.UI.Hud;
    using Unity.Entities;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    /// The HUD's composition root: hosts the <see cref="UIDocument"/>, builds the
    /// View → ViewModel → Presenter chain, installs the two HUD systems into the same
    /// world <c>DotsWorldBridge</c> uses, and joins the two lifetimes with an
    /// <see cref="EcsSinkRegistration{TComponent,TViewModel}"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a UIDocument host and not a uitoolkit screen flow.</b> The project
    /// registers no <c>ScreenManager</c>/screen-flow anywhere — <c>GameLifetimeScope</c>
    /// wires networking, Nakama and dots only — so there is no screen host to enroll a
    /// HUD presenter into. This component is the sample's <c>EcsHudBootstrap</c> shape,
    /// productionized: when the project stands up the uitoolkit screen flow, the
    /// Presenter/sink pair moves into a screen's child scope (see
    /// <c>EcsSinkRegistration</c>'s remarks for the registration) and this component
    /// reduces to the UIDocument. Documented in <c>docs/HUD-BRIDGE.md</c>.</para>
    ///
    /// <para><b>Why not VContainer-injected.</b> It has no managed dependency to inject —
    /// the world is a static, the asset and document are serialized references. The moment
    /// it grows one (a connection-state feed off <c>NetworkClient</c>, say), it takes a
    /// <c>[Inject] Construct</c> and a build-callback in <c>MainSceneScope</c> exactly the
    /// way <c>DotsWorldBridge</c> does.</para>
    ///
    /// <para><b>Install is lazy, teardown is ordered.</b> <c>Update</c> polls only until
    /// <c>World.DefaultGameObjectInjectionWorld</c> exists, then installs once and disables
    /// itself — after that this component costs nothing per frame; the data path is the
    /// change-driven bridge, never a poll. Teardown runs in reverse: sink unregistered
    /// first (a sink left registered keeps Presenter → ViewModel → visual tree alive — the
    /// standard silent UI leak), then the systems, then the view.</para>
    /// </remarks>
    [RequireComponent(typeof(UIDocument))]
    public sealed class HudWorldBridge : MonoBehaviour
    {
        [Tooltip("The panel the HUD renders into. Defaults to the UIDocument on this GameObject.")]
        [SerializeField] private UIDocument uiDocument;

        [Tooltip("HudView.uxml — the enrolled HUD layout this view was generated from.")]
        [SerializeField] private VisualTreeAsset hudAsset;

        private World world;
        private HudView view;
        private EcsSinkRegistration<HudState, HudSnapshot> registration;

        private void Update()
        {
            this.world = World.DefaultGameObjectInjectionWorld;
            if (this.world == null)
            {
                // Not an error yet: the default world may bootstrap after this scene
                // object. Keep polling; DotsWorldBridge does the same.
                return;
            }

            if (this.hudAsset == null)
            {
                Debug.LogWarning("[HudWorldBridge] no HUD VisualTreeAsset assigned — HUD disabled.");
                this.enabled = false;
                return;
            }

            this.Install();

            // Installed. From here the bridge system is the data path; this component has
            // no per-frame job and switches itself off (OnDestroy still runs).
            this.enabled = false;
        }

        private void Install()
        {
            if (this.uiDocument == null)
            {
                this.uiDocument = this.GetComponent<UIDocument>();
            }

            this.view = new HudView(this.hudAsset);
            this.uiDocument.rootVisualElement.Add(this.view.Root);
            this.view.Show();

            var presenter = new HudPresenter(this.view, new HudViewModel());
            var bridge = HudEcsBootstrap.Install(this.world);

            // Registering is what enables the bridge — before this it is disabled and
            // costs the world nothing. It also arms the one-shot catch-up pass, so the
            // HUD shows the current state even if nothing changes for a while.
            this.registration = EcsSinkRegistration.Bind(bridge, presenter);
        }

        private void OnDestroy()
        {
            // Sink first, systems second, view last — see the class remarks.
            this.registration?.Dispose();
            this.registration = null;

            HudEcsBootstrap.Uninstall(this.world);

            this.view?.DestroySelf();
            this.view = null;
        }
    }
}
