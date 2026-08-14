using System.Collections.Generic;

namespace Cuvara.DOTS.Provisioning
{
    /// <summary>
    /// Tears down the views standing on a set of asset keys, before those assets are released.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The seam that lets <see cref="ChunkViewProvisioner"/> cascade a chunk unload into the view
    /// layer without knowing what a view, an entity or a world is. Provisioning is the lower layer;
    /// the dependency points this way so it stays that way.
    /// </para>
    /// <para>
    /// Implementations must reuse the ordinary despawn path — recycle the instance, drop the handle,
    /// clear the link — rather than writing a parallel teardown. A second teardown path is how the
    /// registry and the links drift apart, which is the failure this whole mechanism exists to
    /// prevent.
    /// </para>
    /// </remarks>
    public interface IViewCascadeSink
    {
        /// <summary>
        /// Despawns every live view spawned from any of <paramref name="keys"/>.
        /// </summary>
        /// <returns>How many views were despawned.</returns>
        int CascadeDespawn(IReadOnlyCollection<string> keys);
    }
}
