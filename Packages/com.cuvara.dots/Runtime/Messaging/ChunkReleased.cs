namespace Cuvara.DOTS.Messaging
{
    /// <summary>
    /// A chunk release completed. <see cref="Released"/> is false only for an unknown chunk id.
    /// </summary>
    /// <remarks>
    /// The coarse "a chunk went away" signal. <see cref="ChunkCascadeReleased"/> is the one that
    /// matters when views were torn down with it; this fires either way.
    /// </remarks>
    public readonly struct ChunkReleased
    {
        public readonly string ChunkId;
        public readonly bool Released;

        /// <summary>Views the release cascaded away; 0 when nothing was standing on its keys.</summary>
        public readonly int ViewsDespawned;

        public ChunkReleased(string chunkId, bool released, int viewsDespawned)
        {
            ChunkId = chunkId;
            Released = released;
            ViewsDespawned = viewsDespawned;
        }
    }
}
