using Unity.Entities;

namespace Cuvara.DOTS.Groups
{
    /// <summary>
    /// Structural half of the view layer: views appear and disappear here, and nowhere else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Despawn runs before spawn.</b> Recycling a dead entity's view before this frame's new ones
    /// are spawned lets the pool hand the freed instance straight back, so a frame that destroys ten
    /// entities and creates ten more instantiates nothing. The reverse order also works; it just
    /// grows the pool to the sum of both instead of the maximum.
    /// </para>
    /// <para>
    /// Separate from <see cref="ViewTransformSyncGroup"/> so a consumer can inject work between
    /// "views exist" and "views are positioned" — reparenting, per-view initialisation, anything that
    /// wants the instance but must run before it is placed — without naming a package system.
    /// </para>
    /// <para>
    /// Both structural changes sit here rather than in initialization or simulation: they cost a sync
    /// point wherever they run, and in presentation that lands after the frame's simulation work is
    /// already done. It also keeps view lifetime in the same frame as its cause.
    /// </para>
    /// </remarks>
    [DisableAutoCreation]
    [UpdateInGroup(typeof(ViewSystemGroup))]
    public partial class ViewLifecycleGroup : ComponentSystemGroup
    {
    }
}
