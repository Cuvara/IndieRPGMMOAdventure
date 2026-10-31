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
    /// <b>This component is the thin half.</b> It keeps the serialized fields, method injection,
    /// and input sampling, sending, aim and cast; everything installed into the ECS world, the
    /// session reset and the per-frame feed live in <see cref="EcsSessionInstallation"/>, which
    /// <c>Update</c> drives and <c>OnDestroy</c> disposes. The rest of these remarks describe the
    /// behaviour of the pair.
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
    /// <b>One input owner.</b> When <see cref="driveInput"/> is on, this component samples input,
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

        private long inputTick;
        private Action beforeIngress;

        /// <summary>
        /// When the next input is due. A pinned schedule at
        /// <see cref="InputCadence.RecommendedSendHz"/>, the cadence the netcode DOTS Sample sends
        /// at and the one the prediction fix was validated against.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Why not once per frame.</b> This used to send from <c>Update</c> on every frame a
        /// key was held, which is ~1000 inputs a second on an uncapped desktop frame loop. The
        /// server steps once per base tick and coalesces the rest, so nearly every input
        /// replaced a step already taken, and the client's one-recorded-step-per-input history
        /// stopped describing what the server did.
        /// </para>
        /// <para>
        /// <b>Why idle inputs are sent too.</b> Sending only while a key was down meant a
        /// release never reached the server: it kept moving the player on the held direction
        /// for its 250 ms silence timeout and the client was then snapped back -- a rubber-band
        /// at every stop. <c>docs/API.md</c> requires the vector on every input tick.
        /// </para>
        /// </remarks>
        private InputSendSchedule sendSchedule;

        private bool sendScheduleStarted;

        /// <summary>Where the controls come from; injected by <see cref="Construct"/>.</summary>
        private IGameplayInputSource input;

        /// <summary>Edge-triggered presses latched until the next scheduled send, so none is lost between sends.</summary>
        private bool pendingJump;

        private bool pendingCast;

        private bool castWarningLogged;

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

        /// <summary>Input sampling and send: the part of a frame the bridge keeps, run after the session reset.</summary>
        private void SendInput()
        {
            if (this.driveInput && this.client.State == NetworkClientState.InWorld)
            {
                // Input is sampled and SENT here, not inside the prediction driver: the tick
                // recorded must be the tick that went to the server.
                var inputSource = this.input;
                this.pendingJump |= inputSource.JumpPressed();
                this.pendingCast |= inputSource.CastPressed();

                var now = Time.realtimeSinceStartupAsDouble;
                if (!this.sendScheduleStarted)
                {
                    this.sendSchedule.Start(InputCadence.RecommendedSendHz(GameConstants.DefaultTickRate), now);
                    this.sendScheduleStarted = true;
                }

                if (this.sendSchedule.SecondsUntilDue(now) <= 0.0)
                {
                    // Sampled at the send, so the vector sent is the stick right now. Sent even
                    // when it is zero: that is how a release reaches the server.
                    inputSource.ReadMove(out var moveX, out var moveY);
                    var jump = this.pendingJump;
                    var cast = this.pendingCast;
                    this.pendingJump = false;
                    this.pendingCast = false;

                    this.inputTick++;
                    var input = new InputMessage { Tick = this.inputTick, MoveX = moveX, MoveY = moveY, Jump = jump };
                    if (cast)
                    {
                        this.TryAddProjectileCast(input);
                    }

                    // Every input says which instant the player was looking at, so the server can
                    // rewind hit targets to it (ADR-29 decision 4). Zero before the first snapshot.
                    input.SetRenderTime(this.installation.Binder.RenderTick);
                    this.client.Session?.SendInput(input);
                    this.predictor?.RecordInput(this.inputTick, moveX, moveY, jump);

                    // The prediction system steers the predictor's clock toward the server tick;
                    // it needs to know when each input left to measure the acknowledgement lead.
                    // The round trip sizes the lead the steering aims for; refreshed per send rather
                    // than per frame because the lookup builds an entity query.
                    DotsPredictionBootstrap.NoteInputSent(this.installation.World, this.inputTick);
                    if (DotsPredictionBootstrap.TryGetClockSteering(this.installation.World, out var steering))
                    {
                        steering.RoundTripMs = this.client.Session?.RoundTripMs ?? 0L;
                    }

                    this.sendSchedule.NoteSent(now);
                }
            }
            else
            {
                this.sendScheduleStarted = false;
            }
        }

        private void OnDestroy()
        {
            this.installation?.Dispose();
            this.installation = null;
            this.binder = null;
        }

        /// <summary>
        /// Adds the content's projectile ability to <paramref name="input"/>, aimed where the
        /// pointer meets the ground at the player's height, and starts predicting it. Sends no
        /// cast at all when there is nothing valid to send (no content, a protocol 2 server, a
        /// refused spawn): a cast the client cannot predict is not one it should request.
        /// </summary>
        private void TryAddProjectileCast(InputMessage input)
        {
            var ability = this.content != null ? this.content.Catalog.FirstProjectileAbility() : null;
            if (ability == null || this.ProjectilePredictor == null)
            {
                if (!this.castWarningLogged)
                {
                    this.castWarningLogged = true;
                    Debug.LogWarning(
                        "[DotsWorldBridge] cast key ignored: " +
                        (this.ProjectilePredictor == null
                            ? $"the server speaks protocol {this.client.ServerProtocolVersion} (projectiles need 3)."
                            : "the content has no projectile ability (content not loaded yet?)."));
                }

                return;
            }

            if (!this.TryGetLocalFeet(out var feet))
            {
                return;
            }

            var aim = this.AimFrom(feet);
            var spawnSeq = this.ProjectilePredictor.Fire(
                AimPoint.LaunchOrigin(feet),
                AimPoint.LaunchTarget(aim),
                ability.ProjectileSpeed,
                ability.ProjectileRadius,
                ability.ProjectileRange);
            if (spawnSeq == 0u)
            {
                // ProjectileLogic refused (aim on the origin): send nothing, predict nothing.
                return;
            }

            input.AbilityId = ability.Id;
            input.AimX = aim.X;
            input.AimY = aim.Y;
            input.AimZ = aim.Z;
            input.SpawnSeq = spawnSeq;
        }

        /// <summary>The local player's feet in wire space: the predicted body, else the snapshot.</summary>
        private bool TryGetLocalFeet(out Vec3 feet)
        {
            if (this.predictor != null && this.predictor.IsEnabled && this.predictor.Reconciles > 0)
            {
                feet = this.predictor.Position3;
                return true;
            }

            if (this.client.World.TryGet(this.client.UserId, out var self))
            {
                feet = new Vec3(self.X, self.Y, self.Z);
                return true;
            }

            feet = default;
            return false;
        }

        /// <summary>
        /// The ground point under the pointer at the player's own height, in wire space; with no
        /// pointer (a headless or automated run) a point 10 units along wire +y.
        /// </summary>
        private Vec3 AimFrom(in Vec3 feet)
        {
            if (this.input.TryAimPoint(new Vector3(feet.X, feet.Y, feet.Z), out var explicitAim))
            {
                return new Vec3(explicitAim.x, explicitAim.y, explicitAim.z);
            }

            var camera = this.followCamera != null ? this.followCamera : Camera.main;
            if (camera != null &&
                this.input.TryPointerPosition(out var screen) &&
                AimPoint.TryGroundAim(camera.ScreenPointToRay(screen), feet.Z, out var aim))
            {
                return aim;
            }

            return new Vec3(feet.X, feet.Y + 10f, feet.Z);
        }
    }
}
#endif
