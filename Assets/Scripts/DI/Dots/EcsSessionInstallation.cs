#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER && CUVARA_NETCODE && CUVARA_SHARED_GAMELOGIC
namespace Scripts.DI.Dots
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Cuvara.DOTS.Configuration;
    using Cuvara.DOTS.Modules;
    using Cuvara.DOTS.Netcode;
    using Cuvara.DOTS.Netcode.Prediction;
    using Cuvara.DOTS.Provisioning;
    using Cuvara.DOTS.Simulation;
    using Cuvara.DOTS.Views;
    using Cuvara.Netcode.Client;
    using Cuvara.Netcode.Interpolation;
    using Cuvara.Netcode.Prediction;
    using Cuvara.Netcode.Protocol;
    using Cuvara.Netcode.Protocol.Messages;
    using Cuvara.Netcode.View;
    using Scripts.Gameplay.Maps;
    using Scripts.Gameplay.Presentation;
    using Shared.GameLogic.Components;
    using Unity.Entities;
    using Unity.Mathematics;
    using UnityEngine;

    /// <summary>
    /// The per-session ECS installation <c>DotsWorldBridge</c> used to carry inline: what is
    /// installed into the world, in which order, what is built around it (catalog, view, binder,
    /// feeders), the once-per-session reset, the per-frame ingress tick, and the exact reverse on
    /// <see cref="Dispose"/>. Plain C#, so EditMode tests run the real install and teardown over a
    /// throwaway world with a scripted client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The bridge keeps the serialized fields, method injection, input sampling and sending, and
    /// calls <see cref="TryInstall"/>, <see cref="TryFinishPrewarm"/> and <see cref="Tick"/> from
    /// <c>Update</c> in the order it always did. Nothing here reads a <c>MonoBehaviour</c>.
    /// </para>
    /// <para>
    /// <b>Teardown is the reverse of install and the order is load-bearing.</b> Prediction first
    /// (hands claimed transforms back), then elevation, then the adapter with its mirrors, then
    /// every session-scoped module, then the catalog singleton, then the provider keys; the catalog
    /// blob is freed last, after nothing reads it. The root-scoped view bootstrap is never
    /// uninstalled here.
    /// </para>
    /// </remarks>
    public sealed class EcsSessionInstallation : IDisposable
    {
        /// <summary>World units per second above which a remote entity's last move is a teleport.</summary>
        private const double RemoteTeleportSpeed = 15.0;

        private readonly Settings settings;
        private readonly IEcsWorldProvider worldProvider;
        private readonly NetworkClient client;
        private readonly LocalMovePredictor predictor;
        private readonly IViewAssetProvider viewAssetProvider;
        private readonly DotsViewLibraryReference libraryReference;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly EntityElevationTable elevationTable = new EntityElevationTable();
        private readonly SessionResetGate<GameSessionClient> sessionGate = new SessionResetGate<GameSessionClient>();
        private readonly Action<GameSessionClient> beginSession;
        private readonly Action ingress;

        private World world;
        private ViewConfigCatalog catalog;
        private ViewArchetypeLibrary library;
        private ViewConfig[] configs;
        private DotsEntityView view;
        private WorldViewBinder binder;
        private IDisposable spawnSubscription;
        private Task prewarm;
        private bool installed;
        private bool prewarmFailureLogged;
        private bool elevationInstalled;
        private bool disposed;
        private EntityElevationFeeder elevationFeeder;
        private PredictedProjectileViews predictedViews;
        private Action beforeIngress;
        private int lastGeneration;
        private readonly Dictionary<string, Task> keyPrewarms = new Dictionary<string, Task>();
        private readonly HashSet<string> pendingAtDispose = new HashSet<string>();

        /// <summary>Everything the installation needs from the bridge, resolved.</summary>
        public sealed class Settings
        {
            /// <summary>Scene-assigned view library; null uses the root scope's.</summary>
            public DotsViewLibraryAsset ViewLibrary;

            public bool EnableCameraFollow = true;

            /// <summary>Camera to follow with; null uses <c>Camera.main</c> at install.</summary>
            public Camera FollowCamera;

            public Vector3 CameraOffset = new Vector3(0f, 10f, -8f);

            public bool EnableMinimap;

            /// <summary>Parent of the predicted-projectile spheres (the bridge's transform).</summary>
            public Transform ViewRoot;
        }

        public EcsSessionInstallation(
            IEcsWorldProvider worldProvider,
            NetworkClient client,
            LocalMovePredictor predictor,
            IViewAssetProvider viewAssetProvider,
            DotsViewLibraryReference libraryReference,
            Settings settings)
        {
            this.worldProvider = worldProvider;
            this.client = client;
            this.predictor = predictor;
            this.viewAssetProvider = viewAssetProvider;
            this.libraryReference = libraryReference;
            this.settings = settings ?? new Settings();
            this.beginSession = this.OnSessionBegan;
            this.ingress = this.TickIngress;
        }

        /// <summary>
        /// Receives the name of every teardown step as <see cref="Dispose"/> performs it, in order.
        /// Tests only; null in production, so teardown behaviour is unchanged.
        /// </summary>
        internal Action<string> TeardownRecorder { get; set; }

        /// <summary>True once the catalog is installed and every key is warm; the binder ticks only then.</summary>
        public bool IsReady { get; private set; }

        /// <summary>True after a successful <see cref="TryInstall"/>.</summary>
        public bool IsInstalled => this.installed;

        /// <summary>Reconnects turned into view generations. Diagnostics.</summary>
        public int ReconnectsHandled { get; private set; }

        /// <summary>The world this installation presents into; null before <see cref="TryInstall"/>.</summary>
        public World World => this.world;

        /// <summary>The binder fed every frame; null before install.</summary>
        public WorldViewBinder Binder => this.binder;

        /// <summary>The adapter's view; null before install.</summary>
        public DotsEntityView View => this.view;

        /// <summary>The local projectile predictor of the current session; null before a protocol 3 join.</summary>
        public ProjectilePredictor ProjectilePredictor { get; private set; }

        /// <summary>
        /// Builds the catalog and installs everything into the world, then starts the prewarm.
        /// False means this installation cannot work (no world, invalid library) and the caller
        /// disables itself; whatever was logged says why.
        /// </summary>
        public bool TryInstall()
        {
            try
            {
                this.world = this.worldProvider.World;
            }
            catch (InvalidOperationException)
            {
                this.world = null;
            }

            if (this.world == null)
            {
                Debug.LogWarning("[DotsWorldBridge] no default ECS world — DOTS presentation disabled.");
                return false;
            }

            if (!this.TryBuildCatalog())
            {
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
                minimap: this.settings.EnableMinimap ? MinimapCategories.Instance : null);
            DotsNetcodeBootstrap.Install(this.world, this.view);

            // Teleport guard for remote interpolation (com.cuvara.netcode >= 0.48.0): a last
            // segment faster than this is a respawn/teleport and is not extrapolated past the
            // destination. Three times the fastest legitimate ground speed (players move at
            // 5 u/s, enemies at 2.5 u/s); the testlab saw a respawned player drawn 5 units past
            // its spawn without it (rpg-factory testlab S05).
            var interpolation = InterpolationConfig.Default;
            interpolation.MaxExtrapolationSpeed = RemoteTeleportSpeed;
            this.binder = new WorldViewBinder(this.view, null, null, interpolation);

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

            this.predictedViews = new PredictedProjectileViews(this.settings.ViewRoot);

            this.client.Reconnected += this.OnReconnected;

            // Prewarm from the catalog's own pool sizes rather than numbers typed here. In
            // production this is the Addressables load per key; the binder waits for it.
            this.prewarm = this.PrewarmAsync(this.lifetime.Token);
            _ = ObserveAggregate(this.prewarm);

            this.installed = true;
            return true;
        }

        /// <summary>
        /// Polls the prewarm. True once every key is warm. <paramref name="disable"/> is set once,
        /// on the poll that first reports a failure, and the caller disables itself.
        /// </summary>
        public bool TryFinishPrewarm(out bool disable)
        {
            disable = false;
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
                    disable = true;
                }

                return false;
            }

            this.prewarm = null;
            this.IsReady = true;
            return true;
        }

        /// <summary>
        /// One frame. The gate runs the session reset before the frame's work, so the first frame
        /// of a new session never meets the old session's binder, predictor or view generation;
        /// <paramref name="beforeIngress"/> (the bridge's input send) then runs, then the ingress tick.
        /// </summary>
        public void Tick(Action beforeIngress)
        {
            this.beforeIngress = beforeIngress;
            this.sessionGate.Tick(
                this.client.State == NetworkClientState.InWorld,
                this.client.Session,
                this.beginSession,
                this.ingress);
        }

        /// <summary>Reverse of <see cref="TryInstall"/>; safe to call twice and when never installed.</summary>
        public void Dispose()
        {
            if (this.disposed) return;
            this.disposed = true;

            // Keys still prewarming now: if one settles successfully later the provider ignored
            // the token, and the key it warmed after its release is released again.
            foreach (var pair in this.keyPrewarms)
            {
                if (!pair.Value.IsCompleted) this.pendingAtDispose.Add(pair.Key);
            }

            this.lifetime.Cancel();

            this.Step("unsubscribe-reconnect");
            if (this.client != null)
            {
                this.client.Reconnected -= this.OnReconnected;
            }

            this.Step("dispose-spawn-subscription");
            this.spawnSubscription?.Dispose();
            this.spawnSubscription = null;

            this.Step("dispose-predicted-views");
            this.predictedViews?.Dispose();
            this.predictedViews = null;

            if (this.installed && this.world is { IsCreated: true })
            {
                // Reverse of install. Prediction first (hands claimed transforms back), then the
                // adapter with its mirrors (one NetworkEntityDespawned per id, reason Teardown),
                // then every session-scoped module in reverse install order, then the catalog
                // singleton — the blob is freed below, after nothing reads it.
                this.Step("uninstall-prediction");
                DotsPredictionBootstrap.Uninstall(this.world);
                if (this.elevationInstalled)
                {
                    this.Step("uninstall-elevation");
                    EntityElevationBootstrap.Uninstall(this.world);
                    this.elevationInstalled = false;
                }

                this.Step("uninstall-netcode");
                DotsNetcodeBootstrap.Uninstall(this.world, destroyMirrors: true);
                this.Step("uninstall-session-modules");
                DotsModules.UninstallScope(this.world, DotsModuleScope.Session);
                this.Step("uninstall-catalog");
                this.catalog?.Uninstall(this.world);

                // Assets after instances: the provider drops each key's pooled instances now and
                // its prefab handle once the view layer has recycled the last live instance —
                // which happens on the next presentation tick, after the mirrors above were
                // destroyed. A key the next scene's catalog lists is simply re-leased.
                if (this.catalog != null && this.viewAssetProvider != null)
                {
                    foreach (var key in this.catalog.ViewKeys())
                    {
                        this.Step("release:" + key);
                        this.viewAssetProvider.Release(key);
                    }
                }
            }

            this.DisposeCatalogAndLibrary();
            this.lifetime.Dispose();
        }

        /// <summary>
        /// Everything in a frame that follows the session reset: the caller's input send, then the
        /// binder feed, projectile advance, elevation feed and predicted-projectile views.
        /// </summary>
        private void TickIngress()
        {
            this.beforeIngress?.Invoke();

            // Polls the merged world every frame — despawn falls out of absence. This is the ONE
            // feed into the adapter; see the bridge's class remarks on SetStateAtTick.
            this.binder.Tick(this.client.World, this.client.UserId);

            // Height and projectile positions for this frame's mirrors, read by
            // EntityElevationSystem later in the same frame (Presentation).
            this.ProjectilePredictor?.Advance(Time.deltaTime);
            this.elevationFeeder?.Feed(
                this.client.World,
                this.client.UserId,
                this.binder.RenderTick,
                Time.realtimeSinceStartupAsDouble,
                this.predictor,
                this.ProjectilePredictor);
            this.predictedViews?.Sync(this.ProjectilePredictor);
        }

        private bool TryBuildCatalog()
        {
            var asset = this.settings.ViewLibrary != null ? this.settings.ViewLibrary : this.libraryReference?.Asset;
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

        private async Task PrewarmAsync(CancellationToken cancellationToken)
        {
            var tasks = new List<Task>();
            foreach (var pair in this.catalog.PoolSizesByKey())
            {
                var task = this.viewAssetProvider.PrewarmAsync(pair.Key, pair.Value, cancellationToken);
                tasks.Add(task);
                this.keyPrewarms[pair.Key] = task;
                _ = this.ObserveKeyAsync(pair.Key, task);
            }

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Settles one key's prewarm on the main thread. The cancellation <see cref="Dispose"/>
        /// causes is expected and silent; any other fault is logged once. A prewarm that ran to
        /// completion after <see cref="Dispose"/> began (a provider that ignored the token) has warmed a
        /// key whose release already ran, so the key is released again here.
        /// </summary>
        private async Task ObserveKeyAsync(string key, Task task)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException) when (this.lifetime.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return;
            }

            if (this.pendingAtDispose.Contains(key))
            {
                this.viewAssetProvider.Release(key);
            }
        }

        // The aggregate task is polled by TryFinishPrewarm; this only keeps it observed when
        // Dispose comes first. The per-key observers do the logging.
        private static async Task ObserveAggregate(Task task)
        {
            try
            {
                await task;
            }
            catch
            {
                // Reported by the per-key observers.
            }
        }

        private void Step(string name) => this.TeardownRecorder?.Invoke(name);

        private void InstallSessionModules()
        {
            if (this.settings.EnableCameraFollow)
            {
                var camera = this.settings.FollowCamera != null ? this.settings.FollowCamera : Camera.main;
                if (camera == null)
                {
                    Debug.LogWarning("[DotsWorldBridge] CameraFollow enabled but no camera found; module not installed.");
                }
                else
                {
                    var offset = this.settings.CameraOffset;
                    CameraFollowBootstrap.Install(this.world, new CameraFollowConfig
                    {
                        Camera = camera,
                        Offset = new float3(offset.x, offset.y, offset.z),
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

            if (this.settings.EnableMinimap)
            {
                MinimapBootstrap.Install(this.world, scope: DotsModuleScope.Session);
            }
        }

        /// <summary>
        /// Routes the netcode <c>Reconnected</c> event through the same once-per-session reset as
        /// the per-frame check.
        /// </summary>
        /// <remarks>
        /// Contract relied on (com.cuvara.netcode v0.50.0, <c>Runtime/Client/NetworkClient.cs</c>):
        /// <c>Reconnected</c> is raised only after <c>_session</c> is set to the new
        /// <see cref="GameSessionClient"/> in the join commit, so <c>client.Session</c> is already
        /// the new object here. If it is null anyway, nothing is reset now and the per-frame check
        /// begins the session when a non-null object is seen in the world.
        /// </remarks>
        private void OnReconnected()
        {
            if (!this.installed || this.view == null) return;

            var outcome = this.sessionGate.OnReconnected(this.client.Session, this.beginSession);
            switch (outcome)
            {
                case ReconnectOutcome.Began:
                    this.ReconnectsHandled++;
                    Debug.Log($"[DotsWorldBridge] Reconnected: view generation {this.lastGeneration}, predictor reset, camera smoothing reset.");
                    break;
                case ReconnectOutcome.AlreadyBegun:
                    this.ReconnectsHandled++;
                    Debug.Log($"[DotsWorldBridge] Reconnected: reset already ran for this session (view generation {this.lastGeneration}); no second reset.");
                    break;
                default:
                    Debug.LogWarning("[DotsWorldBridge] Reconnected: no session object at callback time; no reset ran, the per-frame check will begin the session.");
                    break;
            }
        }

        /// <summary>
        /// The one session-start reset: a connect, reconnect, manual retry, dungeon join or map
        /// transfer all arrive here with a new <see cref="GameSessionClient"/>.
        /// </summary>
        private void OnSessionBegan(GameSessionClient session)
        {
            // Order: generation first, so the old session's queued commands are already stale by
            // the time the binder's next Tick feeds the new world in; then the binder and the
            // predictor's backlog, which the new server never acknowledged; then the camera,
            // which would otherwise sweep from the last known position to the re-placed avatar.
            this.lastGeneration = this.view.BeginGeneration();

            // The binder's interpolation clock, last world tick and live set belong to the old
            // session too. NetworkClient resets the world on a new session, and a new server (a
            // drain to another pod, a restart, a dungeon) counts ticks from far below or above the
            // old ones: without this, `world.Tick > lastWorldTick` can stay false until the new
            // ticks pass the old, so no sample is buffered, and RenderTick - sent as the
            // lag-compensation render time - stays in the old tick space. After BeginGeneration,
            // so its despawns are stale no-ops for the drain.
            this.binder.Reset();
            this.predictor?.Reset();
            this.ProjectilePredictor?.Reset();
            this.elevationFeeder?.Reset();

            // The new session may be a different server: select the movement model and geometry
            // for it before its first reconcile.
            this.ApplyServerProtocol();
            CameraFollowBootstrap.ResetSmoothing(this.world);

            Debug.Log($"[DotsWorldBridge] Session began: view generation {this.lastGeneration}, binder and predictor reset.");
        }

        /// <summary>
        /// Called from <see cref="OnSessionBegan"/>: selects the server's movement model, supplies
        /// the map's geometry, and creates the projectile predictor for a protocol 3 server.
        /// </summary>
        private void ApplyServerProtocol()
        {
            var version = this.client.ServerProtocolVersion;
            var mapId = this.client.CurrentMapId;
            var geometry = MapGeometrySource.LoadOrNull(mapId, out var origin);

            if (this.predictor != null)
            {
                // Order matters: a model change resets the predictor, then the geometry it steps
                // against. Null geometry restores the flat world of the predictor's own bounds,
                // which is what the server runs for a map with no file.
                this.predictor.UseServerProtocol(version);
                // The predictor is registered before any join with a guessed rate; the server's
                // advertised tick_rate (60 Hz base ticks) replaces it. Left at the 15 Hz guess,
                // every reconcile missed the history and corrected in whole 15 Hz steps
                // (Cuvara/Netcode#183, netcode 0.48.0).
                this.predictor.UseServerTickRate(this.client.TickRate);
                this.predictor.SetMapGeometry(geometry);
            }

            if (WireProtocolVersion.Supports(version, WireProtocolVersion.Motor3D))
            {
                var tickRate = this.client.TickRate > 0
                    ? (int)this.client.TickRate
                    : this.predictor?.TickRateHz ?? GameConstants.DefaultTickRate;
                this.ProjectilePredictor = new ProjectilePredictor(tickRate, geometry ?? this.predictor?.Geometry);
            }
            else
            {
                this.ProjectilePredictor = null;
            }

            Debug.Log(
                $"[DotsWorldBridge] server protocol {version}: " +
                $"{(this.predictor != null && this.predictor.UsesCharacterMotor ? "3D CharacterMotor" : "planar")} prediction, " +
                $"map '{mapId}' geometry {origin}, projectile prediction {(this.ProjectilePredictor != null ? "on" : "off")}");
        }

        private void DisposeCatalogAndLibrary()
        {
            // After the systems that read the blob are gone, never before — a disposed catalog
            // under a live spawn system is a dangling blob pointer.
            this.Step("dispose-catalog");
            this.catalog?.Dispose();
            this.catalog = null;

            if (this.library != null)
            {
                this.Step("dispose-library");
                DestroyUnityObject(this.library);
                this.library = null;
            }

            if (this.configs != null)
            {
                foreach (var config in this.configs)
                {
                    if (config != null)
                    {
                        DestroyUnityObject(config);
                    }
                }

                this.configs = null;
            }
        }

        // Destroy in play mode (what the bridge always did); EditMode tests cannot call it.
        private static void DestroyUnityObject(UnityEngine.Object target)
        {
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(target);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
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
