#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER && CUVARA_NETCODE && CUVARA_SHARED_GAMELOGIC
namespace Scripts.DI.Dots
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Cuvara.DOTS.Configuration;
    using Cuvara.DOTS.Modules;
    using Cuvara.DOTS.Netcode;
    using Cuvara.DOTS.Netcode.Prediction;
    using Cuvara.DOTS.Provisioning;
    using Cuvara.DOTS.Simulation;
    using Cuvara.DOTS.Views;
    using Cuvara.Netcode.Client;
    using Cuvara.Netcode.Prediction;
    using Cuvara.Netcode.Protocol;
    using Cuvara.Netcode.Protocol.Messages;
    using Cuvara.Netcode.View;
    using Scripts.Gameplay.Content;
    using Scripts.Gameplay.Maps;
    using Scripts.Gameplay.Presentation;
    using Shared.GameLogic.Components;
    using Unity.Entities;
    using Unity.Mathematics;
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
    /// <b>Reconnect is a generation boundary.</b> On <c>NetworkClient.Reconnected</c> the view
    /// begins a new generation (queued old-session commands are dropped, old mirrors torn down
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

        private World world;
        private ViewConfigCatalog catalog;
        private ViewArchetypeLibrary library;
        private ViewConfig[] configs;
        private DotsEntityView view;
        private WorldViewBinder binder;
        private IDisposable spawnSubscription;
        private Task prewarm;
        private bool installed;
        private bool ready;
        private bool prewarmFailureLogged;
        private long inputTick;

        // ---- protocol 3 ----
        private readonly EntityElevationTable elevationTable = new EntityElevationTable();
        private EntityElevationFeeder elevationFeeder;
        private bool elevationInstalled;
        private ProjectilePredictor projectilePredictor;
        private PredictedProjectileViews predictedViews;
        private GameSessionClient protocolAppliedTo;
        private bool castWarningLogged;

        /// <summary>True once the catalog is installed and every key is warm; the binder ticks only then.</summary>
        public bool IsReady => this.ready;

        /// <summary>Reconnects this bridge has turned into view generations. Diagnostics.</summary>
        public int ReconnectsHandled { get; private set; }

        /// <summary>The local projectile predictor of the current session; null before a protocol 3 join.</summary>
        public ProjectilePredictor ProjectilePredictor => this.projectilePredictor;

        /// <summary>Called by the scene scope's build callback; absent a container this component stays inert.</summary>
        [Inject]
        public void Construct(
            NetworkClient networkClient,
            LocalMovePredictor movePredictor,
            IViewAssetProvider assetProvider,
            DotsViewLibraryReference viewLibraryReference,
            GameContentService gameContent)
        {
            this.client = networkClient;
            this.predictor = movePredictor;
            this.viewAssetProvider = assetProvider;
            this.libraryReference = viewLibraryReference;
            this.content = gameContent;
        }

        private void Update()
        {
            if (this.client == null)
            {
                return;
            }

            if (!this.installed && !this.TryInstall())
            {
                return;
            }

            if (!this.ready && !this.TryFinishPrewarm())
            {
                return;
            }

            if (this.client.State == NetworkClientState.InWorld)
            {
                this.EnsureServerProtocolApplied();
            }

            if (this.driveInput && this.client.State == NetworkClientState.InWorld)
            {
                // Input is sampled and SENT here, not inside the prediction driver: the tick
                // recorded must be the tick that went to the server.
                ReadKeyboard(out var moveX, out var moveY);
                var jump = GameplayInputReader.JumpPressed();
                var cast = GameplayInputReader.CastPressed();
                if (moveX != 0f || moveY != 0f || jump || cast)
                {
                    this.inputTick++;
                    var input = new InputMessage { Tick = this.inputTick, MoveX = moveX, MoveY = moveY, Jump = jump };
                    if (cast)
                    {
                        this.TryAddProjectileCast(input);
                    }

                    // Every input says which instant the player was looking at, so the server can
                    // rewind hit targets to it (ADR-29 decision 4). Zero before the first snapshot.
                    input.SetRenderTime(this.binder.RenderTick);
                    this.client.Session?.SendInput(input);
                    this.predictor?.RecordInput(this.inputTick, moveX, moveY, jump);
                }
            }

            // Polls the merged world every frame — despawn falls out of absence. This is the ONE
            // feed into the adapter; see the class remarks on SetStateAtTick.
            this.binder.Tick(this.client.World, this.client.UserId);

            // Height and projectile positions for this frame's mirrors, read by
            // EntityElevationSystem later in the same frame (Presentation).
            this.projectilePredictor?.Advance(Time.deltaTime);
            this.elevationFeeder?.Feed(
                this.client.World,
                this.client.UserId,
                this.binder.RenderTick,
                Time.realtimeSinceStartupAsDouble,
                this.predictor,
                this.projectilePredictor);
            this.predictedViews?.Sync(this.projectilePredictor);
        }

        private bool TryInstall()
        {
            this.world = World.DefaultGameObjectInjectionWorld;
            if (this.world == null)
            {
                Debug.LogWarning("[DotsWorldBridge] no default ECS world — DOTS presentation disabled.");
                this.enabled = false;
                return false;
            }

            if (!this.TryBuildCatalog())
            {
                this.enabled = false;
                return false;
            }

            // Simulation systems are idempotent to install and usable without views; the view
            // bootstrap itself was installed by RegisterDots at root-container build.
            DotsSimulationBootstrap.InstallSimulationSystems(this.world);
            this.catalog.Install(this.world);

            // Kind comes from the wire, never from the id. No catch-all: an unmapped server kind
            // is refused and logged once, so a server that grows a new type says so instead of
            // rendering it as a player. The rule list is DotsViewArchetypes.ServerKindMappings,
            // the same table the library was validated against.
            var rules = new TypeArchetypeResolver.Rule[DotsViewArchetypes.ServerKindMappings.Length];
            for (var i = 0; i < rules.Length; i++)
            {
                var pair = DotsViewArchetypes.ServerKindMappings[i];
                rules[i] = new TypeArchetypeResolver.Rule(pair.Key, pair.Value);
            }

            var resolver = new TypeArchetypeResolver(
                localArchetype: DotsViewArchetypes.PlayerLocal,
                unknownArchetype: null,
                rules);

            // XZPlane: the server's 2D plane is Unity's ground plane. Per-art lift belongs in
            // ViewConfig.PositionOffset, not in the mapping. Wire hp lands on NetworkEntityState
            // (writeHealth defaults to false) so no client-side system destroys an entity the
            // server still lists. The minimap resolver is handed over only when the module is on.
            this.view = new DotsEntityView(
                this.catalog,
                resolver,
                SnapshotSpaceMapping.XZPlane,
                minimap: this.enableMinimap ? MinimapCategories.Instance : null);
            DotsNetcodeBootstrap.Install(this.world, this.view);

            this.binder = new WorldViewBinder(this.view);

            if (this.predictor != null)
            {
                DotsPredictionBootstrap.Install(this.world, this.predictor, this.client.World);
            }

            this.InstallSessionModules();

            // Height for every mirror (protocol 3). Absent a view group nothing is presented, so
            // there is nothing to lift; say so rather than half-install.
            if (EntityElevationBootstrap.Install(this.world, this.elevationTable) != null)
            {
                this.elevationInstalled = true;
                this.elevationFeeder = new EntityElevationFeeder(this.elevationTable);
            }
            else
            {
                Debug.LogWarning("[DotsWorldBridge] no ViewSystemGroup in the world; entity height (wire z) is not presented.");
            }

            this.predictedViews = new PredictedProjectileViews(this.transform);

            this.client.Reconnected += this.OnReconnected;

            // Prewarm from the catalog's own pool sizes rather than numbers typed here. In
            // production this is the Addressables load per key; the binder waits for it.
            this.prewarm = this.PrewarmAsync();

            this.installed = true;
            return true;
        }

        private bool TryBuildCatalog()
        {
            var asset = this.viewLibrary != null ? this.viewLibrary : this.libraryReference?.Asset;
            var mode = this.libraryReference?.Mode ?? DotsViewProviderMode.Production;

            if (asset != null)
            {
                this.library = asset.BuildLibrary(out this.configs);
            }
            else if (mode == DotsViewProviderMode.Primitive || this.viewAssetProvider is PrimitiveViewAssetProvider)
            {
                this.BuildPlaceholderLibrary();
            }
            else
            {
                Debug.LogError(
                    $"[DotsWorldBridge] Production view provider but no DotsViewLibrary asset — assign one on this " +
                    $"component or create '{DotsViewLibraryAsset.DefaultAssetPath}'. DOTS presentation disabled.");
                return false;
            }

            // prefabExists is the provider's own answer: the leased provider asks its loader, the
            // primitive one its shape table. A key neither can serve fails here, before any entity
            // could resolve to it.
            Func<string, bool> prefabExists = null;
            switch (this.viewAssetProvider)
            {
                case LeasedViewAssetProvider leased:
                    prefabExists = leased.CanProvide;
                    break;
                case PrimitiveViewAssetProvider primitive:
                    prefabExists = primitive.IsWarm;
                    break;
            }

            this.catalog = new ViewConfigCatalog();
            var built = this.catalog.TryBuild(this.library, out var report, prefabExists);

            var mappings = ViewConfigValidator.ValidateMappings(this.library, DotsViewArchetypes.ServerKindMappings);
            foreach (var issue in mappings.Issues) report.Add(issue);

            if (!built || report.HasErrors)
            {
                report.Log();
                Debug.LogError(
                    $"[DotsWorldBridge] View library '{this.library.name}' is invalid ({report.ErrorCount} error(s), " +
                    "listed above). Fix the DotsViewLibrary asset; the build-time check in PlayerBuilder catches the " +
                    "same errors. DOTS presentation disabled.");
                this.DisposeCatalogAndLibrary();
                return false;
            }

            if (report.WarningCount > 0) report.Log();
            return true;
        }

        private async Task PrewarmAsync()
        {
            var tasks = new List<Task>();
            foreach (var pair in this.catalog.PoolSizesByKey())
            {
                tasks.Add(this.viewAssetProvider.PrewarmAsync(pair.Key, pair.Value));
            }

            await Task.WhenAll(tasks);
        }

        private bool TryFinishPrewarm()
        {
            if (this.prewarm == null) return true;
            if (!this.prewarm.IsCompleted) return false;

            if (this.prewarm.IsFaulted || this.prewarm.IsCanceled)
            {
                if (!this.prewarmFailureLogged)
                {
                    this.prewarmFailureLogged = true;
                    Debug.LogError(
                        "[DotsWorldBridge] Prewarming the view catalog failed; DOTS presentation disabled. " +
                        $"{this.prewarm.Exception?.GetBaseException().Message}");
                    this.enabled = false;
                }

                return false;
            }

            this.prewarm = null;
            this.ready = true;
            return true;
        }

        private void InstallSessionModules()
        {
            if (this.enableCameraFollow)
            {
                var camera = this.followCamera != null ? this.followCamera : Camera.main;
                if (camera == null)
                {
                    Debug.LogWarning("[DotsWorldBridge] CameraFollow enabled but no camera found; module not installed.");
                }
                else
                {
                    CameraFollowBootstrap.Install(this.world, new CameraFollowConfig
                    {
                        Camera = camera,
                        Offset = new float3(this.cameraOffset.x, this.cameraOffset.y, this.cameraOffset.z),
                    }, DotsModuleScope.Session);

                    // The local player's mirror is the follow target, tagged as its life begins.
                    // The event is published on the drain's thread after the entity is complete,
                    // and an EntityManager structural change from a handler is allowed there.
                    this.spawnSubscription = this.view.Lifecycle.Subscribe((NetworkEntitySpawned spawned) =>
                    {
                        if (!spawned.IsLocal || this.world == null || !this.world.IsCreated) return;
                        var entityManager = this.world.EntityManager;
                        if (entityManager.Exists(spawned.Entity) && !entityManager.HasComponent<CameraFollowTarget>(spawned.Entity))
                        {
                            entityManager.AddComponent<CameraFollowTarget>(spawned.Entity);
                        }
                    });
                }
            }

            if (this.enableMinimap)
            {
                MinimapBootstrap.Install(this.world, scope: DotsModuleScope.Session);
            }
        }

        private void OnReconnected()
        {
            if (!this.installed || this.view == null) return;

            // Order: generation first, so the old session's queued commands are already stale by
            // the time the binder's next Tick feeds the reconnected world in; then the predictor's
            // backlog, which the new server never acknowledged; then the camera, which would
            // otherwise sweep from the last known position to the re-placed avatar.
            var generation = this.view.BeginGeneration();
            this.predictor?.Reset();
            this.projectilePredictor?.Reset();
            this.elevationFeeder?.Reset();

            // The reconnected session may be a different server (a transfer, a restarted pod):
            // re-select the movement model and geometry for it before its first reconcile.
            this.protocolAppliedTo = null;
            this.EnsureServerProtocolApplied();
            CameraFollowBootstrap.ResetSmoothing(this.world);
            this.ReconnectsHandled++;

            Debug.Log($"[DotsWorldBridge] Reconnected: view generation {generation}, predictor reset, camera smoothing reset.");
        }

        private void OnDestroy()
        {
            if (this.client != null)
            {
                this.client.Reconnected -= this.OnReconnected;
            }

            this.spawnSubscription?.Dispose();
            this.spawnSubscription = null;

            this.predictedViews?.Dispose();
            this.predictedViews = null;

            if (this.installed && this.world is { IsCreated: true })
            {
                // Reverse of install. Prediction first (hands claimed transforms back), then the
                // adapter with its mirrors (one NetworkEntityDespawned per id, reason Teardown),
                // then every session-scoped module in reverse install order, then the catalog
                // singleton — the blob is freed below, after nothing reads it.
                DotsPredictionBootstrap.Uninstall(this.world);
                if (this.elevationInstalled)
                {
                    EntityElevationBootstrap.Uninstall(this.world);
                    this.elevationInstalled = false;
                }

                DotsNetcodeBootstrap.Uninstall(this.world, destroyMirrors: true);
                DotsModules.UninstallScope(this.world, DotsModuleScope.Session);
                this.catalog?.Uninstall(this.world);

                // Assets after instances: the provider drops each key's pooled instances now and
                // its prefab handle once the view layer has recycled the last live instance —
                // which happens on the next presentation tick, after the mirrors above were
                // destroyed. A key the next scene's catalog lists is simply re-leased.
                if (this.catalog != null && this.viewAssetProvider != null)
                {
                    foreach (var key in this.catalog.ViewKeys())
                    {
                        this.viewAssetProvider.Release(key);
                    }
                }
            }

            this.DisposeCatalogAndLibrary();
        }

        private void DisposeCatalogAndLibrary()
        {
            // After the systems that read the blob are gone, never before — a disposed catalog
            // under a live spawn system is a dangling blob pointer.
            this.catalog?.Dispose();
            this.catalog = null;

            if (this.library != null)
            {
                Destroy(this.library);
                this.library = null;
            }

            if (this.configs != null)
            {
                foreach (var config in this.configs)
                {
                    if (config != null)
                    {
                        Destroy(config);
                    }
                }

                this.configs = null;
            }
        }

        /// <summary>
        /// Once per session (and again after a reconnect): selects the server's movement model,
        /// supplies the map's geometry, and creates the projectile predictor for a protocol 3
        /// server. Keyed on the session object, so a transfer or reconnect re-applies.
        /// </summary>
        private void EnsureServerProtocolApplied()
        {
            var session = this.client.Session;
            if (session == null || ReferenceEquals(session, this.protocolAppliedTo))
            {
                return;
            }

            this.protocolAppliedTo = session;
            var version = this.client.ServerProtocolVersion;
            var mapId = this.client.CurrentMapId;
            var geometry = MapGeometrySource.LoadOrNull(mapId, out var origin);

            if (this.predictor != null)
            {
                // Order matters: a model change resets the predictor, then the geometry it steps
                // against. Null geometry restores the flat world of the predictor's own bounds,
                // which is what the server runs for a map with no file.
                this.predictor.UseServerProtocol(version);
                this.predictor.SetMapGeometry(geometry);
            }

            if (WireProtocolVersion.Supports(version, WireProtocolVersion.Motor3D))
            {
                var tickRate = this.client.TickRate > 0
                    ? (int)this.client.TickRate
                    : this.predictor?.TickRateHz ?? GameConstants.DefaultTickRate;
                this.projectilePredictor = new ProjectilePredictor(tickRate, geometry ?? this.predictor?.Geometry);
            }
            else
            {
                this.projectilePredictor = null;
            }

            Debug.Log(
                $"[DotsWorldBridge] server protocol {version}: " +
                $"{(this.predictor != null && this.predictor.UsesCharacterMotor ? "3D CharacterMotor" : "planar")} prediction, " +
                $"map '{mapId}' geometry {origin}, projectile prediction {(this.projectilePredictor != null ? "on" : "off")}");
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
            if (ability == null || this.projectilePredictor == null)
            {
                if (!this.castWarningLogged)
                {
                    this.castWarningLogged = true;
                    Debug.LogWarning(
                        "[DotsWorldBridge] cast key ignored: " +
                        (this.projectilePredictor == null
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
            var spawnSeq = this.projectilePredictor.Fire(
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
            var camera = this.followCamera != null ? this.followCamera : Camera.main;
            if (camera != null &&
                GameplayInputReader.TryPointerPosition(out var screen) &&
                AimPoint.TryGroundAim(camera.ScreenPointToRay(screen), feet.Z, out var aim))
            {
                return aim;
            }

            return new Vec3(feet.X, feet.Y + 10f, feet.Z);
        }

        /// <summary>
        /// WASD/arrows through whichever input backend the project enables. This project runs
        /// "Input System Package (New)" (<c>activeInputHandler: 1</c>), under which the legacy
        /// <c>UnityEngine.Input</c> class THROWS rather than returning zero — so the legacy call
        /// is compiled only when the legacy manager is actually enabled.
        /// </summary>
        private static void ReadKeyboard(out float x, out float y)
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null)
            {
                // No keyboard device — a headless or automated run. Not an error.
                x = 0f;
                y = 0f;
                return;
            }

            static float Axis(bool positive, bool negative) => (positive ? 1f : 0f) - (negative ? 1f : 0f);
            x = Axis(keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed,
                     keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed);
            y = Axis(keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed,
                     keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed);
#elif ENABLE_LEGACY_INPUT_MANAGER
            // Raw, not smoothed: GetAxis's acceleration curve would make the predicted vector
            // differ from what the player would say they pressed.
            x = Input.GetAxisRaw("Horizontal");
            y = Input.GetAxisRaw("Vertical");
#else
            x = 0f;
            y = 0f;
#endif
        }

        /// <summary>
        /// The placeholder library for scenes with no authored art (primitive provider): the
        /// three archetypes as capsules and a sphere, the way the package's NetworkedPrediction
        /// sample does it. Never used on the production path — that path requires the asset.
        /// </summary>
        private void BuildPlaceholderLibrary()
        {
            ViewConfig Config(string key, float scale, float lift)
            {
                var config = ScriptableObject.CreateInstance<ViewConfig>();
                config.name = key;
                // The lift is the art's half-height, authored as a config offset — the entity
                // stays on the plane the server simulates on; only the visual is raised.
                config.Configure(key, pool: 8, uniformScale: scale, position: new Vector3(0f, lift, 0f));
                return config;
            }

            this.configs = new[]
            {
                Config(DotsViewArchetypes.PlayerLocal, 1.2f, 1f),
                Config(DotsViewArchetypes.PlayerRemote, 1f, 1f),
                Config(DotsViewArchetypes.Mob, 0.8f, 0.5f),
                Config(DotsViewArchetypes.Projectile, 0.4f, 0f),
                Config(DotsViewArchetypes.Item, 0.5f, 0.25f),
            };

            this.library = ScriptableObject.CreateInstance<ViewArchetypeLibrary>();
            this.library.name = "PlaceholderViewLibrary";
            this.library.Configure(
                new ViewArchetypeLibrary.Entry { Name = DotsViewArchetypes.PlayerLocal, Config = this.configs[0] },
                new ViewArchetypeLibrary.Entry { Name = DotsViewArchetypes.PlayerRemote, Config = this.configs[1] },
                new ViewArchetypeLibrary.Entry { Name = DotsViewArchetypes.Mob, Config = this.configs[2] },
                new ViewArchetypeLibrary.Entry { Name = DotsViewArchetypes.Projectile, Config = this.configs[3] },
                new ViewArchetypeLibrary.Entry { Name = DotsViewArchetypes.Item, Config = this.configs[4] });
        }

        /// <summary>Minimap categories by archetype: 0 local player, 1 remote player, 2 mob.</summary>
        private sealed class MinimapCategories : IMinimapCategoryResolver
        {
            public static readonly MinimapCategories Instance = new MinimapCategories();

            public bool TryResolve(in NetworkEntityDescriptor entity, out int category)
            {
                if (entity.IsLocal)
                {
                    category = 0;
                    return true;
                }

                switch (entity.Type)
                {
                    case DotsViewArchetypes.ServerKindPlayer:
                        category = 1;
                        return true;
                    case DotsViewArchetypes.ServerKindMob:
                        category = 2;
                        return true;
                    default:
                        category = -1;
                        return false;
                }
            }
        }
    }
}
#endif
