using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Scripts.Gameplay;
using Scripts.Gameplay.Maps;
using Shared.GameLogic.Components;
using Shared.GameLogic.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Bakes a scene into the server's map file format (<c>content/maps/&lt;map_id&gt;.json</c>,
/// ADR-28 decision 3): the Unity client authors the world, the server loads it at boot, and
/// both predict and simulate against the same <see cref="MapGeometry"/>.
/// </summary>
/// <remarks>
/// <para><b>What is baked</b> (Unity <c>(x, y, z)</c> becomes wire <c>(x, z, y)</c>, <see cref="WireAxes"/>):</para>
/// <list type="bullet">
/// <item><description><b>Boxes</b>: every enabled, non-trigger <see cref="BoxCollider"/> on an active
/// GameObject, as its world AABB. Its world rotation must be identity: the server's boxes are
/// axis-aligned, and the AABB of a rotated box is a different, larger box. A rotated collider is
/// SKIPPED with a warning naming it, never silently enlarged.</description></item>
/// <item><description><b>Heightfield</b>: the first active <see cref="Terrain"/>, sampled on a regular
/// grid (its heightmap resolution, downsampled to at most 1025 x 1025 and to a square cell).
/// More than one terrain warns; only the first is baked.</description></item>
/// <item><description><b>Spawns</b>: GameObjects named <c>spawn_&lt;name&gt;</c> (or tagged
/// <c>Respawn</c>, named by the object), at their position. <c>spawn_default</c> is where new
/// players appear.</description></item>
/// <item><description><b>Portals</b>: GameObjects named
/// <c>portal_&lt;name&gt;@&lt;targetMapId&gt;/&lt;targetSpawn&gt;</c>, read as a Unity cylinder:
/// radius = half the larger of scale x/z, height = 2 x scale y, base at the bottom of the
/// cylinder.</description></item>
/// <item><description><b>Bounds</b>: a GameObject named <c>map_bounds</c> with a BoxCollider gives
/// the ground-plane rectangle; otherwise the union of everything above.</description></item>
/// </list>
/// <para>Marker objects (spawns, portals, <c>map_bounds</c>) never contribute boxes.</para>
/// <para>
/// The result is validated with the shared <see cref="MapGeometryValidation"/> — the server's
/// boot check — before anything is written; an invalid map is not written at all. It is written
/// to the chosen path (default: the server repo's <c>backend/content/maps/</c> next to this
/// project) AND to the client copy <c>Assets/Resources/Maps/&lt;map_id&gt;.json</c> that the
/// client predicts against, so the two cannot drift by forgetting one.
/// </para>
/// <para>
/// Headless: <c>-executeMethod MapGeometryExporter.ExportFromCommandLine -mapScene
/// Assets/Scenes/X.unity [-mapId x] [-mapExportPath path]</c>.
/// </para>
/// </remarks>
public static class MapGeometryExporter
{
    public const string ServerMapsFolder = "../rpg-mmo-server/backend/content/maps";
    public const string ClientCopyFolder = "Assets/Resources/" + MapGeometrySource.ResourcesFolder;
    public const string BoundsMarker = "map_bounds";
    public const string SpawnPrefix = "spawn_";
    public const string PortalPrefix = "portal_";
    public const int MaxHeightfieldSide = 1025;

