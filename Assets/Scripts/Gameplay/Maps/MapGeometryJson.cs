namespace Scripts.Gameplay.Maps
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using Cuvara.Netcode.Json;
    using Shared.GameLogic.Components;
    using Shared.GameLogic.World;

    /// <summary>
    /// Reads and writes the server's map file format (<c>content/maps/&lt;map_id&gt;.json</c>,
    /// ADR-28 decision 3, documented in <c>rpg-mmo-server/backend/content/README.md</c>) as a
    /// <see cref="Shared.GameLogic.World.MapGeometry"/>, and validates it with the same
    /// <see cref="MapGeometryValidation"/> the server runs at boot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a client-side parser at all.</b> <c>Shared.GameLogic</c> has no JSON parser by
    /// design (ADR-19 decision 4): the server parses with source-generated
    /// <c>System.Text.Json</c>, which Unity does not have. This one is built on the netcode
    /// package's hand-written <see cref="JsonParser"/> — no reflection, so it is safe under
    /// IL2CPP stripping — and mirrors the server's <c>MapLoader.Build</c> field for field:
    /// the same required fields, the same defaults (<c>z</c> 0), the same refusal of a file
    /// that is malformed rather than a guess at what it meant.
    /// </para>
    /// <para>
    /// <b>Writing</b> is for the Editor exporter. The output is plain, stable, invariant-culture
    /// JSON with round-trip float formatting, so a re-export of an unchanged scene is a
    /// byte-identical file and a reviewer sees only what moved.
    /// </para>
    /// <para>Axes are the wire's: x/y ground plane, z up. Convert with <see cref="WireAxes"/>.</para>
    /// </remarks>
    public static class MapGeometryJson
    {
        private const char ByteOrderMark = (char)0xFEFF;

        /// <summary>
        /// Parses and validates <paramref name="json"/>. Returns false and appends one line per
        /// problem to <paramref name="errors"/> when the document is malformed, incomplete or
        /// fails <see cref="MapGeometryValidation"/>.
        /// </summary>
        public static bool TryParse(string json, string mapId, out MapGeometry geometry, List<string> errors)
        {
            if (errors == null) throw new ArgumentNullException(nameof(errors));
            geometry = null;
            int before = errors.Count;
            string p = "map " + (string.IsNullOrEmpty(mapId) ? "<unnamed>" : mapId) + ": ";

            if (string.IsNullOrWhiteSpace(json))
            {
                errors.Add(p + "the document is empty.");
                return false;
            }

            JsonValue root;
            try
            {
                // A UTF-8 BOM is legal in a file and not in JSON; editors add one silently.
                root = JsonParser.Parse(json.TrimStart(ByteOrderMark));
            }
            catch (JsonParseException ex)
            {
                errors.Add(p + "is not valid JSON: " + ex.Message);
                return false;
            }

            if (root.Kind != JsonKind.Object)
            {
                errors.Add(p + "the document must be a JSON object.");
                return false;
            }

            var built = Build(root, p, errors);
            if (built == null || errors.Count > before)
            {
                return false;
            }

            if (!MapGeometryValidation.Validate(mapId, built, errors))
            {
                return false;
            }

            geometry = built;
            return true;
        }

        private static MapGeometry Build(JsonValue root, string p, List<string> errors)
        {
            if (!root.TryGetMember("bounds", out var b) || b.Kind != JsonKind.Object ||
                !TryNumber(b, "minX", out var minX) || !TryNumber(b, "minY", out var minY) ||
                !TryNumber(b, "maxX", out var maxX) || !TryNumber(b, "maxY", out var maxY))
            {
                errors.Add(p + "'bounds' is missing or incomplete; it needs minX, minY, maxX and maxY.");
                return null;
            }

            var bounds = new MapBounds(minX, minY, maxX, maxY);

            HeightField heightField = null;
            if (root.TryGetMember("heightfield", out var h) && h.Kind != JsonKind.Null)
            {
                if (h.Kind != JsonKind.Object ||
                    !TryNumber(h, "originX", out var originX) || !TryNumber(h, "originY", out var originY) ||
                    !TryNumber(h, "cellSize", out var cellSize) || !TryInt(h, "columns", out var columns) ||
                    !TryInt(h, "rows", out var rows) || !h.TryGetMember("heights", out var heightsValue) ||
                    heightsValue.Kind != JsonKind.Array)
                {
                    errors.Add(p + "'heightfield' needs originX, originY, cellSize, columns, rows and heights.");
                }
                else
                {
                    var items = heightsValue.Items;
                    var heights = new float[items.Count];
                    var bad = false;
                    for (var i = 0; i < items.Count; i++)
                    {
                        if (items[i].Kind != JsonKind.Number)
                        {
                            bad = true;
                            break;
                        }

                        heights[i] = (float)items[i].AsNumber();
                    }

                    if (bad)
                    {
                        errors.Add(p + "'heightfield.heights' must contain only numbers.");
                    }
                    else
                    {
                        heightField = new HeightField(originX, originY, cellSize, columns, rows, heights);
                    }
                }
            }

            var boxes = new List<StaticBox>();
            var rawBoxes = root.GetArray("boxes");
            for (var i = 0; i < rawBoxes.Count; i++)
            {
                var o = rawBoxes[i];
                if (o.Kind != JsonKind.Object ||
                    !TryNumber(o, "minX", out var x0) || !TryNumber(o, "minY", out var y0) || !TryNumber(o, "minZ", out var z0) ||
                    !TryNumber(o, "maxX", out var x1) || !TryNumber(o, "maxY", out var y1) || !TryNumber(o, "maxZ", out var z1))
                {
                    errors.Add(p + "boxes[" + I(i) + "] needs minX, minY, minZ, maxX, maxY and maxZ.");
                    continue;
                }

                boxes.Add(new StaticBox(x0, y0, z0, x1, y1, z1));
            }

            var spawns = new List<SpawnPoint>();
            var rawSpawns = root.GetArray("spawns");
            for (var i = 0; i < rawSpawns.Count; i++)
            {
                var o = rawSpawns[i];
                if (o.Kind != JsonKind.Object || !TryNumber(o, "x", out var x) || !TryNumber(o, "y", out var y))
                {
                    errors.Add(p + "spawns[" + I(i) + "] needs name, x and y (z defaults to 0).");
                    continue;
                }

                TryNumber(o, "z", out var z);
                spawns.Add(new SpawnPoint(o.GetString("name"), new Vec3(x, y, z)));
            }

            var portals = new List<Portal>();
            var rawPortals = root.GetArray("portals");
            for (var i = 0; i < rawPortals.Count; i++)
            {
                var o = rawPortals[i];
                if (o.Kind != JsonKind.Object || !TryNumber(o, "x", out var x) || !TryNumber(o, "y", out var y) ||
                    !TryNumber(o, "radius", out var radius) || !TryNumber(o, "height", out var height))
                {
                    errors.Add(p + "portals[" + I(i) + "] needs name, x, y, radius, height, targetMapId and targetSpawn (z defaults to 0).");
                    continue;
                }

                TryNumber(o, "z", out var z);
                portals.Add(new Portal(
                    o.GetString("name"),
                    new Vec3(x, y, z),
                    radius,
                    height,
                    o.GetString("targetMapId"),
                    o.GetString("targetSpawn")));
            }

            return new MapGeometry(bounds, heightField, boxes.ToArray(), spawns.ToArray(), portals.ToArray());
        }

        /// <summary>
        /// Writes <paramref name="geometry"/> in the server's map file format. Optional
        /// <paramref name="comments"/> become the <c>"$comment"</c> array the server ignores.
        /// </summary>
        /// <exception cref="ArgumentException">A value is not a finite number; JSON cannot carry it.</exception>
        public static string Write(MapGeometry geometry, IReadOnlyList<string> comments = null)
        {
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));

            var sb = new StringBuilder(1024);
            sb.Append("{\n");

            if (comments != null && comments.Count > 0)
            {
                sb.Append("  \"$comment\": [\n");
                for (var i = 0; i < comments.Count; i++)
                {
                    sb.Append("    ");
                    Quoted(sb, comments[i] ?? string.Empty);
                    sb.Append(i + 1 < comments.Count ? ",\n" : "\n");
                }

                sb.Append("  ],\n");
            }

            var b = geometry.Bounds;
            sb.Append("  \"bounds\": { \"minX\": ").Append(F(b.MinX))
              .Append(", \"minY\": ").Append(F(b.MinY))
              .Append(", \"maxX\": ").Append(F(b.MaxX))
              .Append(", \"maxY\": ").Append(F(b.MaxY)).Append(" }");

            var hf = geometry.HeightField;
            if (hf != null)
            {
                sb.Append(",\n  \"heightfield\": {\n");
                sb.Append("    \"originX\": ").Append(F(hf.OriginX))
                  .Append(", \"originY\": ").Append(F(hf.OriginY))
                  .Append(", \"cellSize\": ").Append(F(hf.CellSize))
                  .Append(", \"columns\": ").Append(I(hf.Columns))
                  .Append(", \"rows\": ").Append(I(hf.Rows)).Append(",\n");
                sb.Append("    \"heights\": [");
                var heights = hf.Heights;
                var columns = Math.Max(1, hf.Columns);
                for (var i = 0; i < heights.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    // One heightfield row per line: row-major, so a diff of an edited terrain
                    // points at the row that changed.
                    sb.Append(i % columns == 0 ? "\n      " : " ");
                    sb.Append(F(heights[i]));
                }

                sb.Append(heights.Length > 0 ? "\n    ]\n  }" : "]\n  }");
            }

            var boxes = geometry.Boxes;
            sb.Append(",\n  \"boxes\": [");
            for (var i = 0; i < boxes.Length; i++)
            {
                var box = boxes[i];
                sb.Append(i == 0 ? "\n" : ",\n");
                sb.Append("    { \"minX\": ").Append(F(box.MinX))
                  .Append(", \"minY\": ").Append(F(box.MinY))
                  .Append(", \"minZ\": ").Append(F(box.MinZ))
                  .Append(", \"maxX\": ").Append(F(box.MaxX))
                  .Append(", \"maxY\": ").Append(F(box.MaxY))
                  .Append(", \"maxZ\": ").Append(F(box.MaxZ)).Append(" }");
            }

            sb.Append(boxes.Length > 0 ? "\n  ]" : "]");

            var spawns = geometry.Spawns;
            sb.Append(",\n  \"spawns\": [");
            for (var i = 0; i < spawns.Length; i++)
            {
                var s = spawns[i];
                sb.Append(i == 0 ? "\n" : ",\n");
                sb.Append("    { \"name\": ");
                Quoted(sb, s.Name ?? string.Empty);
                sb.Append(", \"x\": ").Append(F(s.Position.X))
                  .Append(", \"y\": ").Append(F(s.Position.Y))
                  .Append(", \"z\": ").Append(F(s.Position.Z)).Append(" }");
            }

            sb.Append(spawns.Length > 0 ? "\n  ]" : "]");

            var portals = geometry.Portals;
            sb.Append(",\n  \"portals\": [");
            for (var i = 0; i < portals.Length; i++)
            {
                var portal = portals[i];
                sb.Append(i == 0 ? "\n" : ",\n");
                sb.Append("    { \"name\": ");
                Quoted(sb, portal.Name ?? string.Empty);
                sb.Append(", \"x\": ").Append(F(portal.Position.X))
                  .Append(", \"y\": ").Append(F(portal.Position.Y))
                  .Append(", \"z\": ").Append(F(portal.Position.Z))
                  .Append(", \"radius\": ").Append(F(portal.Radius))
                  .Append(", \"height\": ").Append(F(portal.Height))
                  .Append(", \"targetMapId\": ");
                Quoted(sb, portal.TargetMapId ?? string.Empty);
                sb.Append(", \"targetSpawn\": ");
                Quoted(sb, portal.TargetSpawn ?? string.Empty);
                sb.Append(" }");
            }

            sb.Append(portals.Length > 0 ? "\n  ]" : "]");
            sb.Append("\n}\n");
            return sb.ToString();
        }

        private static bool TryNumber(JsonValue obj, string name, out float value)
        {
            if (obj.TryGetMember(name, out var v) && v.Kind == JsonKind.Number)
            {
                value = (float)v.AsNumber();
                return true;
            }

            value = 0f;
            return false;
        }

        private static bool TryInt(JsonValue obj, string name, out int value)
        {
            if (obj.TryGetMember(name, out var v) && v.Kind == JsonKind.Number)
            {
                var d = v.AsNumber();
                if (d == Math.Floor(d) && d >= int.MinValue && d <= int.MaxValue)
                {
                    value = (int)d;
                    return true;
                }
            }

            value = 0;
            return false;
        }

        private static string F(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentException($"map value {value} is not a finite number; JSON cannot represent it.");
            }

            // "R": the shortest string that parses back to the same float, so a write/read
            // round trip is exact and a re-export of an unchanged scene is byte-identical.
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static void Quoted(StringBuilder sb, string value)
        {
            sb.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }

            sb.Append('"');
        }
    }
}
