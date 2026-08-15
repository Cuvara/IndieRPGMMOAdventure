using Unity.Entities;

namespace Cuvara.DOTS.Netcode
{
    /// <summary>
    /// Singleton pointing at the session's <see cref="DotsEntityView"/>, so the drain system can
    /// reach its queue.
    /// </summary>
    /// <remarks>
    /// A managed component for the same reason <c>EntityViewRegistryReference</c> is one: the thing
    /// it points at is a managed object with a managed queue, and there is no unmanaged shape for
    /// that. It is reached through <c>SystemAPI.ManagedAPI.GetSingleton</c>, which is what stops the
    /// drain system from being Bursted — that is a property of the queue, not a choice made here.
    /// </remarks>
    public sealed class NetworkEntityViewReference : IComponentData
    {
        public DotsEntityView View;
    }
}
