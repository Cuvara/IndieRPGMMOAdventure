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
    using Cuvara.Netcode.View;
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

        /// <summary>True once the catalog is installed and every key is warm; the binder ticks only then.</summary>
        public bool IsReady => this.ready;

        /// <summary>Reconnects this bridge has turned into view generations. Diagnostics.</summary>
        public int ReconnectsHandled { get; private set; }

        /// <summary>Called by the scene scope's build callback; absent a container this component stays inert.</summary>
        [Inject]
        public void Construct(
            NetworkClient networkClient,
            LocalMovePredictor movePredictor,
            IViewAssetProvider assetProvider,
            DotsViewLibraryReference viewLibraryReference)
        {
            this.client = networkClient;
            this.predictor = movePredictor;
            this.viewAssetProvider = assetProvider;
            this.libraryReference = viewLibraryReference;
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

            if (this.driveInput && this.client.State == NetworkClientState.InWorld)
            {
                // Input is sampled and SENT here, not inside the prediction driver: the tick
                // recorded must be the tick that went to the server.
                ReadKeyboard(out var moveX, out var moveY);
                if (moveX != 0f || moveY != 0f)
                {
                    this.inputTick++;
                    this.client.Session?.SendInput(this.inputTick, moveX, moveY);
                    this.predictor?.RecordInput(this.inputTick, moveX, moveY);
                }
            }

            // Polls the merged world every frame — despawn falls out of absence. This is the ONE
            // feed into the adapter; see the class remarks on SetStateAtTick.
            this.binder.Tick(this.client.World, this.client.UserId);
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

            if (this.installed && this.world is { IsCreated: true })
            {
                // Reverse of install. Prediction first (hands claimed transforms back), then the
                // adapter with its mirrors (one NetworkEntityDespawned per id, reason Teardown),
                // then every session-scoped module in reverse install order, then the catalog
                // singleton — the blob is freed below, after nothing reads it.
                DotsPredictionBootstrap.Uninstall(this.world);
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
            };

            this.library = ScriptableObject.CreateInstance<ViewArchetypeLibrary>();
            this.library.name = "PlaceholderViewLibrary";
            this.library.Configure(
                new ViewArchetypeLibrary.Entry { Name = DotsViewArchetypes.PlayerLocal, Config = this.configs[0] },
                new ViewArchetypeLibrary.Entry { Name = DotsViewArchetypes.PlayerRemote, Config = this.configs[1] },
                new ViewArchetypeLibrary.Entry { Name = DotsViewArchetypes.Mob, Config = this.configs[2] });
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
