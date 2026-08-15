namespace Cuvara.DOTS.Netcode
{
    /// <summary>
    /// Decides which <see cref="Cuvara.DOTS.Configuration.ViewArchetypeLibrary"/> archetype a
    /// replicated entity should be presented as.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The seam exists because "what kind is this" and "what does that look like" are different
    /// questions with different owners.</b> The server owns the first and answers it on the wire as
    /// <c>NetworkEntityDescriptor.Type</c>. The art pipeline owns the second, and answers it in a
    /// <c>ViewArchetypeLibrary</c>. This interface is the join, and it is a seam rather than a
    /// dictionary because the join is rarely one-to-one: a <c>"mob"</c> is a goblin or a dragon
    /// depending on something the view layer has no business hardcoding.
    /// </para>
    /// <para>
    /// <b>It is no longer a workaround.</b> Until netcode 0.4.0, <c>IEntityView.Spawn</c> passed
    /// only <c>(id, isLocal)</c> and this interface existed to hold a decoding rule the presentation
    /// layer had to invent — matching an <c>"enemy-"</c> id prefix. That is over: the kind crosses
    /// the wire on every snapshot, keyframe and delta alike, so a resolver reads it instead of
    /// guessing it, and <c>PrefixArchetypeResolver</c> has been deleted rather than kept as a
    /// fallback. See <see cref="TypeArchetypeResolver"/> for the built-in implementation.
    /// </para>
    /// </remarks>
    public interface INetworkArchetypeResolver
    {
        /// <summary>
        /// Names the archetype for a replicated entity, as authored in the
        /// <see cref="Cuvara.DOTS.Configuration.ViewArchetypeLibrary"/>.
        /// </summary>
        /// <returns>
        /// False when this entity should not be presented at all. The adapter then skips the spawn
        /// entirely rather than inventing a fallback archetype — an entity nobody configured a look
        /// for is better invisible than rendered as something it is not. An implementation returning
        /// false for a kind it did not recognise should say so in the log itself; the adapter treats
        /// false as a decision, not as an error.
        /// </returns>
        bool TryResolve(in NetworkEntityDescriptor entity, out string archetypeName);
    }
}