    [MenuItem("Cuvara/Maps/Export Open Scene To Map JSON...")]
    public static void ExportInteractive()
    {
        var scene = SceneManager.GetActiveScene();
        var mapId = SanitizeMapId(scene.name);
        var directory = Path.GetFullPath(ServerMapsFolder);
        if (!Directory.Exists(directory)) directory = Path.GetFullPath(".");

        var path = EditorUtility.SaveFilePanel("Export map geometry", directory, mapId + ".json", "json");
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            var summary = Export(scene, Path.GetFileNameWithoutExtension(path), path);
            EditorUtility.DisplayDialog("Map exported", summary, "OK");
        }
        catch (Exception exception)
        {
            Debug.LogError(exception.Message);
            EditorUtility.DisplayDialog("Map export failed", exception.Message, "OK");
        }
    }

    /// <summary>Batchmode entry point; throws (non-zero exit) on any failure.</summary>
    public static void ExportFromCommandLine()
    {
        var args = Environment.GetCommandLineArgs();
        var scenePath = Arg(args, "-mapScene");
        if (!string.IsNullOrEmpty(scenePath))
        {
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        }

        var scene = SceneManager.GetActiveScene();
        var mapId = Arg(args, "-mapId") ?? SanitizeMapId(scene.name);
        var path = Arg(args, "-mapExportPath") ?? Path.Combine(Path.GetFullPath(ServerMapsFolder), mapId + ".json");
        Debug.Log(Export(scene, mapId, path));
    }

    /// <summary>Bakes, validates and writes; returns a summary. Throws with every problem listed.</summary>
    public static string Export(Scene scene, string mapId, string path)
    {
        if (!MapGeometrySource.IsSafeMapId(mapId))
        {
            throw new InvalidOperationException($"[MapExport] '{mapId}' is not a usable map id (letters, digits, '_', '-', '.').");
        }

        var warnings = new List<string>();
        var geometry = Bake(scene, warnings);

        var errors = new List<string>();
        if (!MapGeometryValidation.Validate(mapId, geometry, errors))
        {
            throw new InvalidOperationException(
                $"[MapExport] '{scene.name}' does not produce a valid map ({errors.Count} problem(s)); nothing was written:\n  - " +
                string.Join("\n  - ", errors));
        }

        var json = MapGeometryJson.Write(geometry, new[]
        {
            $"Baked from Unity scene '{scene.path}' by MapGeometryExporter (IndieRPGMMOAdventure).",
            "Re-export from the scene rather than editing by hand; the client copy is Assets/Resources/Maps/" + mapId + ".json.",
            "x/y are the ground plane, z is up. Format: backend/content/README.md, 'Map geometry'.",
        });

        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full) ?? ".");
        File.WriteAllText(full, json);

        Directory.CreateDirectory(ClientCopyFolder);
        var clientCopy = $"{ClientCopyFolder}/{mapId}.json";
        File.WriteAllText(clientCopy, json);
        AssetDatabase.ImportAsset(clientCopy);

        foreach (var warning in warnings) Debug.LogWarning(warning);

        return $"[MapExport] '{scene.name}' -> {full} and {clientCopy}: {geometry.Boxes.Length} boxes, " +
               $"heightfield {(geometry.HeightField != null ? $"{geometry.HeightField.Columns}x{geometry.HeightField.Rows}" : "none")}, " +
               $"{geometry.Spawns.Length} spawns, {geometry.Portals.Length} portals, {warnings.Count} warning(s).";
    }

    /// <summary>
    /// The scene's geometry in wire axes; problems that skip an object are appended to
    /// <paramref name="warnings"/>. Pure over the scene: writes nothing.
    /// </summary>
    public static MapGeometry Bake(Scene scene, List<string> warnings)
    {
        // Collider.bounds reads the physics scene; make it match the transforms as edited.
        Physics.SyncTransforms();

        var boxes = new List<StaticBox>();
        var spawns = new List<SpawnPoint>();
        var portals = new List<Portal>();
        Rect? explicitBounds = null;
        var extent = new ExtentXZ();

        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(includeInactive: false))
            {
                var go = t.gameObject;
                var name = go.name;

                if (string.Equals(name, BoundsMarker, StringComparison.OrdinalIgnoreCase))
                {
                    var marker = go.GetComponent<BoxCollider>();
                    if (marker != null)
                    {
                        var b = marker.bounds;
                        explicitBounds = Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z);
                    }
                    else
                    {
                        warnings.Add($"[MapExport] '{Describe(t)}' is named {BoundsMarker} but has no BoxCollider; ignored.");
                    }

                    continue;
                }

                if (name.StartsWith(SpawnPrefix, StringComparison.OrdinalIgnoreCase) || go.CompareTag("Respawn"))
                {
                    var spawnName = name.StartsWith(SpawnPrefix, StringComparison.OrdinalIgnoreCase) ? name.Substring(SpawnPrefix.Length) : name;
                    var wire = WireAxes.ToWire(t.position);
                    spawns.Add(new SpawnPoint(spawnName, wire));
                    extent.Add(wire.X, wire.Y);
                    continue;
                }

                if (name.StartsWith(PortalPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    if (TryParsePortal(t, out var portal, out var problem))
                    {
                        portals.Add(portal);
                        extent.Add(portal.Position.X, portal.Position.Y);
                    }
                    else
                    {
                        warnings.Add($"[MapExport] '{Describe(t)}': {problem}; portal skipped.");
                    }

                    continue;
                }

                foreach (var collider in go.GetComponents<BoxCollider>())
                {
                    if (!collider.enabled || collider.isTrigger) continue;

                    if (Quaternion.Angle(t.rotation, Quaternion.identity) > 0.01f)
                    {
                        warnings.Add(
                            $"[MapExport] '{Describe(t)}' has a rotated BoxCollider (world rotation {t.rotation.eulerAngles}); " +
                            "map boxes are axis-aligned, so it was SKIPPED. Un-rotate it or split it into axis-aligned boxes.");
                        continue;
                    }

                    var b = collider.bounds;
                    var box = new StaticBox(b.min.x, b.min.z, b.min.y, b.max.x, b.max.z, b.max.y);
                    boxes.Add(box);
                    extent.Add(box.MinX, box.MinY);
                    extent.Add(box.MaxX, box.MaxY);
                }
            }
        }

        HeightField heightField = null;
        var terrains = new List<Terrain>();
        foreach (var root in scene.GetRootGameObjects())
        {
            terrains.AddRange(root.GetComponentsInChildren<Terrain>(includeInactive: false));
        }

        if (terrains.Count > 0)
        {
            if (terrains.Count > 1)
            {
                warnings.Add($"[MapExport] {terrains.Count} terrains in the scene; only '{Describe(terrains[0].transform)}' is baked (a map has one heightfield).");
            }

            heightField = BakeTerrain(terrains[0], warnings);
            if (heightField != null)
            {
                extent.Add(heightField.OriginX, heightField.OriginY);
                extent.Add(heightField.MaxX, heightField.MaxY);
            }
        }

        MapBounds bounds;
        if (explicitBounds.HasValue)
        {
            var r = explicitBounds.Value;
            bounds = new MapBounds(r.xMin, r.yMin, r.xMax, r.yMax);
        }
        else if (extent.HasValue)
        {
            bounds = new MapBounds(extent.MinX, extent.MinY, extent.MaxX, extent.MaxY);
            warnings.Add($"[MapExport] no '{BoundsMarker}' object; bounds are the extent of the baked geometry {bounds.MinX},{bounds.MinY} .. {bounds.MaxX},{bounds.MaxY}.");
        }
        else
        {
            bounds = new MapBounds(0f, 0f, 0f, 0f); // fails validation, with the validator's message
        }

        return new MapGeometry(bounds, heightField, boxes.ToArray(), spawns.ToArray(), portals.ToArray());
    }

    private static HeightField BakeTerrain(Terrain terrain, List<string> warnings)
    {
        var data = terrain.terrainData;
        if (data == null)
        {
            warnings.Add($"[MapExport] terrain '{Describe(terrain.transform)}' has no TerrainData; no heightfield.");
            return null;
        }

        var size = data.size;
        var resolution = Math.Max(2, data.heightmapResolution);

        // A square cell (the format has one cellSize): the smaller of the two axes' native
        // spacing, then coarsened until neither side exceeds the format's 1025 samples.
        var cell = Math.Min(size.x, size.z) / (resolution - 1);
        if (!(cell > 0f))
        {
            warnings.Add($"[MapExport] terrain '{Describe(terrain.transform)}' has zero size; no heightfield.");
            return null;
        }

        var columns = (int)Math.Floor(size.x / cell + 1e-4) + 1;
        var rows = (int)Math.Floor(size.z / cell + 1e-4) + 1;
        while (columns > MaxHeightfieldSide || rows > MaxHeightfieldSide)
        {
            cell *= 2f;
            columns = (int)Math.Floor(size.x / cell + 1e-4) + 1;
            rows = (int)Math.Floor(size.z / cell + 1e-4) + 1;
        }

        var origin = terrain.transform.position;
        var heights = new float[columns * rows];
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var world = new Vector3(origin.x + column * cell, 0f, origin.z + row * cell);
                heights[row * columns + column] = terrain.SampleHeight(world) + origin.y;
            }
        }

        if (Math.Abs(size.x - size.z) > 1e-3f)
        {
            warnings.Add(
                $"[MapExport] terrain '{Describe(terrain.transform)}' is not square ({size.x} x {size.z}); baked with a square cell of " +
                $"{cell.ToString(CultureInfo.InvariantCulture)} ({columns} x {rows} samples), so the far edge may stop short of the terrain's.");
        }

        return new HeightField(origin.x, origin.z, cell, columns, rows, heights);
    }

    private static bool TryParsePortal(Transform t, out Portal portal, out string problem)
    {
        portal = default;
        var spec = t.gameObject.name.Substring(PortalPrefix.Length);
        var at = spec.IndexOf('@');
        var slash = at < 0 ? -1 : spec.IndexOf('/', at + 1);
        if (at <= 0 || slash < 0 || slash == at + 1 || slash == spec.Length - 1)
        {
            problem = $"a portal is named '{PortalPrefix}<name>@<targetMapId>/<targetSpawn>'";
            return false;
        }

        var scale = t.lossyScale;
        var radius = 0.5f * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        var height = 2f * Mathf.Abs(scale.y);
        var feet = WireAxes.ToWire(t.position - new Vector3(0f, Mathf.Abs(scale.y), 0f));
        portal = new Portal(spec.Substring(0, at), feet, radius, height, spec.Substring(at + 1, slash - at - 1), spec.Substring(slash + 1));
        problem = null;
        return true;
    }

    private static string SanitizeMapId(string sceneName)
    {
        var chars = sceneName.ToLowerInvariant().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.')) chars[i] = '_';
        }

        return new string(chars);
    }

    private static string Arg(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal)) return args[i + 1];
        }

        return null;
    }

    private static string Describe(Transform t) => t.parent == null ? t.name : Describe(t.parent) + "/" + t.name;

    private struct ExtentXZ
    {
        public bool HasValue;
        public float MinX, MinY, MaxX, MaxY;

        public void Add(float x, float y)
        {
            if (!this.HasValue)
            {
                this.HasValue = true;
                this.MinX = this.MaxX = x;
                this.MinY = this.MaxY = y;
                return;
            }

            this.MinX = Math.Min(this.MinX, x);
            this.MaxX = Math.Max(this.MaxX, x);
            this.MinY = Math.Min(this.MinY, y);
            this.MaxY = Math.Max(this.MaxY, y);
        }
    }
}
