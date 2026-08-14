namespace Cuvara.DOTS.Provisioning
{
    /// <summary>
    /// Outcome of <see cref="ChunkViewProvisioner.ReleaseChunk"/>.
    /// </summary>
    /// <remarks>
    /// A struct rather than a <c>bool</c> because a caller has to be able to tell a release from a
    /// no-op on an unknown chunk, and because <see cref="ViewsDespawned"/> is the number that says
    /// whether anything on screen just went away.
    /// </remarks>
    public readonly struct ChunkReleaseResult
    {
        /// <summary>The chunk's references were dropped.</summary>
        public readonly bool Released;

        /// <summary>The chunk was known to the provisioner when the call was made.</summary>
        public readonly bool WasTracked;

        /// <summary>
        /// Views the cascade despawned. Their entities survive without views; see
        /// <see cref="Messaging.ChunkCascadeReleased"/>.
        /// </summary>
        public readonly int ViewsDespawned;

        /// <summary>Distinct keys actually released, i.e. those whose reference count reached zero.</summary>
        public readonly int KeysReleased;

        public ChunkReleaseResult(bool released, bool wasTracked, int viewsDespawned, int keysReleased)
        {
            Released = released;
            WasTracked = wasTracked;
            ViewsDespawned = viewsDespawned;
            KeysReleased = keysReleased;
        }
    }
}
