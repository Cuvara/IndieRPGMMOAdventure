using Unity.Entities;

namespace Cuvara.DOTS.Netcode
{
    /// <summary>
    /// "Something else owns this entity's <c>LocalTransform</c>." The snapshot adapter stops writing
    /// it and writes only <see cref="ReconciliationAnchor"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Presence, not a flag on an existing component</b>, for the reason
    /// <c>ViewConfigRef</c> already states: a <c>bool</c> field has a default value, and a default
    /// that means "predicted" or "not predicted" is a decision made silently for every entity that
    /// never set it. Presence has no such failure mode — either something claimed the transform or
    /// nothing did.
    /// </para>
    /// <para>
    /// <b>Added by the predictor, never by this package.</b> The adapter cannot know which entities
    /// are predicted: today none are, and when prediction lands it may predict only the local player,
    /// or the local player plus its projectiles, or nothing during a spectate. Keying off
    /// <c>NetworkEntity.IsLocal</c> instead was the obvious shortcut and is wrong in a way that bites
    /// immediately — with no predictor installed the local avatar would simply stop moving.
    /// </para>
    /// <para>
    /// <b>There is no gap at spawn.</b> The adapter creates the entity and positions it before
    /// anything can add this, so an entity is placed correctly on its first frame and only then
    /// handed over. A predictor adding the tag mid-life takes over from a known-good position rather
    /// than from the origin.
    /// </para>
    /// <para>
    /// Removing it hands the transform back: the next state applies to <c>LocalTransform</c> again.
    /// That is what a predictor should do when it stops predicting an entity — on a spectate, or
    /// when the local player dies — rather than leaving a transform nobody writes.
    /// </para>
    /// </remarks>
    public struct PredictedTransform : IComponentData
    {
    }
}
