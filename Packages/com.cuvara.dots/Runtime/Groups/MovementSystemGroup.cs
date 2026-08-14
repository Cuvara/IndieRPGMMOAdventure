using Unity.Entities;

namespace Cuvara.DOTS.Groups
{
    /// <summary>
    /// Everything that changes where an entity is: the simulation-model step first, then the
    /// local movement behaviours.
    /// </summary>
    /// <remarks>
    /// <para>
    /// First inside <see cref="GameplaySystemGroup"/>, expressed as an
    /// <c>[UpdateAfter(MovementSystemGroup)]</c> on <see cref="LifecycleSystemGroup"/> rather than
    /// as <c>OrderFirst</c> here. The two are not interchangeable: Entities sorts <c>OrderFirst</c>
    /// members into a separate batch and then <b>drops</b> any ordering relation between that batch
    /// and a normal member, with a warning. The explicit relation is what actually holds, so it is
    /// the one that is written down.
    /// </para>
    /// <para>
    /// <b>Empty in this version</b> — its systems (the simulation-model step, move-toward, bounce,
    /// spin) land later. Declared now so its position is fixed before anyone orders against it.
    /// </para>
    /// </remarks>
    [DisableAutoCreation]
    [UpdateInGroup(typeof(GameplaySystemGroup))]
    public partial class MovementSystemGroup : ComponentSystemGroup
    {
    }
}
