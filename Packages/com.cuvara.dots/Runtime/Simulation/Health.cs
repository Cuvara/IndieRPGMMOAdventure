using Unity.Entities;

namespace Cuvara.DOTS.Simulation
{
    /// <summary>
    /// Hit points. An entity whose <see cref="Current"/> reaches zero or below is destroyed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Health means "destroy at zero" in this package.</b> There is no tag to filter on and no
    /// policy field: an entity that must survive zero should not carry <see cref="Health"/>, or its
    /// owner should clamp <see cref="Current"/> above zero before
    /// <see cref="Cuvara.DOTS.Groups.LifecycleSystemGroup"/> runs. The reference implementation this
    /// was lifted from filtered on a game-specific enemy tag; a package cannot know that tag, and
    /// inventing one here would put a game's vocabulary into a shared assembly.
    /// </para>
    /// <para>
    /// <see cref="Max"/> is carried for consumers and UI. Nothing in the package reads it — no
    /// clamping, no regeneration, no percentage — because every one of those is a game rule.
    /// </para>
    /// </remarks>
    public struct Health : IComponentData
    {
        public int Current;

        /// <summary>Upper bound, for consumers. The package never reads or enforces it.</summary>
        public int Max;
    }
}
