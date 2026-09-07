#if CUVARA_DOTS
namespace Tests.Editor
{
    using System;
    using Cuvara.DOTS.Configuration;
    using NUnit.Framework;
    using Scripts.DI.Dots;
    using UnityEngine;
    using UnityEngine.AddressableAssets;

    /// <summary>
    /// The library asset's generation and the validation the bridge and the build gate share:
    /// every archetype present, every server kind mapped, every entry carrying a prefab reference,
    /// keys well-formed — with the offending entry named.
    /// </summary>
    public sealed class DotsViewLibraryValidationTests
    {
        private DotsViewLibraryAsset asset;

        [SetUp]
        public void SetUp()
        {
            this.asset = ScriptableObject.CreateInstance<DotsViewLibraryAsset>();
            this.asset.name = "TestViewLibrary";
        }

        [TearDown]
        public void TearDown()
        {
            if (this.asset != null) UnityEngine.Object.DestroyImmediate(this.asset);
        }

        private static DotsViewLibraryAsset.Entry Entry(string archetype, string viewKey = null, bool withPrefab = true, int pool = 4)
        {
            return new DotsViewLibraryAsset.Entry
            {
                Archetype = archetype,
                ViewKey = viewKey,
                // A well-formed guid is a valid runtime key; whether it resolves to a real prefab
                // is the build-time check's stricter question, exercised through prefabExists.
                Prefab = withPrefab ? new AssetReferenceGameObject(Guid.NewGuid().ToString("N")) : null,
                PoolSize = pool,
                Scale = 1f,
            };
        }

        private void ConfigureComplete()
        {
            this.asset.Configure(
                Entry(DotsViewArchetypes.PlayerLocal),
                Entry(DotsViewArchetypes.PlayerRemote, viewKey: "player"),
                Entry(DotsViewArchetypes.Mob));
        }

        [Test]
        public void CompleteLibrary_IsValid()
        {
            this.ConfigureComplete();

            var report = DotsViewLibraryValidation.Validate(this.asset);

            Assert.That(report.IsValid, Is.True, DotsViewLibraryValidation.Describe(report));
        }

        [Test]
        public void BuildLibrary_GeneratesConfigs_WithTheEffectiveKey()
        {
            this.ConfigureComplete();

            var library = this.asset.BuildLibrary(out var configs);
            try
            {
                Assert.That(library.Entries.Count, Is.EqualTo(3));
                Assert.That(configs[0].ViewKey, Is.EqualTo(DotsViewArchetypes.PlayerLocal), "empty view key defaults to the archetype");
                Assert.That(configs[1].ViewKey, Is.EqualTo("player"), "an explicit view key is kept");
                Assert.That(configs[0].PoolSize, Is.EqualTo(4));
                Assert.That(library.Entries[2].Name, Is.EqualTo(DotsViewArchetypes.Mob));
                Assert.That(library.Entries[2].Config, Is.SameAs(configs[2]));

                var catalog = new ViewConfigCatalog();
                Assert.That(catalog.TryBuild(library, out var report, this.asset.HasPrefab), Is.True, DotsViewLibraryValidation.Describe(report));
                Assert.That(catalog.PoolSizesByKey()["player"], Is.EqualTo(4));
                catalog.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(library);
                foreach (var config in configs) UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void MissingArchetype_IsNamed()
        {
            this.asset.Configure(Entry(DotsViewArchetypes.PlayerLocal), Entry(DotsViewArchetypes.PlayerRemote));

            var report = DotsViewLibraryValidation.Validate(this.asset);

            Assert.That(report.HasErrors, Is.True);
            Assert.That(report.Has(DotsViewLibraryValidation.MissingArchetype, DotsViewArchetypes.Mob), Is.True, DotsViewLibraryValidation.Describe(report));
            Assert.That(report.Has(ViewConfigIssue.UnknownArchetype), Is.True, "the server kind 'mob' maps to an archetype the library lacks");
        }

        [Test]
        public void MissingPrefabReference_IsNamed_OnBothChecks()
        {
            this.asset.Configure(
                Entry(DotsViewArchetypes.PlayerLocal),
                Entry(DotsViewArchetypes.PlayerRemote),
                Entry(DotsViewArchetypes.Mob, withPrefab: false));

            var report = DotsViewLibraryValidation.Validate(this.asset);

            Assert.That(report.Has(DotsViewLibraryValidation.MissingPrefabReference, DotsViewArchetypes.Mob), Is.True);
            Assert.That(report.Has(ViewConfigIssue.MissingPrefab, DotsViewArchetypes.Mob), Is.True, "the package's prefabExists check agrees");
        }

        [Test]
        public void StricterPrefabExists_FailsAReferenceThatDoesNotResolve()
        {
            // What the build gate does: a well-formed guid whose editor asset is not a prefab.
            this.ConfigureComplete();

            var report = DotsViewLibraryValidation.Validate(this.asset, prefabExists: key => key != DotsViewArchetypes.Mob);

            Assert.That(report.Has(ViewConfigIssue.MissingPrefab, DotsViewArchetypes.Mob), Is.True);
            Assert.That(report.Has(ViewConfigIssue.MissingPrefab, DotsViewArchetypes.PlayerLocal), Is.False);
        }

        [Test]
        public void DuplicateArchetype_IsNamed()
        {
            this.asset.Configure(
                Entry(DotsViewArchetypes.PlayerLocal),
                Entry(DotsViewArchetypes.PlayerLocal),
                Entry(DotsViewArchetypes.PlayerRemote),
                Entry(DotsViewArchetypes.Mob));

            var report = DotsViewLibraryValidation.Validate(this.asset);

            Assert.That(report.Has(ViewConfigIssue.DuplicateName, DotsViewArchetypes.PlayerLocal), Is.True);
            Assert.That(report.HasErrors, Is.True);
        }

        [Test]
        public void NullAsset_IsAnActionableError()
        {
            var report = DotsViewLibraryValidation.Validate(null);

            Assert.That(report.Has(DotsViewLibraryValidation.MissingLibrary), Is.True);
            StringAssert.Contains(DotsViewLibraryAsset.DefaultAssetPath, DotsViewLibraryValidation.Describe(report));
        }

        [Test]
        public void ServerKindMappings_TargetArchetypesTheResolverKnows()
        {
            foreach (var pair in DotsViewArchetypes.ServerKindMappings)
            {
                CollectionAssert.Contains(DotsViewArchetypes.All, pair.Value, $"server kind '{pair.Key}' maps to an unlisted archetype");
            }

            CollectionAssert.Contains(DotsViewArchetypes.All, DotsViewArchetypes.PlayerLocal);
        }
    }
}
#endif
