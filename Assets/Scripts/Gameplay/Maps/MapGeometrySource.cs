namespace Scripts.Gameplay.Maps
{
    using System.Collections.Generic;
    using Shared.GameLogic.World;
    using UnityEngine;

    /// <summary>
    /// Finds the client's copy of a map's geometry: <c>Resources/Maps/&lt;map_id&gt;.json</c>,
    /// the same file the server loads from <c>content/maps/</c> (ADR-28 decision 3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a client-side copy and not the content endpoint.</b> The server's <c>/content</c>
    /// document carries items, abilities, stats and statuses, never maps
    /// (<c>backend/content/README.md</c>, "Served at /content: no"). Map geometry is authored in
    /// the client's scene and baked by the Editor exporter (<c>MapGeometryExporter</c>), which
    /// writes both this copy and the server's file in one step, so the two cannot drift by
    /// accident.
    /// </para>
    /// <para>
    /// <b>Absent is legal, invalid is not used.</b> No file means the flat protocol 2 world —
    /// exactly what the server runs for a map with no file — and the caller passes
    /// <c>null</c> to <c>SetMapGeometry</c>, which restores
    /// <c>MapGeometry.Flat</c> of the
    /// predictor's bounds. A file that fails to parse or validate is logged with every
    /// problem and also falls back to flat: the server refuses to boot on such a file, so it
    /// cannot be the geometry the server is running.
    /// </para>
    /// </remarks>
    public static class MapGeometrySource
    {
        /// <summary>Resources folder the map copies live in.</summary>
        public const string ResourcesFolder = "Maps";

        /// <summary>
        /// The geometry for <paramref name="mapId"/>, or null for "flat" (no file, an unsafe id,
        /// or an invalid file — the last one logged as an error).
        /// </summary>
        public static MapGeometry LoadOrNull(string mapId, out string origin)
        {
            origin = "flat (no map file)";
            if (!IsSafeMapId(mapId))
            {
                return null;
            }

            var asset = Resources.Load<TextAsset>(ResourcesFolder + "/" + mapId);
            if (asset == null)
            {
                return null;
            }

            var errors = new List<string>();
            if (!MapGeometryJson.TryParse(asset.text, mapId, out var geometry, errors))
            {
                Debug.LogError(
                    $"[MapGeometry] Resources/{ResourcesFolder}/{mapId}.json is invalid; predicting against a flat " +
                    $"world instead ({errors.Count} problem(s)):\n  - " + string.Join("\n  - ", errors));
                origin = "flat (invalid map file)";
                return null;
            }

            origin = $"Resources/{ResourcesFolder}/{mapId}.json ({geometry.Boxes.Length} boxes, " +
                     $"heightfield {(geometry.HeightField != null ? "yes" : "no")}, {geometry.Spawns.Length} spawns)";
            return geometry;
        }

        /// <summary>
        /// The server's rule for a map id that may name a file (<c>MapLoader.IsSafeMapId</c>):
        /// letters, digits, <c>_</c>, <c>-</c>, <c>.</c>, no <c>..</c>, at most 128 characters.
        /// </summary>
        public static bool IsSafeMapId(string mapId)
        {
            if (string.IsNullOrEmpty(mapId) || mapId.Length > 128 || mapId.Contains("..")) return false;
            foreach (var c in mapId)
            {
                var ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') ||
                         c == '_' || c == '-' || c == '.';
                if (!ok) return false;
            }

            return true;
        }
    }
}
