using Cuvara.Netcode.Auth;
using Scripts.Nakama.Auth;
using Scripts.Nakama.Social;
using VContainer;

namespace Scripts.Nakama.DI
{
    /// <summary>
    /// Registers Nakama services in a VContainer scope.
    /// </summary>
    /// <remarks>
    /// Call from <c>GameLifetimeScope.Configure</c> so the session outlives
    /// scene loads — an auth token must survive scene transitions.
    /// Registers <see cref="NakamaAuthProvider"/> as <see cref="IAuthProvider"/>,
    /// which <c>NetworkClient</c> picks up via DI for its
    /// <c>ConnectAsync(mapId, ct)</c> overload, and <see cref="PartyService"/>, whose party
    /// id is what <c>NetworkClient.ConnectToDungeonAsync</c> takes.
    /// </remarks>
    public static class NakamaRegistration
    {
        public static IContainerBuilder RegisterNakama(
            this IContainerBuilder builder,
            NakamaSettings settings = null)
        {
            builder.RegisterInstance(settings ?? new NakamaSettings());
            builder.Register<NakamaSessionService>(Lifetime.Singleton);
            builder.Register<NakamaAuthProvider>(Lifetime.Singleton).As<IAuthProvider>();

            // Registered as its own type, not behind an interface: nothing in the netcode
            // package knows what a party is, and inventing an interface here would be an
            // abstraction over exactly one implementation with no second caller in sight.
            // Singleton because a player is in at most one party and every caller must see
            // the same one.
            builder.Register<PartyService>(Lifetime.Singleton);
            return builder;
        }
    }
}
