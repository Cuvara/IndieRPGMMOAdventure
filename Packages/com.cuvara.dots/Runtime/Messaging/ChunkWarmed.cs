namespace Cuvara.DOTS.Messaging
{
    /// <summary>Every key a chunk asked for has finished loading.</summary>
    /// <remarks>
    /// Published after the awaited prewarm completes, not when it is requested — the point of the
    /// message is that spawning from this chunk will no longer be deferred.
    /// </remarks>
    public readonly struct ChunkWarmed
    {
        public readonly string ChunkId;

        /// <summary>Number of distinct keys the chunk holds references on.</summary>
        public readonly int KeyCount;

        public ChunkWarmed(string chunkId, int keyCount)
        {
            ChunkId = chunkId;
            KeyCount = keyCount;
        }
    }
}
