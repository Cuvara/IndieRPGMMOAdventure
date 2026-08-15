using Unity.Entities;

namespace Cuvara.DOTS.Groups
{
    /// <summary>
    /// Where received bytes become component data: snapshot application and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Split out of <see cref="NetcodeSystemGroup"/> so that prediction can be ordered after
    /// snapshot application <b>without naming a system</b>. The alternative was an
    /// <c>[UpdateAfter]</c> pointing at the internal drain system, which would have required an
    /// <c>InternalsVisibleTo</c> grant and turned an internal name into a cross-assembly ordering
    /// promise — the exact thing this package keeps its systems internal to avoid.
    /// </para>
    /// <para>
    /// Empty in a project without the netcode adapter, which costs an empty update call and keeps
    /// the ordering surface identical whether or not the optional assembly is installed.
    /// </para>
    /// </remarks>
    [DisableAutoCreation]
    [UpdateInGroup(typeof(NetcodeSystemGroup))]
    public partial class SnapshotApplyGroup : ComponentSystemGroup
    {
    }
}
