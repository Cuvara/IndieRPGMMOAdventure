#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER && CUVARA_NETCODE && CUVARA_SHARED_GAMELOGIC
namespace Scripts.DI.Dots
{
    using System;
    using Cuvara.DOTS.Netcode.Prediction;
    using Cuvara.DOTS.Provisioning;
    using Cuvara.DOTS.Views;
    using Cuvara.Netcode.Client;
    using Cuvara.Netcode.Prediction;
    using Cuvara.Netcode.Protocol.Messages;
    using Cuvara.Netcode.View;
    using Scripts.Gameplay.Content;
    using Scripts.Gameplay.Maps;
    using Scripts.Gameplay.Presentation;
    using Shared.GameLogic.Components;
    using UnityEngine;
    using VContainer;

    /// <summary>
    /// The per-session half of the DOTS wiring: hangs the <c>com.cuvara.dots</c> netcode adapter,
    /// prediction driver and session modules off the same <c>NetworkClient</c> the game already
    /// registers, and ticks the binder that feeds it. Place one in the gameplay scene;
    /// <c>MainSceneScope</c> injects it the way <c>GameLifetimeScope</c> injects
    /// <c>NetworkBootstrap</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This component is the thin half.</b> It keeps the serialized fields and method
    /// injection. Everything installed into the ECS world, the session reset and the per-frame
    /// feed live in <see cref="EcsSessionInstallation"/>, which <c>Update</c> drives and
    /// <c>OnDestroy</c> disposes; input sampling, sending, aim and cast live in
    /// <see cref="InputEgress"/>. The rest of these remarks describe the behaviour of the trio.
    /// </para>
    /// <para>
    /// The root scope (<c>RegisterDots</c>) owns what outlives a scene: registry, the asset
    /// provider and its pools, provisioner, predictor, the view bootstrap. This component owns
    /// what belongs to one session in one scene: the catalog built from the
    /// <see cref="DotsViewLibraryAsset"/>, the archetype resolver, the adapter, the netcode and
    /// prediction bootstraps, the session-scoped modules (camera follow, minimap), the binder
    /// tick, and the reconnect hook. Teardown mirrors install in reverse — prediction, then the
    /// adapter and its mirrors, then <c>DotsModules.UninstallScope(Session)</c>, then the catalog
    /// — and finally releases the catalog's asset keys through the provider, which drops each
    /// prefab's handle once the pool has recycled its last instance. The view bootstrap is
    /// deliberately NOT uninstalled here: it is root-scoped and other scenes' views stand on it.
    /// </para>
    /// <para>
    /// <b>Install is validated, then asynchronous.</b> The library goes through
    /// <c>ViewConfigCatalog.TryBuild</c> with the provider's own <c>prefabExists</c> and the
    /// server-kind mapping check; an invalid library logs every issue and disables this
    /// component rather than presenting a partial world. The catalog's keys are then prewarmed
    /// through the provider — an Addressables load in production — and the binder starts
    /// ticking only once that completes, so the first spawn never hits an unloaded key.
    /// </para>
    /// <para>
    /// <b>Every new session is a generation boundary.</b> The first frame that sees a session
    /// object it has not begun (a first connect, a reconnect, a manual retry, a dungeon join, a
    /// map transfer - <c>NetworkClient.Reconnected</c> reaches the same path and is idempotent
    /// per session object) the view begins a new generation (queued old-session commands are dropped, old mirrors torn down
    /// before the new session's first snapshot is applied), the predictor is reset so its input
    /// backlog does not replay against a server that never saw it, and the camera's smoothing is
    /// reset so it snaps to the re-placed avatar instead of sweeping across the map.
    /// </para>
    /// <para>
    /// <b>One input owner.</b> When <see cref="driveInput"/> is on, <see cref="InputEgress"/> samples input,
    /// sends it, and records the same tick on the predictor — recorded and sent must be the same
    /// stream or replay diverges by construction. <c>NetworkBootstrap</c>'s
    /// <c>SendSyntheticInput</c> must then be OFF in the scene's config.
    /// </para>
    /// <para>
    /// <b>Protocol 3 (Core v3, ADR-28/29).</b> After every join and reconnect the predictor is
    /// switched to the server's movement model (<c>UseServerProtocol</c>) and given the map's
    /// geometry (<see cref="MapGeometrySource"/>, flat when the map has no file); a
    /// <see cref="ProjectilePredictor"/> is created for a protocol 3 server. Input carries jump,
    /// the render time, and -- on the cast key -- the content's projectile ability with a ground
    /// aim and a <c>spawn_seq</c>. Height reaches the mirrors through
    /// <see cref="EntityElevationSystem"/>, which also draws projectiles extrapolated; the
    /// owner's predicted projectiles are <see cref="PredictedProjectileViews"/> until handover.
    /// </para>
    /// <para>
    /// <b>The binder uses the no-predictor overload on purpose.</b>
    /// <c>WorldViewBinder(view, predictor)</c> hands <c>SetState</c> the predicted position, and
    /// <c>DotsEntityView</c> stores what it receives as the authoritative
    /// <c>ReconciliationAnchor</c> — prediction reconciling against its own output. With the
    /// adapter, prediction lives ECS-side in <c>LocalPredictionSystem</c>. Likewise nothing here
    /// may call <c>SetStateAtTick</c> for an entity the binder ticks — that buffers the already
    /// interpolated output for a second interpolation pass (double <c>TargetDelay</c>).
    /// </para>
    /// </remarks>
    public sealed class DotsWorldBridge : MonoBehaviour
    {
        [Tooltip("Sample input axes, send them, and record them on the predictor. Turn OFF if " +
                 "something else owns input — and never leave NetworkBootstrap's synthetic input " +
                 "on at the same time.")]
        [SerializeField] private bool driveInput = true;

