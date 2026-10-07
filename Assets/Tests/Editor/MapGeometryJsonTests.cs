namespace Tests.Editor
{
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using Scripts.Gameplay.Maps;
    using Shared.GameLogic.Components;
    using Shared.GameLogic.World;

    /// <summary>
    /// The client's reader/writer for the server's map file format (ADR-28): the shipped example
    /// parses into the geometry the server builds from it, every server-required field is
    /// required here too, the shared validation runs, and write -> read is exact.
    /// </summary>
    public sealed class MapGeometryJsonTests
    {
        private const string DevArenaCopy = "Assets/Resources/Maps/dev_arena.json";

        private static MapGeometry ParseOrFail(string json, string mapId = "test")
        {
            var errors = new List<string>();
            var ok = MapGeometryJson.TryParse(json, mapId, out var geometry, errors);
            Assert.That(ok, Is.True, string.Join("\n", errors));
            return geometry;
        }

        private static List<string> ParseErrors(string json)
        {
            var errors = new List<string>();
            Assert.That(MapGeometryJson.TryParse(json, "bad", out var geometry, errors), Is.False);
            Assert.That(geometry, Is.Null);
            Assert.That(errors, Is.Not.Empty);
            return errors;
        }

        [Test]
        public void DevArena_ClientCopy_ParsesIntoTheServersGeometry()
        {
            var geometry = ParseOrFail(File.ReadAllText(DevArenaCopy), "dev_arena");

            Assert.That(geometry.Bounds, Is.EqualTo(new MapBounds(-20f, -20f, 20f, 20f)));
            Assert.That(geometry.HeightField, Is.Not.Null);
            Assert.That(geometry.HeightField.Columns, Is.EqualTo(3));
            Assert.That(geometry.HeightField.Rows, Is.EqualTo(3));
            // Row-major, sample (0,0) at the origin: the last sample (row 2, column 2) is the 2.
            Assert.That(geometry.GroundHeight(20f, 20f), Is.EqualTo(2f));
            Assert.That(geometry.GroundHeight(-20f, -20f), Is.EqualTo(0f));

            Assert.That(geometry.Boxes.Length, Is.EqualTo(2));
            Assert.That(geometry.Boxes[1], Is.EqualTo(new StaticBox(-4f, 4f, 0f, -2f, 6f, 0.3f)));

            Assert.That(geometry.FindSpawn("default", out var spawn), Is.True);
            Assert.That(spawn.Position, Is.EqualTo(new Vec3(0f, -10f, 0f)));
            Assert.That(geometry.Portals.Length, Is.EqualTo(1));
            Assert.That(geometry.Portals[0].TargetMapId, Is.EqualTo("map_01"));
            Assert.That(geometry.Portals[0].Radius, Is.EqualTo(1.5f));
        }

        [Test]
        public void MinimalMap_ZDefaultsToZero_AndMissingSectionsAreEmpty()
        {
            var geometry = ParseOrFail(
                "{ \"bounds\": { \"minX\": 0, \"minY\": 0, \"maxX\": 10, \"maxY\": 5 }, " +
                "\"spawns\": [ { \"name\": \"default\", \"x\": 1, \"y\": 2 } ] }");

            Assert.That(geometry.IsFlat, Is.True);
            Assert.That(geometry.HeightField, Is.Null);
            Assert.That(geometry.Spawns[0].Position, Is.EqualTo(new Vec3(1f, 2f, 0f)));
        }

        [Test]
        public void MissingBounds_IsAnError()
        {
            var errors = ParseErrors("{ \"boxes\": [] }");
            StringAssert.Contains("'bounds'", errors[0]);
        }

        [Test]
        public void IncompleteBox_IsNamedByIndex()
        {
            var errors = ParseErrors(
                "{ \"bounds\": { \"minX\": 0, \"minY\": 0, \"maxX\": 10, \"maxY\": 10 }, " +
                "\"boxes\": [ { \"minX\": 0, \"minY\": 0, \"minZ\": 0, \"maxX\": 1, \"maxY\": 1, \"maxZ\": 1 }, { \"minX\": 0 } ] }");
            StringAssert.Contains("boxes[1]", errors[0]);
        }

        [Test]
        public void SharedValidationRuns_AFlatBoxIsRefused()
        {
            var errors = ParseErrors(
                "{ \"bounds\": { \"minX\": 0, \"minY\": 0, \"maxX\": 10, \"maxY\": 10 }, " +
                "\"boxes\": [ { \"minX\": 0, \"minY\": 0, \"minZ\": 0, \"maxX\": 1, \"maxY\": 1, \"maxZ\": 0 } ] }");
            StringAssert.Contains("box 0", string.Join("\n", errors));
        }

        [Test]
        public void MalformedJson_IsAnError_NotAnException()
        {
            var errors = ParseErrors("{ \"bounds\": ");
            StringAssert.Contains("not valid JSON", errors[0]);
            ParseErrors(string.Empty);
            ParseErrors("[1, 2]");
        }

        [Test]
        public void WriteThenRead_IsExact_AndStable()
        {
            var original = new MapGeometry(
                new MapBounds(-12.5f, -3f, 40.25f, 17f),
                new HeightField(-12.5f, -3f, 0.1f, 2, 3, new[] { 0f, 0.333333343f, 1.5f, -2f, 7.125f, 1e-3f }),
                new[] { new StaticBox(1f, 2f, 0f, 3.5f, 4f, 0.4f) },
                new[] { new SpawnPoint("default", new Vec3(0.1f, 0.2f, 0.3f)) },
                new[] { new Portal("gate", new Vec3(5f, 6f, 0f), 1.5f, 3f, "map_01", "default") });

            var json = MapGeometryJson.Write(original, new[] { "a \"quoted\" comment" });
            var parsed = ParseOrFail(json);

            Assert.That(parsed.Bounds, Is.EqualTo(original.Bounds));
            CollectionAssert.AreEqual(original.HeightField.Heights.ToArray(), parsed.HeightField.Heights.ToArray());
            Assert.That(parsed.HeightField.CellSize, Is.EqualTo(original.HeightField.CellSize));
            Assert.That(parsed.Boxes[0], Is.EqualTo(original.Boxes[0]));
            Assert.That(parsed.Spawns[0].Position, Is.EqualTo(original.Spawns[0].Position));
            Assert.That(parsed.Portals[0].TargetSpawn, Is.EqualTo("default"));
            Assert.That(MapGeometryJson.Write(parsed, new[] { "a \"quoted\" comment" }), Is.EqualTo(json), "a re-export is byte-identical");
        }

        [Test]
        public void MapIds_FollowTheServersFileNameRule()
        {
            Assert.That(MapGeometrySource.IsSafeMapId("dev_arena"), Is.True);
            Assert.That(MapGeometrySource.IsSafeMapId("map-01.v2"), Is.True);
            Assert.That(MapGeometrySource.IsSafeMapId("../secrets"), Is.False);
            Assert.That(MapGeometrySource.IsSafeMapId("a/b"), Is.False);
            Assert.That(MapGeometrySource.IsSafeMapId(string.Empty), Is.False);
        }

        [Test]
        public void Source_LoadsTheBundledCopy_AndAbsentMeansFlat()
        {
            var geometry = MapGeometrySource.LoadOrNull("dev_arena", out var origin);
            Assert.That(geometry, Is.Not.Null, origin);
            Assert.That(geometry.Boxes.Length, Is.EqualTo(2));

            Assert.That(MapGeometrySource.LoadOrNull("map_01", out var flat), Is.Null);
            StringAssert.StartsWith("flat", flat);
        }
    }
}
