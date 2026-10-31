namespace Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Cuvara.Netcode.Auth;
    using Cuvara.Netcode.Client;
    using Cuvara.Netcode.Transport;
    using NUnit.Framework;
    using Scripts.DI;
    using Scripts.Gameplay.Content;
    using Scripts.Nakama;
    using Scripts.Nakama.Characters;
    using Scripts.Nakama.Social;
    using Scripts.Session;
    using VContainer;
    using VContainer.Unity;
#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER
    using Cuvara.DOTS.Provisioning;
    using Cuvara.DOTS.Simulation;
    using Cuvara.DOTS.Views;
    using Scripts.DI.Dots;
    using Unity.Entities;
    using UnityEngine;
#endif
#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER && CUVARA_NETCODE && CUVARA_SHARED_GAMELOGIC
    using Cuvara.Netcode.Prediction;
#endif

    /// <summary>
    /// Builds the production composition — <see cref="ClientRootInstaller"/> for the root scope and
    /// <see cref="MainSceneInstaller"/> for a child scope, the same functions
    /// <c>GameLifetimeScope</c> and <c>MainSceneScope</c> call — and resolves every service and
    /// entry point they register. A type with two public constructors and no <c>[Inject]</c>
    /// makes VContainer pick the one with the most parameters, and a test-only constructor taking
    /// an unregistered type then breaks the player at join time while every unit test, which news
    /// the type up directly, stays green.
    /// </summary>
    /// <remarks>
    /// Only the inputs are substituted: localhost settings, a throwaway <c>World</c>, a fake prefab
    /// loader and an empty view library. The entry points are registered as in production, but
    /// their <c>Start</c>/<c>Tick</c> run on the player loop, which an EditMode test never pumps,
    /// so nothing authenticates against a backend.
    /// </remarks>
    public sealed class ContainerResolutionTests
    {
        private IObjectResolver root;
        private IObjectResolver scene;

#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER
        private World world;
        private DotsViewLibraryAsset library;
#endif

        private static RootInstallerArgs TestArgs()
        {
            var settings = BackendCommandLine.Resolve(
                Array.Empty<string>(),
                _ => null,
                "127.0.0.1",
                8000,
                "map_01",
                "http://127.0.0.1:9101/status");
            return new RootInstallerArgs
            {
                Backend = new BackendSettings { Value = settings },
                // A pin, so the constructor builds the pinned adapter: the default Unity adapter creates a
                // DontDestroyOnLoad object, which throws outside play mode. Either way it is the same
                // production constructor VContainer must choose.
                NakamaPinnedCertificate = new byte[] { 1, 2, 3 },
            };
        }

        [SetUp]
        public void SetUp()
        {
            var args = TestArgs();
#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER
            this.world = new World("ContainerResolutionTests");
            this.library = ScriptableObject.CreateInstance<DotsViewLibraryAsset>();
            args.World = this.world;
            args.Library = this.library;
            args.Loader = new FakeViewPrefabLoader();
#endif
            var builder = new ContainerBuilder();
            ClientRootInstaller.Install(builder, args);
            this.root = builder.Build();
            this.scene = this.root.CreateScope(MainSceneInstaller.Install);
        }

        [TearDown]
        public void TearDown()
        {
            this.scene?.Dispose();
            this.root?.Dispose();
#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER
            if (this.world is { IsCreated: true })
            {
                DotsViewBootstrap.Uninstall(this.world);
                this.world.Dispose();
            }

            this.world = null;
            if (this.library != null)
            {
                UnityEngine.Object.DestroyImmediate(this.library);
            }
#endif
        }

        [Test]
        public void NakamaSessionService_Resolves_ThroughItsProductionConstructor()
        {
            Assert.That(this.root.Resolve<NakamaSessionService>(), Is.Not.Null);
        }

        [Test]
        public void AuthProvider_Resolves_AsNakamaAuthProvider()
        {
            Assert.That(this.root.Resolve<IAuthProvider>(), Is.InstanceOf<Scripts.Nakama.Auth.NakamaAuthProvider>());
        }

        [Test]
        public void EveryRootService_Resolves()
        {
            Assert.That(this.root.Resolve<NetworkClient>(), Is.Not.Null);
            Assert.That(this.root.Resolve<BackendSettings>(), Is.Not.Null);
            Assert.That(this.root.Resolve<ITransportFactory>(), Is.InstanceOf<DefaultTransportFactory>());
            Assert.That(this.root.Resolve<CharacterSelectionState>(), Is.Not.Null);
            Assert.That(this.root.Resolve<CharacterService>(), Is.Not.Null);
            Assert.That(this.root.Resolve<PartyService>(), Is.Not.Null);
            Assert.That(this.root.Resolve<GameContentService>(), Is.Not.Null);
        }

        [Test]
        public void EverySceneEntryPoint_Resolves_ThroughTheChildScope()
        {
            var startables = this.scene.Resolve<IEnumerable<IStartable>>().Select(s => s.GetType()).ToList();
            Assert.That(startables, Does.Contain(typeof(MainSessionDriver)));
            Assert.That(startables, Does.Contain(typeof(GameplayPanelDriver)));
            Assert.That(this.scene.Resolve<IEnumerable<ITickable>>().Select(t => t.GetType()), Does.Contain(typeof(GameplayPanelDriver)));
        }

#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER
        [Test]
        public void DotsLayer_Resolves_FromTheRootInstaller()
        {
            Assert.That(this.root.Resolve<DotsViewLibraryReference>(), Is.Not.Null);
            Assert.That(this.root.Resolve<IViewAssetProvider>(), Is.InstanceOf<LeasedViewAssetProvider>());
            Assert.That(this.root.Resolve<EntityViewRegistry>(), Is.Not.Null);
            Assert.That(this.root.Resolve<ChunkViewProvisioner>(), Is.Not.Null);
            Assert.That(this.root.Resolve<ISimulationModel>(), Is.Not.Null);
            Assert.That(this.root.Resolve<IEcsWorldProvider>().World, Is.SameAs(this.world),
                "the provider must hand out the world the root installer was given");
#if CUVARA_NETCODE && CUVARA_SHARED_GAMELOGIC
            Assert.That(this.root.Resolve<LocalMovePredictor>(), Is.Not.Null);
#endif
        }
#endif

        /// <summary>
        /// Guard: a registration dropped from an installer fails this test by name. The lists are
        /// the old hand-written SetUp (root: networking, Nakama, content, drivers) plus the DOTS
        /// layer that SetUp never covered.
        /// </summary>
        [Test]
        public void Installers_Register_EveryExpectedProductionType()
        {
            var rootBuilder = new ContainerBuilder();
            ClientRootInstaller.Install(rootBuilder, TestArgs().WithTestDots());

            var expectedRoot = new List<Type>
            {
                typeof(BackendSettings),
                typeof(IGameplayInputSource),
                typeof(GameplayInputRouter),
                typeof(NetworkClient),
                typeof(IAuthProvider),
                typeof(ITransportFactory),
                typeof(NakamaSessionService),
                typeof(CharacterSelectionState),
                typeof(CharacterService),
                typeof(PartyService),
                typeof(GameContentService),
#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER
                typeof(DotsViewLibraryReference),
                typeof(IViewAssetProvider),
                typeof(EntityViewRegistry),
                typeof(ChunkViewProvisioner),
                typeof(ISimulationModel),
                typeof(IEcsWorldProvider),
#if CUVARA_NETCODE && CUVARA_SHARED_GAMELOGIC
                typeof(LocalMovePredictor),
#endif
#endif
            };
            var missingRoot = expectedRoot.Where(t => !rootBuilder.Exists(t, true)).Select(t => t.Name).ToList();
            Assert.That(missingRoot, Is.Empty, "ClientRootInstaller no longer registers: " + string.Join(", ", missingRoot));

            var sceneBuilder = new ContainerBuilder();
            MainSceneInstaller.Install(sceneBuilder);
            var expectedScene = new[]
            {
                typeof(MainSessionDriver),
                typeof(GameplayPanelDriver),
                typeof(IStartable),
                typeof(ITickable),
            };
            var missingScene = expectedScene.Where(t => !sceneBuilder.Exists(t, true)).Select(t => t.Name).ToList();
            Assert.That(missingScene, Is.Empty, "MainSceneInstaller no longer registers: " + string.Join(", ", missingScene));
        }
    }

    internal static class RootInstallerArgsTestExtensions
    {
        /// <summary>The builder-only guard never builds, but RegisterDots still reads the library, so give it one.</summary>
        public static RootInstallerArgs WithTestDots(this RootInstallerArgs args)
        {
#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER
            args.Mode = DotsViewProviderMode.Primitive;
#endif
            return args;
        }
    }
}