        [Tooltip("View library for this scene. Empty uses the one the root scope loaded from " +
                 "Resources/DotsViews/DotsViewLibrary. Ignored in primitive-provider mode.")]
        [SerializeField] private DotsViewLibraryAsset viewLibrary;

        [Header("Session modules")]
        [Tooltip("Install CameraFollow (session-scoped) and target the local player's mirror.")]
        [SerializeField] private bool enableCameraFollow = true;

        [Tooltip("Camera to follow with. Empty uses Camera.main at install.")]
        [SerializeField] private Camera followCamera;

        [SerializeField] private Vector3 cameraOffset = new Vector3(0f, 10f, -8f);

        [Tooltip("Install the minimap data module (session-scoped) and categorise mirrors for it.")]
        [SerializeField] private bool enableMinimap;

        private NetworkClient client;
        private LocalMovePredictor predictor;
        private GameContentService content;
        private IViewAssetProvider viewAssetProvider;
        private DotsViewLibraryReference libraryReference;
        private IEcsWorldProvider worldProvider;

        private EcsSessionInstallation installation;

        /// <summary>
        /// The installation's binder, mirrored here so the testlab's <c>BinderProbe</c>, which finds
        /// it by reflection on this type, keeps reading the live binder.
        /// </summary>
        private WorldViewBinder binder;

        private Action beforeIngress;

        /// <summary>Where the controls come from; injected by <see cref="Construct"/>.</summary>
        private IGameplayInputSource input;

        private InputEgress inputEgress;

        /// <summary>True once the catalog is installed and every key is warm; the binder ticks only then.</summary>
        public bool IsReady => this.installation != null && this.installation.IsReady;

        /// <summary>Reconnects this bridge has turned into view generations. Diagnostics.</summary>
        public int ReconnectsHandled => this.installation?.ReconnectsHandled ?? 0;

        /// <summary>The local projectile predictor of the current session; null before a protocol 3 join.</summary>
        public ProjectilePredictor ProjectilePredictor => this.installation?.ProjectilePredictor;

        /// <summary>Called by the scene scope's build callback; absent a container this component stays inert.</summary>
        [Inject]
        public void Construct(
            NetworkClient networkClient,
            LocalMovePredictor movePredictor,
            IViewAssetProvider assetProvider,
            DotsViewLibraryReference viewLibraryReference,
            GameContentService gameContent,
            IGameplayInputSource gameplayInput,
            IEcsWorldProvider ecsWorldProvider)
        {
            this.client = networkClient;
            this.predictor = movePredictor;
            this.viewAssetProvider = assetProvider;
            this.libraryReference = viewLibraryReference;
            this.content = gameContent;
            this.input = gameplayInput;
            this.worldProvider = ecsWorldProvider;
        }

        private void Update()
        {
            if (this.client == null)
            {
                return;
            }

            if (this.installation == null)
            {
                this.installation = new EcsSessionInstallation(
                    this.worldProvider,
                    this.client,
                    this.predictor,
                    this.viewAssetProvider,
                    this.libraryReference,
                    new EcsSessionInstallation.Settings
                    {
                        ViewLibrary = this.viewLibrary,
                        EnableCameraFollow = this.enableCameraFollow,
                        FollowCamera = this.followCamera,
                        CameraOffset = this.cameraOffset,
                        EnableMinimap = this.enableMinimap,
                        ViewRoot = this.transform,
                    });
                this.inputEgress = new InputEgress(
                    new InstallationInputPort(this.client, this.content, this.installation),
                    this.predictor,
                    this.input,
                    this.followCamera);
                this.beforeIngress = this.SendInput;
            }

            if (!this.installation.IsInstalled)
            {
                if (!this.installation.TryInstall())
                {
                    this.enabled = false;
                    return;
                }

                this.binder = this.installation.Binder;
            }

            if (!this.installation.IsReady)
            {
                var ready = this.installation.TryFinishPrewarm(out var disable);
                if (disable)
                {
                    this.enabled = false;
                }

                if (!ready)
                {
                    return;
                }
            }

            // The gate runs the session reset before the frame's work (input send, then the binder
            // feed), so the first frame of a new session never meets the old session's binder,
            // predictor or view generation.
            this.installation.Tick(this.beforeIngress);
        }

        /// <summary>The bridge's slice of a frame: hands the serialized <c>driveInput</c> to <see cref="InputEgress"/> and runs it, after the session reset.</summary>
        private void SendInput()
        {
            this.inputEgress.DriveInput = this.driveInput;
            this.inputEgress.Step();
        }

        private void OnDestroy()
        {
            this.installation?.Dispose();
            this.installation = null;
            this.binder = null;
            this.inputEgress = null;
        }
    }
}
#endif
