namespace Cuvara.DOTS.Messaging
{
    /// <summary>
    /// A chunk release tore down live views on its way out. The entities survive without views.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Published whenever a release cascades, i.e. whenever <see cref="ViewsDespawned"/> would be
    /// non-zero. Surviving without a view is the intended outcome of a streaming unload — "this
    /// region is no longer present" — but it is a state a consumer cannot infer from anything else,
    /// so shipping it silently would make an entity that has quietly stopped rendering
    /// indistinguishable from a bug. A log line alone is not enough: the code unloading a region is
    /// usually not the code that owns the entities in it.
    /// </para>
    /// </remarks>
    public readonly struct ChunkCascadeReleased
    {
        public readonly string ChunkId;

        /// <summary>Distinct keys the chunk was the last referencer of, i.e. the keys actually released.</summary>
        public readonly int KeyCount;

        /// <summary>Views despawned by the cascade.</summary>
        public readonly int ViewsDespawned;

        public ChunkCascadeReleased(string chunkId, int keyCount, int viewsDespawned)
        {
            ChunkId = chunkId;
            KeyCount = keyCount;
            ViewsDespawned = viewsDespawned;
        }
    }
}
