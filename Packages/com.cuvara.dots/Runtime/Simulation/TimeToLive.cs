using Unity.Entities;

namespace Cuvara.DOTS.Simulation
{
    /// <summary>
    /// Countdown to destruction. The entity is destroyed once <see cref="Remaining"/> reaches zero.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Carrying this component is the opt-in: nothing gives an entity a lifetime implicitly, and
    /// removing it stops the countdown without the entity ever being destroyed. The destruction is
    /// recorded into the package's own command buffer rather than applied inline — see
    /// <see cref="TimeToLiveSystem"/>.
    /// </para>
    /// <para>
    /// <b>Named <c>TimeToLive</c>, not <c>Lifetime</c>.</b> The reference implementation calls it
    /// <c>Lifetime</c>, but <c>VContainer.Lifetime</c> is core vocabulary in the DI framework this
    /// package supports, and the two collided the moment both namespaces were imported into one
    /// file — inside <c>Cuvara.DOTS.DI</c>, where nothing about this component is visible. A common
    /// word in a shared assembly is a name that will collide again.
    /// </para>
    /// </remarks>
    public struct TimeToLive : IComponentData
    {
        /// <summary>Seconds left. Counted down by <see cref="TimeToLiveSystem"/> in simulation time.</summary>
        public float Remaining;
    }
}
