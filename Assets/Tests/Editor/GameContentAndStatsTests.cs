namespace Tests.Editor
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using Scripts.Gameplay.Content;
    using Scripts.Gameplay.Presentation;
    using Scripts.Gameplay.Stats;
    using Shared.GameLogic.Components;

    /// <summary>
    /// Content resolved by key (ADR-30): the composed <c>/content</c> document, stat ids looked up
    /// by "level"/"mana" rather than hard-coded, the HUD snapshot built from the replicated stat
    /// block and statuses, and the presentation helpers (height track, projectile
    /// extrapolation, nearest item).
    /// </summary>
    public sealed class GameContentAndStatsTests
    {
        // Shaped like the server's composed document; ids deliberately NOT 1 and 2, so a client
        // that hard-coded the numbers would fail here.
        private const string Composed = @"{
  ""items"": [ { ""id"": ""rusty_sword"", ""name"": ""Rusty Sword"", ""slot"": ""weapon"", ""rarity"": ""common"", ""stackMax"": 1, ""attack"": 3, ""defense"": 0, ""levelRequirement"": 1 } ],
  ""abilities"": [
    { ""id"": 7, ""name"": ""Frost Snare"", ""delivery"": ""ground"", ""range"": 12, ""radius"": 3, ""effects"": [], ""cooldownTicks"": 300 },
    { ""id"": 5, ""name"": ""Fire Bolt"", ""delivery"": ""projectile"", ""effects"": [ { ""kind"": ""damage"", ""power"": 15 } ],
      ""projectile"": { ""speed"": 20, ""radius"": 0.3, ""range"": 25 }, ""cooldownTicks"": 45 }
  ],
  ""stats"": [ { ""id"": 11, ""key"": ""level"", ""default"": 1 }, { ""id"": 12, ""key"": ""mana"", ""default"": 100 } ],
  ""statuses"": [ { ""id"": 3, ""key"": ""burning"", ""durationTicks"": 180 } ]
}";

        private static GameContentCatalog Catalog()
        {
            Assert.That(GameContentCatalog.TryParse(Composed, "h1", out var catalog, out var error), Is.True, error);
            return catalog;
        }

        [Test]
        public void ComposedDocument_ResolvesStatsByKey_AndFindsTheProjectileAbility()
        {
            var catalog = Catalog();

            Assert.That(catalog.Hash, Is.EqualTo("h1"));
            Assert.That(catalog.TryGetStatId("level", out var level) && level == 11u, Is.True);
            Assert.That(catalog.TryGetStatId("mana", out var mana) && mana == 12u, Is.True);
            Assert.That(catalog.TryGetStatId("stamina", out _), Is.False);
            Assert.That(catalog.StatusKey(3u), Is.EqualTo("burning"));
            Assert.That(catalog.Items.ItemCount, Is.EqualTo(1));

            var bolt = catalog.FirstProjectileAbility();
            Assert.That(bolt.Id, Is.EqualTo(5u));
            Assert.That(bolt.ProjectileSpeed, Is.EqualTo(20f));
            Assert.That(bolt.ProjectileRadius, Is.EqualTo(0.3f).Within(1e-6f));
            Assert.That(bolt.ProjectileRange, Is.EqualTo(25f));
        }

        [Test]
        public void AnItemsOnlyDocument_IsFine_AndMalformedSectionsAreErrors()
        {
            Assert.That(GameContentCatalog.TryParse("{\"items\":[]}", "h", out var itemsOnly, out var e1), Is.True, e1);
            Assert.That(itemsOnly.StatCount, Is.EqualTo(0));
            Assert.That(itemsOnly.FirstProjectileAbility(), Is.Null);

            Assert.That(GameContentCatalog.TryParse("{\"stats\":[{\"id\":0,\"key\":\"x\"}]}", "h", out _, out _), Is.False);
            Assert.That(GameContentCatalog.TryParse("{\"stats\":[{\"id\":1,\"key\":\"x\"},{\"id\":2,\"key\":\"x\"}]}", "h", out _, out _), Is.False);
            Assert.That(GameContentCatalog.TryParse("{\"abilities\":[{\"id\":1,\"delivery\":\"projectile\"}]}", "h", out _, out _), Is.False);
            Assert.That(GameContentCatalog.TryParse("not json", "h", out _, out var e2), Is.False);
            StringAssert.Contains("not valid JSON", e2);
        }

        [TestCase(null, "http://10.0.0.5:9101/status", "http://10.0.0.5:9101")]
        [TestCase("http://cdn:9100/content/", "http://ignored/status", "http://cdn:9100")]
        [TestCase("https://content.example", null, "https://content.example")]
        [TestCase(null, null, null)]
        [TestCase(null, "not a url", null)]
        public void ContentOrigin_ExplicitElseTheStatusOrigin(string explicitUrl, string statusUrl, string expected)
        {
            Assert.That(GameContentService.ResolveOrigin(explicitUrl, statusUrl), Is.EqualTo(expected));
        }

        [Test]
        public void StatsSnapshot_ReadsLevelAndManaByKey_AndNamesStatuses()
        {
            var entity = new EntitySnapshotData("player:1", "player", 0f, 0f, 90, 100)
                .WithStats(new[] { new StatValueData(11u, 4), new StatValueData(12u, 37), new StatValueData(99u, 5) })
                .WithStatuses(new[]
                {
                    new StatusEffectData(3u, 2u, 1060ul, "mob:1"),
                    new StatusEffectData(8u, 1u, 0ul, null),
                });

            var stats = PlayerStatsSnapshot.From(entity, Catalog(), worldTick: 1000, tickRate: 60);

            Assert.That(stats.Present, Is.True);
            Assert.That(stats.Level, Is.EqualTo(4));
            Assert.That(stats.Mana, Is.EqualTo(37));
            Assert.That(stats.LevelCaption, Is.EqualTo("Level 4"));
            Assert.That(stats.Statuses, Is.EqualTo("burning x2 (1.0s), status 8"));
        }

        [Test]
        public void StatsSnapshot_WithoutContent_ShowsDashes_NotGuesses()
        {
            var entity = new EntitySnapshotData("p", "player", 0f, 0f, 1, 1).WithStats(new[] { new StatValueData(1u, 9) });

            var stats = PlayerStatsSnapshot.From(entity, GameContentCatalog.Empty, 0, 60);

            Assert.That(stats.Level, Is.Null);
            Assert.That(stats.ManaCaption, Is.EqualTo("Mana —"));
            Assert.That(stats.StatusCaption, Is.EqualTo("No status effects"));
            Assert.That(stats.Equals(PlayerStatsSnapshot.From(entity, GameContentCatalog.Empty, 0, 60)), Is.True);
        }

        [Test]
        public void HeightTrack_InterpolatesAtTheRenderTick_ClampsOutside()
        {
            var track = new HeightTrack();
            Assert.That(track.Evaluate(5), Is.EqualTo(0f));

            track.Push(10, 0f);
            track.Push(12, 2f);
            track.Push(11, 9f); // older than the newest: ignored

            Assert.That(track.Evaluate(9), Is.EqualTo(0f), "before the oldest: the oldest");
            Assert.That(track.Evaluate(11), Is.EqualTo(1f).Within(1e-6f));
            Assert.That(track.Evaluate(11.5), Is.EqualTo(1.5f).Within(1e-6f));
            Assert.That(track.Evaluate(20), Is.EqualTo(2f), "never extrapolated");

            track.Push(12, 3f); // the same tick re-delivered: replaced
            Assert.That(track.Evaluate(12), Is.EqualTo(3f));

            for (var t = 13; t < 20; t++) track.Push(t, t);
            Assert.That(track.Count, Is.EqualTo(HeightTrack.Capacity));
            Assert.That(track.NewestTick, Is.EqualTo(19));
            Assert.That(track.Evaluate(17.5), Is.EqualTo(17.5f).Within(1e-5f));
        }

        [Test]
        public void ProjectileExtrapolation_AdvancesAlongVelocity_Capped()
        {
            var p = new Vec3(1f, 2f, 1f);
            var v = new Vec3(20f, 0f, -1f);

            var drawn = ProjectileExtrapolation.Evaluate(p, v, 0.1f);
            Assert.That(drawn.X, Is.EqualTo(3f).Within(1e-5f));
            Assert.That(drawn.Y, Is.EqualTo(2f));
            Assert.That(drawn.Z, Is.EqualTo(0.9f).Within(1e-5f));
            Assert.That(ProjectileExtrapolation.Evaluate(p, v, -1f), Is.EqualTo(p));
            var capped = ProjectileExtrapolation.Evaluate(p, v, 10f);
            Assert.That(capped.X, Is.EqualTo(1f + 20f * ProjectileExtrapolation.MaxSeconds).Within(1e-5f));
        }

        [Test]
        public void NearestEntity_FindsTheClosestOfAKind_WithinRange()
        {
            var world = new Dictionary<string, EntitySnapshotData>
            {
                ["item:far"] = new EntitySnapshotData("item:far", "item", 9f, 0f, 1, 1),
                ["item:near"] = new EntitySnapshotData("item:near", "item", 2f, 1f, 1, 1),
                ["mob:closer"] = new EntitySnapshotData("mob:closer", "mob", 0.5f, 0f, 1, 1),
            };

            Assert.That(NearestEntity.Find(world, NearestEntity.ItemType, 0f, 0f, 6f), Is.EqualTo("item:near"));
            Assert.That(NearestEntity.Find(world, NearestEntity.ItemType, 0f, 0f, 1f), Is.Null);
            Assert.That(NearestEntity.Find(null, NearestEntity.ItemType, 0f, 0f, 6f), Is.Null);
        }
    }
}
