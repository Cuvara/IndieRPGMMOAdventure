#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER && CUVARA_NETCODE && CUVARA_SHARED_GAMELOGIC
namespace Scripts.DI.Dots
{
    using Cuvara.DOTS.Configuration;
    using Cuvara.DOTS.Netcode;
    using Cuvara.DOTS.Netcode.Prediction;
    using Cuvara.DOTS.Provisioning;
    using Cuvara.DOTS.Simulation;
    using Cuvara.Netcode.Client;
    using Cuvara.Netcode.Prediction;
    using Cuvara.Netcode.View;
    using Unity.Entities;
    using UnityEngine;
    using VContainer;

    /// <summary>
    /// The per-session half of the DOTS wiring: hangs the <c>com.cuvara.dots</c> netcode adapter
    /// and prediction driver off the same <c>NetworkClient</c> the game already registers, and
    /// ticks the binder that feeds it. Place one in the gameplay scene;
    /// <c>MainSceneScope</c> injects it the way <c>GameLifetimeScope</c> injects
    /// <c>NetworkBootstrap</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The root scope (<c>RegisterDots</c>) owns what outlives a scene: registry, pools,
    /// provisioner, predictor, the view bootstrap. This component owns what belongs to one
    /// session in one scene: the catalog, the archetype resolver, the adapter, the netcode and
    /// prediction bootstraps, and the binder tick. Teardown mirrors install in reverse —
    /// prediction, then netcode, then the mirrored entities and the catalog. The view bootstrap
    /// is deliberately NOT uninstalled here: it is root-scoped and other scenes' views stand on
    /// it.
    /// </para>
    /// <para>
    /// <b>One input owner.</b> When <see cref="driveInput"/> is on, this component samples input,
    /// sends it, and records the same tick on the predictor — recorded and sent must be the same
    /// stream or replay diverges by construction. <c>NetworkBootstrap</c>'s
    /// <c>SendSyntheticInput</c> must then be OFF in the scene's config: two senders interleave
    /// ticks and the server integrates input the predictor never saw.
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

        private NetworkClient client;
        private LocalMovePredictor predictor;
        private IViewAssetProvider viewAssetProvider;

        private World world;
        private ViewConfigCatalog catalog;
        private ViewArchetypeLibrary library;
        private ViewConfig[] configs;
        private DotsEntityView view;
        private WorldViewBinder binder;
        private bool installed;
        private long inputTick;

        /// <summary>Called by the scene scope's build callback; absent a container this component stays inert.</summary>
        [Inject]
        public void Construct(NetworkClient networkClient, LocalMovePredictor movePredictor, IViewAssetProvider assetProvider)
        {
            this.client = networkClient;
            this.predictor = movePredictor;
            this.viewAssetProvider = assetProvider;
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

            // Simulation systems are idempotent to install and usable without views; the view
            // bootstrap itself was installed by RegisterDots at root-container build.
            DotsSimulationBootstrap.InstallSimulationSystems(this.world);

            this.BuildCatalog();
            this.catalog.Install(this.world);

            // Prewarm from the catalog's own pool sizes rather than numbers typed here.
            foreach (var pair in this.catalog.PoolSizesByKey())
            {
                this.viewAssetProvider?.PrewarmAsync(pair.Key, pair.Value).GetAwaiter().GetResult();
            }

            // Kind comes from the wire, never from the id. No catch-all: an unmapped server kind
            // is refused and logged once, so a server that grows a new type says so instead of
            // rendering it as a player.
            var resolver = new TypeArchetypeResolver(
                localArchetype: DotsViewArchetypes.PlayerLocal,
                unknownArchetype: null,
                new TypeArchetypeResolver.Rule(DotsViewArchetypes.ServerKindPlayer, DotsViewArchetypes.PlayerRemote),
                new TypeArchetypeResolver.Rule(DotsViewArchetypes.ServerKindMob, DotsViewArchetypes.Mob));

            // XZPlane: the server's 2D plane is Unity's ground plane. Per-art lift belongs in
            // ViewConfig.PositionOffset, not in the mapping. Wire hp lands on NetworkEntityState
            // (writeHealth defaults to false) so no client-side system destroys an entity the
            // server still lists.
            this.view = new DotsEntityView(this.catalog, resolver, SnapshotSpaceMapping.XZPlane);
            DotsNetcodeBootstrap.Install(this.world, this.view);

            this.binder = new WorldViewBinder(this.view);

            if (this.predictor != null)
            {
                DotsPredictionBootstrap.Install(this.world, this.predictor, this.client.World);
            }

            this.installed = true;
            return true;
        }

        private void OnDestroy()
        {
            if (this.installed && this.world is { IsCreated: true })
            {
                // Reverse of install. Prediction first (hands claimed transforms back), then the
                // adapter, then the session's mirrored entities — netcode's Uninstall leaves them
                // to the consumer, and this world outlives the scene, so leaving them would leak
                // a ghost per replicated entity into the next session.
                DotsPredictionBootstrap.Uninstall(this.world);
                DotsNetcodeBootstrap.Uninstall(this.world);

                var entityManager = this.world.EntityManager;
                using var mirrors = entityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkEntity>());
                entityManager.DestroyEntity(mirrors);
            }

            // After the systems that read the blob are gone, never before — a disposed catalog
            // under a live spawn system is a dangling blob pointer.
            this.catalog?.Dispose();
            this.catalog = null;

            if (this.library != null)
            {
                Destroy(this.library);
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
        /// Builds the catalog in code, the way the package's NetworkedPrediction sample does:
        /// there is no authored art yet, so there are no ViewConfig assets to load. When art
        /// exists, author ViewConfig assets, list them in a ViewArchetypeLibrary asset, and
        /// replace this method with a serialized reference to it.
        /// </summary>
        private void BuildCatalog()
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
            this.library.Configure(
                new ViewArchetypeLibrary.Entry { Name = DotsViewArchetypes.PlayerLocal, Config = this.configs[0] },
                new ViewArchetypeLibrary.Entry { Name = DotsViewArchetypes.PlayerRemote, Config = this.configs[1] },
                new ViewArchetypeLibrary.Entry { Name = DotsViewArchetypes.Mob, Config = this.configs[2] });

            this.catalog = new ViewConfigCatalog();
            this.catalog.Build(this.library);
        }
    }
}
#endif
