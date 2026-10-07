namespace Tests.Editor
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using Shared.GameLogic.Components;
    using Shared.GameLogic.World;
    using UnityEditor.SceneManagement;
    using UnityEngine;
    using UnityEngine.SceneManagement;

    /// <summary>
    /// The Editor map exporter's bake (ADR-28 decision 3) on a throwaway additive scene: axis
    /// mapping, rotated colliders skipped with a warning, markers, and a result the shared
    /// validation accepts.
    /// </summary>
    public sealed class MapGeometryExporterTests
    {
        private Scene scene;

        [SetUp]
        public void SetUp()
        {
            // Single, not additive: the runner's own scene is untitled and unsaved, and Unity
            // refuses an additive scene beside it. The Test Runner restores the scene setup.
            this.scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [TearDown]
        public void TearDown()
        {
            if (!this.scene.IsValid()) return;
            foreach (var root in this.scene.GetRootGameObjects()) Object.DestroyImmediate(root);
        }

        private GameObject Add(string name, Vector3 position, Vector3? scale = null, Quaternion? rotation = null, bool box = false)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, this.scene);
            go.transform.SetPositionAndRotation(position, rotation ?? Quaternion.identity);
            go.transform.localScale = scale ?? Vector3.one;
            if (box) go.AddComponent<BoxCollider>();
            return go;
        }

        [Test]
        public void Bake_MapsUnityToWireAxes_AndSkipsRotatedBoxes()
        {
            var bounds = Add("map_bounds", new Vector3(0f, 0f, 0f), new Vector3(40f, 1f, 30f), box: true);
            Add("wall", new Vector3(2f, 1f, 5f), new Vector3(4f, 2f, 1f), box: true);
            Add("ramp", new Vector3(-5f, 0.5f, 0f), Vector3.one, Quaternion.Euler(0f, 30f, 0f), box: true);
            var trigger = Add("trigger", new Vector3(0f, 0f, 0f), box: true);
            trigger.GetComponent<BoxCollider>().isTrigger = true;
            Add("spawn_default", new Vector3(1f, 0f, -3f));
            Add("portal_gate@map_01/default", new Vector3(10f, 1.5f, 10f), new Vector3(3f, 1.5f, 3f));
            Assert.That(bounds, Is.Not.Null);

            var warnings = new List<string>();
            var geometry = MapGeometryExporter.Bake(this.scene, warnings);

            Assert.That(geometry.Bounds, Is.EqualTo(new MapBounds(-20f, -15f, 20f, 15f)), "map_bounds' Unity x/z rectangle");

            Assert.That(geometry.Boxes.Length, Is.EqualTo(1), "the rotated ramp and the trigger are not baked");
            var wall = geometry.Boxes[0];
            // Unity centre (2,1,5), size (4,2,1): x 0..4, y(up) 0..2, z 4.5..5.5 -> wire y = Unity z, wire z = Unity y.
            Assert.That(wall, Is.EqualTo(new StaticBox(0f, 4.5f, 0f, 4f, 5.5f, 2f)));
            Assert.That(warnings.Exists(w => w.Contains("ramp") && w.Contains("SKIPPED")), Is.True, string.Join("\n", warnings));

            Assert.That(geometry.FindSpawn("default", out var spawn), Is.True);
            Assert.That(spawn.Position, Is.EqualTo(new Vec3(1f, -3f, 0f)));

            Assert.That(geometry.Portals.Length, Is.EqualTo(1));
            var gate = geometry.Portals[0];
            Assert.That(gate.Name, Is.EqualTo("gate"));
            Assert.That(gate.TargetMapId, Is.EqualTo("map_01"));
            Assert.That(gate.TargetSpawn, Is.EqualTo("default"));
            Assert.That(gate.Radius, Is.EqualTo(1.5f));
            Assert.That(gate.Height, Is.EqualTo(3f));
            Assert.That(gate.Position, Is.EqualTo(new Vec3(10f, 10f, 0f)), "the cylinder's base, not its centre");

            var errors = new List<string>();
            Assert.That(MapGeometryValidation.Validate("test", geometry, errors), Is.True, string.Join("\n", errors));
        }

        [Test]
        public void Bake_WithoutBoundsMarker_UsesTheExtent_AndSaysSo()
        {
            Add("block", new Vector3(0f, 0.5f, 0f), Vector3.one, box: true);
            Add("spawn_default", new Vector3(3f, 0f, 4f));

            var warnings = new List<string>();
            var geometry = MapGeometryExporter.Bake(this.scene, warnings);

            Assert.That(geometry.Bounds, Is.EqualTo(new MapBounds(-0.5f, -0.5f, 3f, 4f)));
            Assert.That(warnings.Exists(w => w.Contains("map_bounds")), Is.True);
        }
    }
}
