#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER
namespace Scripts.DI.Dots
{
    using Cuvara.DOTS.DI;
    using Cuvara.DOTS.Messaging;
    using Cuvara.DOTS.Provisioning;
    using Unity.Entities;
    using UnityEngine;
    using VContainer;
#if CUVARA_DOTS_MESSAGEPIPE
    using MessagePipe;
#endif
#if CUVARA_NETCODE && CUVARA_SHARED_GAMELOGIC
    using Cuvara.Netcode.Prediction;
    using Shared.GameLogic.Components;
#endif

    /// <summary>
    /// Root-scope registration for the <c>com.cuvara.dots</c> layer, mirroring
    /// <c>RegisterNetworking()</c> / <c>RegisterNakama()</c>: one extension called from
    /// <c>GameLifetimeScope.Configure</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Order inside this method matters.</b> The package's <c>RegisterDotsViews</c> calls
    /// <c>RegisterDotsMessaging</c>, whose MessagePipe adapters resolve
    /// <c>IPublisher&lt;T&gt;</c> at container build — so MessagePipe's own
    /// <c>RegisterMessagePipe()</c> and a <c>RegisterMessageBroker&lt;T&gt;</c> per package
    /// message type must already be on the builder, or the build fails.
    /// </para>
    /// <para>
    /// Root scope and not scene scope, for the same reason as networking: the view registry,
    /// pools and provisioner outlive a scene load. The per-session pieces — the netcode adapter,
    /// prediction — are scene-scoped and live in <see cref="DotsWorldBridge"/>.
    /// </para>
    /// </remarks>
    public static class DotsRegistration
    {
        /// <summary>
        /// Registers the DOTS view layer, its messaging, the simulation model seam and (when
        /// netcode + shared game logic are present) the session's single
        /// <c>LocalMovePredictor</c>.
        /// </summary>
        /// <param name="viewRoot">
        /// Parent for spawned view instances — pass the scope's own transform so views survive
        /// scene loads with the container that owns them. Null parents to the scene root.
        /// </param>
        /// <param name="world">
        /// The ECS world the view bootstrap installs into. Null means
        /// <c>World.DefaultGameObjectInjectionWorld</c> at container-build time; tests pass a
        /// throwaway world.
        /// </param>
        /// <param name="mode">
        /// <see cref="DotsViewProviderMode.Production"/> (default) leases Addressables prefabs from
        /// the <see cref="DotsViewLibraryAsset"/> into the package's <c>PooledViewAssetProvider</c>;
        /// <see cref="DotsViewProviderMode.Primitive"/> keeps the capsule/sphere placeholder for
        /// the sample and benchmark scenes.
        /// </param>
        /// <param name="library">
        /// The view library. Null loads <c>Resources/DotsViews/DotsViewLibrary</c>; if that is
        /// missing in Production mode the registration logs an error and falls back to the
        /// primitive provider rather than failing every scene's container build.
        /// </param>
        /// <param name="loader">
        /// Prefab loader override — tests pass a fake. Null means Addressables.
        /// </param>
        /// <param name="maxActivePerKey">
        /// The pool's admission budget per key (0 = unlimited). The inactive-pool cap is separate
        /// and comes from the package default; see the package's VIEW-PROVISIONING.md.
        /// </param>
        public static IContainerBuilder RegisterDots(
            this IContainerBuilder builder,
            Transform viewRoot = null,
            World world = null,
            DotsViewProviderMode mode = DotsViewProviderMode.Production,
            DotsViewLibraryAsset library = null,
            IViewPrefabLoader loader = null,
            int maxActivePerKey = 0)
        {
#if CUVARA_DOTS_MESSAGEPIPE
            // MessagePipe first — this is the project's first (and so far only) RegisterMessagePipe
            // call. A future consumer that needs its own message types adds brokers here, not a
            // second RegisterMessagePipe.
            var options = builder.RegisterMessagePipe();
            builder.RegisterMessageBroker<ViewSpawned>(options);
            builder.RegisterMessageBroker<ViewDespawned>(options);
            builder.RegisterMessageBroker<ChunkWarmed>(options);
            builder.RegisterMessageBroker<ChunkReleased>(options);
            builder.RegisterMessageBroker<ChunkCascadeReleased>(options);
#endif

            // The production provider is the package's pooled provider with Addressables prefabs
            // leased in per key (LeasedViewAssetProvider). The primitive provider stays for scenes
            // with no authored art. The choice is made here, once, and the bridge reads it back
            // through DotsViewLibraryReference so it can validate against the right prefab source.
            if (mode == DotsViewProviderMode.Production && library == null)
            {
                library = Resources.Load<DotsViewLibraryAsset>(DotsViewLibraryAsset.ResourcesPath);
                if (library == null)
                {
                    Debug.LogError(
                        $"[DotsRegistration] No DotsViewLibrary asset at '{DotsViewLibraryAsset.DefaultAssetPath}'. " +
                        "Falling back to primitive views. Create it via Assets > Create > Cuvara > DOTS View Library " +
                        "and list every archetype in DotsViewArchetypes.All with an Addressable prefab.");
                    mode = DotsViewProviderMode.Primitive;
                }
            }

            builder.RegisterInstance(new DotsViewLibraryReference { Asset = library, Mode = mode });

            if (mode == DotsViewProviderMode.Production)
            {
                var prefabLoader = loader ?? new AddressableViewPrefabLoader(library);
                // viewRoot is caller-owned: the pool parks inactive instances under it and never
                // destroys it (the package's root-ownership contract). The provider is IDisposable
                // and container-owned, so disposing the root scope destroys every instance and
                // releases every handle — instances before assets.
                builder.Register<IViewAssetProvider>(
                    _ => new LeasedViewAssetProvider(
                        new PooledViewAssetProvider(viewRoot, maxActivePerKey: maxActivePerKey),
                        prefabLoader),
                    Lifetime.Singleton);
            }
            else
            {
                builder.Register<IViewAssetProvider>(_ => new PrimitiveViewAssetProvider(viewRoot), Lifetime.Singleton);
            }

            // Registry + cascade + provisioner, and a build callback that installs
            // DotsViewBootstrap into the world. Calls RegisterDotsMessaging itself, which is why
            // the brokers above must come first.
            builder.RegisterDotsViews(viewRoot, world);

            // SharedGameLogicSimulation when com.rpgmmo.shared-gamelogic is installed (it is;
            // IsAuthoritative == true), PassiveSimulationModel otherwise. The call site is
            // identical either way — that is the seam's whole point.
            builder.RegisterSimulationModel();

#if CUVARA_NETCODE && CUVARA_SHARED_GAMELOGIC
            // The session's single predictor. It MUST be one instance shared between whatever
            // sends input (RecordInput) and DotsPredictionBootstrap — two predictors diverge by
            // construction, which is why it is container-owned rather than constructed where it
            // is used. Tick rate and bounds come from GameConstants — the same source the server
            // compiled against — never from literals here. The speed is a FALLBACK only, used
            // before the first snapshot: the wire carries per-entity speed since netcode 0.8.0
            // and the prediction driver feeds the server's value in on every reconcile, so it
            // does not have to match the server to predict correctly.
            const float fallbackMoveSpeed = 5f;
            builder.Register(
                _ => new LocalMovePredictor(new PredictionSettings(
                    GameConstants.DefaultTickRate,
                    fallbackMoveSpeed,
                    new MapBounds(0f, 0f, GameConstants.DefaultMapWidth, GameConstants.DefaultMapHeight))),
                Lifetime.Singleton);
#endif

            return builder;
        }
    }
}
#endif
