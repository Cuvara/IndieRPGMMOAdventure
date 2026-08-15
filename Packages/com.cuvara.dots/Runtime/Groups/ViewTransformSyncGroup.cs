using Unity.Entities;

namespace Cuvara.DOTS.Groups
{
    /// <summary>
    /// Read-only half of the view layer: ECS transforms are copied onto GameObjects here.
    /// </summary>
    /// <remarks>
    /// Runs after <see cref="ViewLifecycleGroup"/>, so this frame's new views are positioned in the
    /// frame they appear rather than sitting at their spawn pose until the next one, and no view it
    /// touches is about to be recycled. Nothing in this group makes structural changes, which is what
    /// lets it stay a pure read plus a managed write.
    /// </remarks>
    [DisableAutoCreation]
    [UpdateInGroup(typeof(ViewSystemGroup))]
    [UpdateAfter(typeof(ViewLifecycleGroup))]
    public partial class ViewTransformSyncGroup : ComponentSystemGroup
    {
    }
}
