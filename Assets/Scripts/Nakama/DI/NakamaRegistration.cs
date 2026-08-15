using Cuvara.Netcode.Auth;
using Scripts.Nakama.Auth;
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
    /// <c>ConnectAsync(mapId, ct)</c> overload.
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
            return builder;
        }
    }
}
