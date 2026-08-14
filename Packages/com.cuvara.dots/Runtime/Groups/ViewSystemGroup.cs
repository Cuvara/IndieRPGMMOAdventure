using Unity.Entities;

namespace Cuvara.DOTS.Groups
{
    /// <summary>
    /// Turns entities into visible GameObjects: spawn, despawn, transform sync.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a sync in presentation is not one frame stale.</b> The reasoning is worth stating
    /// rather than leaving to be re-derived: <c>TransformSystemGroup</c> lives at the end of
    /// <see cref="SimulationSystemGroup"/>, and the root player loop runs
    /// <see cref="SimulationSystemGroup"/> before <see cref="PresentationSystemGroup"/>. So by the
    /// time anything in this group runs, <c>LocalToWorld</c> already holds values computed from this
    /// frame's simulation. Putting the sync inside <see cref="SimulationSystemGroup"/> instead is
    /// what would make it stale, because it would race the transform systems it depends on.
    /// </para>
    /// <para>
    /// There is deliberately no <c>[UpdateAfter(typeof(TransformSystemGroup))]</c> on this group:
    /// that would name a system under a different parent, which Entities ignores with a warning. The
    /// guarantee comes from the group nesting, not from an attribute.
    /// </para>
    /// <para>
    /// <b>This group, not its members, is the package's ordering contract.</b> Consumers write
    /// <c>[UpdateAfter(typeof(ViewSystemGroup))]</c>; the systems inside are <c>internal</c> and
    /// will change.
    /// </para>
    /// </remarks>
    [DisableAutoCreation]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class ViewSystemGroup : ComponentSystemGroup
    {
    }
}
