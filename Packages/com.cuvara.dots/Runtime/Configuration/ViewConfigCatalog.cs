using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Cuvara.DOTS.Configuration
{
    /// <summary>
    /// Builds the <see cref="ViewConfigTable"/> blob from authoring assets, installs it into a world,
    /// and owns its lifetime.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The runtime half of "authoring, not baking": construction happens when a session starts,
    /// against whatever library the consumer hands over, rather than at conversion time against a
    /// subscene that does not exist.
    /// </para>
    /// <para>
    /// Managed and DI-agnostic — a plain constructor and a <see cref="World"/> argument, matching
    /// <c>DotsViewBootstrap</c>. Disposing it releases the blob; the entity holding
    /// <see cref="ViewConfigTableReference"/> is destroyed with it, so nothing is left pointing at
    /// freed memory.
    /// </para>
    /// </remarks>
    public sealed class ViewConfigCatalog : IDisposable
    {
        private readonly Dictionary<string, int> _indexByName = new Dictionary<string, int>();
        private BlobAssetReference<ViewConfigTable> _table;
        private ViewConfigRecord[] _records = Array.Empty<ViewConfigRecord>();

        /// <summary>Number of archetypes in the catalog.</summary>
        public int Count => _records.Length;

        /// <summary>The built blob. Not valid before <see cref="Build"/>.</summary>
        public BlobAssetReference<ViewConfigTable> Table => _table;

        /// <summary>
        /// Builds the blob from a library. A later call replaces the previous blob and disposes it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Only safe to call between frames, and never while a tick is in flight.</b> Rebuilding
        /// frees the previous blob immediately. Entities hold a <see cref="ViewConfigRef"/> index into
        /// that blob, and a system reading the table mid-frame — or a Bursted job holding it — would
        /// then be reading freed memory. That is not a clean exception: a Bursted read of a disposed
        /// blob is undefined behaviour, so it can look like corrupt data or nothing at all rather than
        /// a crash pointing at this line. The caller owns that sequencing; the package cannot detect it.
        /// </para>
        /// <para>
        /// <b>A rebuild also invalidates every index handed out before it.</b> Indices are positions
        /// in the new record list, so an entity still carrying a <see cref="ViewConfigRef"/> from
        /// before a rebuild may now name a different archetype, or none. Re-resolve names to indices
        /// after rebuilding — the spawn path warns and falls back to the request's own key for an
        /// out-of-range index, but an index that is merely *wrong* rather than out of range cannot be
        /// detected.
        /// </para>
        /// <para>
        /// Entries with no config, no name, or a duplicate name are skipped with a warning rather
        /// than throwing: one broken row in an asset should not stop a session from starting, and the
        /// warning names the row so it can be found. A duplicate name would otherwise make which
        /// config wins depend on list order.
        /// </para>
        /// </remarks>
        public void Build(ViewArchetypeLibrary library)
        {
            if (library == null) throw new ArgumentNullException(nameof(library));

            _indexByName.Clear();
            var records = new List<ViewConfigRecord>(library.Entries.Count);

            foreach (var entry in library.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name) || entry.Config == null)
                {
                    Debug.LogWarning($"[Cuvara.DOTS] '{library.name}' has an entry with no name or no config; skipped.");
                    continue;
                }

                if (_indexByName.ContainsKey(entry.Name))
                {
                    Debug.LogWarning($"[Cuvara.DOTS] '{library.name}' defines archetype '{entry.Name}' twice; the later one is skipped.");
                    continue;
                }

                _indexByName.Add(entry.Name, records.Count);
                records.Add(entry.Config.ToRecord(ViewArchetypeLibrary.HashName(entry.Name)));
            }

            _records = records.ToArray();
            Rebuild();
        }

        /// <summary>Index of a named archetype, or -1. Resolve once and carry the index.</summary>
        public int IndexOf(string archetypeName)
        {
            return archetypeName != null && _indexByName.TryGetValue(archetypeName, out var index) ? index : -1;
        }

        /// <summary>The record at an index; throws for an out-of-range index rather than returning junk.</summary>
        public ViewConfigRecord this[int index] => _records[index];

        /// <summary>
        /// Every distinct view key in the catalog, paired with the largest pool size any archetype
        /// asks for.
        /// </summary>
        /// <remarks>
        /// This is what a consumer feeds to <c>ChunkViewProvisioner.PrewarmChunkAsync</c>. Two
        /// archetypes sharing a prefab must not each add a reference for the same key here — the
        /// provisioner already de-duplicates on intake, and the larger pool size is the one that
        /// matters, matching its grow-only warm count.
        /// </remarks>
        public IReadOnlyDictionary<string, int> PoolSizesByKey()
        {
            var result = new Dictionary<string, int>();
            foreach (var record in _records)
            {
                var key = record.ViewKey.ToString();
                if (string.IsNullOrEmpty(key)) continue;

                result[key] = result.TryGetValue(key, out var existing) && existing > record.PoolSize
                    ? existing
                    : record.PoolSize;
            }

            return result;
        }

        /// <summary>
        /// Publishes the table into <paramref name="world"/> as a singleton. Idempotent — a second
        /// call updates the existing singleton rather than creating a second one, which would make
        /// every <c>GetSingleton</c> throw.
        /// </summary>
        public Entity Install(World world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (!_table.IsCreated) throw new InvalidOperationException("Build must be called before Install.");

            var entityManager = world.EntityManager;
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadWrite<ViewConfigTableReference>());

            Entity entity;
            if (query.IsEmpty)
            {
                entity = entityManager.CreateEntity();
                entityManager.AddComponentData(entity, new ViewConfigTableReference { Table = _table });
#if UNITY_EDITOR
                entityManager.SetName(entity, "ViewConfigTable");
#endif
            }
            else
            {
                entity = query.GetSingletonEntity();
                entityManager.SetComponentData(entity, new ViewConfigTableReference { Table = _table });
            }

            return entity;
        }

        public void Dispose()
        {
            if (_table.IsCreated) _table.Dispose();
            _table = default;
            _records = Array.Empty<ViewConfigRecord>();
            _indexByName.Clear();
        }

        /// <remarks>
        /// The previous blob is freed here. <c>Dispose</c> nulls the pointer in <see cref="_table"/>
        /// itself, but any copy of that reference taken by a caller keeps its own now-dangling
        /// pointer — which is why <see cref="Build"/> documents when it is safe to call rather than
        /// trying to detect misuse.
        /// </remarks>
        private void Rebuild()
        {
            if (_table.IsCreated) _table.Dispose();

            using var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<ViewConfigTable>();
            var array = builder.Allocate(ref root.Records, _records.Length);
            for (var i = 0; i < _records.Length; i++) array[i] = _records[i];

            _table = builder.CreateBlobAssetReference<ViewConfigTable>(Allocator.Persistent);
        }
    }
}
