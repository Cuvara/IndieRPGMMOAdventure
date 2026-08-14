using Unity.Entities;

namespace Cuvara.DOTS.Groups
{
    /// <summary>
    /// Everything that decides whether an entity still exists: death resolution, then lifetime
    /// expiry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// After <see cref="MovementSystemGroup"/>, so a death is resolved against the position the
    /// entity actually reached this frame rather than the one it had at the start of it.
    /// </para>
    /// <para>
    /// Its systems destroy entities, and they do it through
    /// <see cref="DotsEndSimulationCommandBufferSystem"/> — which plays back at the end of
    /// <see cref="GameplaySystemGroup"/>, still before <c>TransformSystemGroup</c> and well before
    /// presentation. That is what guarantees no view is ever synced against an entity that died this
    /// frame.
    /// </para>
    /// <para><b>Empty in this version</b>; declared now so its position is fixed.</para>
    /// </remarks>
    [DisableAutoCreation]
    [UpdateInGroup(typeof(GameplaySystemGroup))]
    [UpdateAfter(typeof(MovementSystemGroup))]
    public partial class LifecycleSystemGroup : ComponentSystemGroup
    {
    }
}
