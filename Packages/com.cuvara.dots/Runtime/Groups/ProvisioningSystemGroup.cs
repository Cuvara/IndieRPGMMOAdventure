using Unity.Entities;

namespace Cuvara.DOTS.Groups
{
    /// <summary>
    /// Asset and view provisioning: draining completed async loads, prewarming, releasing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Runs after <see cref="NetcodeSystemGroup"/> and before everything else, because a load that
    /// finished between frames must be visible to every system that reads the result this frame. A
    /// drain that ran later would leave prefabs "not warm yet" for one more frame than they actually
    /// were, and <c>EntityViewSpawnSystem</c> would defer spawns it could have served.
    /// </para>
    /// <para>
    /// <b>Empty in this version.</b> <c>ChunkViewProvisioner</c> is driven by awaited
    /// <see cref="System.Threading.Tasks.Task"/>s from the consumer's own loading code, not by a
    /// system, so there is nothing to drain yet. The group is declared now so the position is fixed
    /// before anyone orders against it — a drain system that appeared later and shifted the phase
    /// would silently invalidate a consumer's <c>[UpdateAfter]</c>.
    /// </para>
    /// </remarks>
    [DisableAutoCreation]
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(NetcodeSystemGroup))]
    public partial class ProvisioningSystemGroup : ComponentSystemGroup
    {
    }
}
