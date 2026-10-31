#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER
namespace Scripts.DI.Dots
{
    using Unity.Entities;

    /// <summary>
    /// The one place the client asks "which ECS world do I present into?". Registered in the root
    /// scope by <c>ClientRootInstaller</c>; <c>DotsWorldBridge</c> reaches it through its session
    /// installation instead of reading <c>World.DefaultGameObjectInjectionWorld</c> itself.
    /// </summary>
    /// <remarks>
    /// This is a seam for tests and for one named failure, not multi-World support: the client
    /// runs exactly one world, and every implementation is expected to return the same instance
    /// for the lifetime of the container.
    /// </remarks>
    public interface IEcsWorldProvider
    {
        /// <summary>The client's ECS world.</summary>
        /// <exception cref="System.InvalidOperationException">No world exists (yet).</exception>
        World World { get; }
    }
}
#endif
