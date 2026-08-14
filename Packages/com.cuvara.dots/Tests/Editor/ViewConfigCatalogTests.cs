using Cuvara.DOTS.Configuration;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cuvara.DOTS.Tests.Editor
{
    /// <summary>
    /// Authoring assets in, blob out: name resolution, pool-size aggregation, and the malformed-row
    /// behaviour.
    /// </summary>
    public sealed class ViewConfigCatalogTests
    {
        private ViewArchetypeLibrary _library;
        private ViewConfigCatalog _catalog;

        private static ViewConfig MakeConfig(string key, int poolSize = 1, float scale = 1f)
        {
            var config = ScriptableObject.CreateInstance<ViewConfig>();
            config.Configure(key, poolSize, scale);
            return config;
        }

        private void SetEntries(params ViewArchetypeLibrary.Entry[] entries) => _library.Configure(entries);

        [SetUp]
        public void SetUp()
        {
            _library = ScriptableObject.CreateInstance<ViewArchetypeLibrary>();
            _catalog = new ViewConfigCatalog();
        }

        [TearDown]
        public void TearDown()
        {
            _catalog.Dispose();
            Object.DestroyImmediate(_library);
        }

        [Test]
        public void Build_ResolvesNamesToIndices_AndCarriesTheConfig()
        {
            var goblin = MakeConfig("goblin", poolSize: 8, scale: 1.5f);
            var torch = MakeConfig("torch", poolSize: 2);
            SetEntries(
                new ViewArchetypeLibrary.Entry { Name = "goblin", Config = goblin },
                new ViewArchetypeLibrary.Entry { Name = "torch", Config = torch });

            _catalog.Build(_library);

            Assert.AreEqual(2, _catalog.Count);
            Assert.AreEqual(0, _catalog.IndexOf("goblin"));
            Assert.AreEqual(1, _catalog.IndexOf("torch"));
            Assert.AreEqual(-1, _catalog.IndexOf("wyvern"), "an unknown name is -1, not an exception");

            Assert.AreEqual("goblin", _catalog[0].ViewKey.ToString());
            Assert.AreEqual(8, _catalog[0].PoolSize);
            Assert.AreEqual(1.5f, _catalog[0].Scale, 1e-5f);

            Object.DestroyImmediate(goblin);
            Object.DestroyImmediate(torch);
        }

        [Test]
        public void Blob_LooksUpByNameHash_WithoutAManagedString()
        {
            var goblin = MakeConfig("goblin");
            SetEntries(new ViewArchetypeLibrary.Entry { Name = "goblin", Config = goblin });

            _catalog.Build(_library);

            ref var table = ref _catalog.Table.Value;
            Assert.AreEqual(0, table.IndexOf(ViewArchetypeLibrary.HashName("goblin")));
            Assert.AreEqual(-1, table.IndexOf(ViewArchetypeLibrary.HashName("wyvern")));

            Object.DestroyImmediate(goblin);
        }

        [Test]
        public void TwoArchetypesSharingAPrefab_ReportTheLargestPoolSizeOnce()
        {
            // The provisioner's warm count only grows, so the larger request is the one that matters;
            // reporting the key twice would also double-count against a chunk's reference.
            var small = MakeConfig("goblin", poolSize: 2);
            var large = MakeConfig("goblin", poolSize: 9);
            SetEntries(
                new ViewArchetypeLibrary.Entry { Name = "goblin", Config = small },
                new ViewArchetypeLibrary.Entry { Name = "goblin-elite", Config = large });

            _catalog.Build(_library);
            var sizes = _catalog.PoolSizesByKey();

            Assert.AreEqual(1, sizes.Count);
            Assert.AreEqual(9, sizes["goblin"]);

            Object.DestroyImmediate(small);
            Object.DestroyImmediate(large);
        }

        [Test]
        public void MalformedRows_AreSkippedWithAWarning_NotThrown()
        {
            LogAssert.ignoreFailingMessages = true;

            var good = MakeConfig("goblin");
            SetEntries(
                new ViewArchetypeLibrary.Entry { Name = "", Config = good },          // no name
                new ViewArchetypeLibrary.Entry { Name = "orphan", Config = null },     // no config
                new ViewArchetypeLibrary.Entry { Name = "goblin", Config = good },
                new ViewArchetypeLibrary.Entry { Name = "goblin", Config = good });    // duplicate

            Assert.DoesNotThrow(() => _catalog.Build(_library));
            Assert.AreEqual(1, _catalog.Count, "one broken row must not stop a session starting");
            Assert.AreEqual(0, _catalog.IndexOf("goblin"));

            LogAssert.ignoreFailingMessages = false;
            Object.DestroyImmediate(good);
        }

        /// <summary>
        /// Rebuilding produces a valid table, and disposing releases it.
        /// </summary>
        /// <remarks>
        /// <b>Note what is deliberately not asserted here.</b> An earlier version of this test held a
        /// copy of the first <see cref="Unity.Entities.BlobAssetReference{T}"/> and asserted
        /// <c>IsCreated == false</c> on it after the rebuild. That assertion is unsound:
        /// <c>BlobAssetReference&lt;T&gt;</c> is a struct, and its <c>Dispose</c> frees the memory and
        /// then nulls <c>m_Ptr</c> <i>on the instance it was called on</i> (Entities
        /// <c>Blobs.cs</c>). A copy taken beforehand keeps its own pointer and goes on reporting
        /// <c>IsCreated == true</c> even though the memory is gone — so the test failed against
        /// correct code. Pointer inequality is not a safe substitute either: the allocator may hand
        /// the same address back for the new blob. What is observable is asserted; the release itself
        /// is a single unconditional line in <c>Rebuild</c>.
        /// </remarks>
        [Test]
        public void Rebuilding_ProducesAValidTable_AndDisposeReleasesIt()
        {
            var goblin = MakeConfig("goblin", poolSize: 3);
            SetEntries(new ViewArchetypeLibrary.Entry { Name = "goblin", Config = goblin });
            _catalog.Build(_library);

            _catalog.Build(_library);

            Assert.IsTrue(_catalog.Table.IsCreated, "the rebuilt table is usable");
            Assert.AreEqual(1, _catalog.Count);
            Assert.AreEqual(3, _catalog[0].PoolSize, "and holds the rebuilt content, not stale memory");
            Assert.AreEqual(0, _catalog.Table.Value.IndexOf(ViewArchetypeLibrary.HashName("goblin")));

            _catalog.Dispose();
            Assert.IsFalse(_catalog.Table.IsCreated, "the catalog no longer references a blob");

            Object.DestroyImmediate(goblin);
        }
    }
}
