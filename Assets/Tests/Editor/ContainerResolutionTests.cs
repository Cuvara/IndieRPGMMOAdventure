namespace Tests.Editor
{
    using Cuvara.Netcode.Auth;
    using Cuvara.Netcode.Client;
    using Cuvara.Netcode.DI;
    using NUnit.Framework;
    using Scripts.DI;
    using Scripts.Gameplay.Content;
    using Scripts.Nakama;
    using Scripts.Nakama.Characters;
    using Scripts.Nakama.DI;
    using Scripts.Nakama.Social;
    using VContainer;

    /// <summary>
    /// Builds the registrations <c>GameLifetimeScope</c> and <c>MainSceneScope</c> make and
    /// resolves every service they register. A type with two public constructors and no
    /// <c>[Inject]</c> makes VContainer pick the one with the most parameters, and a test-only
    /// constructor taking an unregistered type then breaks the player at join time while every
    /// unit test, which news the type up directly, stays green.
    /// </summary>
    /// <remarks>
    /// The same <c>Register*</c> calls rather than the scope components: a scope builds in
    /// <c>Awake</c> and needs a scene. The two entry points are registered as plain types, so
    /// resolving them does not run <c>Start</c> (which would authenticate against a backend).
    /// The DOTS view layer is left out; <c>DotsRegistrationTests</c> covers it.
    /// </remarks>
    public sealed class ContainerResolutionTests
    {
        private IObjectResolver container;

        [SetUp]
        public void SetUp()
        {
            var builder = new ContainerBuilder();
            builder.RegisterInstance(new BackendSettings());
            builder.RegisterNetworking(new NetworkSettings());
            // A pin, so the constructor builds the pinned adapter: the default Unity adapter creates a
            // DontDestroyOnLoad object, which throws outside play mode. Either way it is the same
            // production constructor VContainer must choose.
            builder.RegisterNakama(new NakamaSettings { PinnedCertificate = new byte[] { 1, 2, 3 } });
            builder.Register<GameContentService>(Lifetime.Singleton);
            builder.Register<MainSessionDriver>(Lifetime.Scoped);
            builder.Register<GameplayPanelDriver>(Lifetime.Scoped);
            this.container = builder.Build();
        }

        [TearDown]
        public void TearDown() => this.container?.Dispose();

        [Test]
        public void NakamaSessionService_Resolves_ThroughItsProductionConstructor()
        {
            Assert.That(this.container.Resolve<NakamaSessionService>(), Is.Not.Null);
        }

        [Test]
        public void AuthProvider_Resolves_AsNakamaAuthProvider()
        {
            Assert.That(this.container.Resolve<IAuthProvider>(), Is.InstanceOf<Scripts.Nakama.Auth.NakamaAuthProvider>());
        }

        [Test]
        public void EveryRegisteredService_Resolves()
        {
            Assert.That(this.container.Resolve<NetworkClient>(), Is.Not.Null);
            Assert.That(this.container.Resolve<BackendSettings>(), Is.Not.Null);
            Assert.That(this.container.Resolve<CharacterSelectionState>(), Is.Not.Null);
            Assert.That(this.container.Resolve<CharacterService>(), Is.Not.Null);
            Assert.That(this.container.Resolve<PartyService>(), Is.Not.Null);
            Assert.That(this.container.Resolve<GameContentService>(), Is.Not.Null);
            Assert.That(this.container.Resolve<MainSessionDriver>(), Is.Not.Null);
            Assert.That(this.container.Resolve<GameplayPanelDriver>(), Is.Not.Null);
        }
    }
}
