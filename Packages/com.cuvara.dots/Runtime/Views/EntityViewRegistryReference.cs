using Unity.Entities;

namespace Cuvara.DOTS.Views
{
    /// <summary>
    /// Managed singleton component carrying the world's <see cref="EntityViewRegistry"/>.
    /// </summary>
    /// <remarks>
    /// A class <see cref="IComponentData"/> is how an <c>ISystem</c> struct reaches a managed
    /// object without a static. Systems fetch it with
    /// <c>SystemAPI.ManagedAPI.GetSingleton&lt;EntityViewRegistryReference&gt;()</c>. Installed by
    /// <see cref="DotsViewBootstrap"/>, which is what the VContainer extension calls — the DI
    /// container owns the registry's lifetime, the world only borrows it.
    /// </remarks>
    public sealed class EntityViewRegistryReference : IComponentData
    {
        public EntityViewRegistry Registry;
    }
}
