using Unity.Entities;
using Unity.Transforms;

namespace Cuvara.DOTS.Groups
{
    /// <summary>
    /// All package gameplay simulation, positioned so that everything it writes is picked up by the
    /// transform systems in the same frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>UpdateBefore(TransformSystemGroup)</c> is the load-bearing part: both groups sit directly
    /// inside <see cref="SimulationSystemGroup"/>, so this is a legal same-parent ordering, and it
    /// guarantees a position written this frame is baked into <c>LocalToWorld</c> this frame rather
    /// than next.
    /// </para>
    /// <para>
    /// <b>No <c>FixedStepSimulationSystemGroup</c>, deliberately.</b> The server is authoritative and
    /// the client integrates nothing of its own, so re-timing server-paced data onto Unity's default
    /// 60 Hz fixed step would add latency and buy no determinism. That changes when prediction lands,
    /// and the rate will then derive from the server tick rate — never from Unity's default.
    /// </para>
    /// <para>
    /// The shape of this group is identical whether or not <c>com.rpgmmo.shared-gamelogic</c> is
    /// installed. A layout that changed with an optional dependency would make a consumer's
    /// <c>[UpdateAfter]</c> conditionally meaningless.
    /// </para>
    /// </remarks>
    [DisableAutoCreation]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public partial class GameplaySystemGroup : ComponentSystemGroup
    {
    }
}
