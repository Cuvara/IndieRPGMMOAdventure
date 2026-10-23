namespace TestLab
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using Cuvara.Netcode.Snapshot;

    /// <summary>
    /// <c>entity_added</c> / <c>entity_removed</c> events from the snapshots this client receives,
    /// with the position the entity was first rendered at. Pure (no Unity runtime), so it is tested
    /// in EditMode.
    /// </summary>
    /// <remarks>
    /// <para><b>Added</b> = an id in a snapshot that this session has not seen live. The event is
    /// written when the entity is first <i>rendered</i>, so it carries both where the server said it
    /// was (<c>x</c>, <c>y</c>, at <c>tick</c>) and where it was first drawn (<c>rx</c>, <c>ry</c>,
    /// at <c>frame</c>). An entity removed before it was ever drawn still gets its
    /// <c>entity_added</c>, with null rendered fields, right before its <c>entity_removed</c>.</para>
    /// <para><b>Removed</b> = an id in the snapshot's removed list (<c>reason: "removed"</c>), or a
    /// live id absent from a keyframe (<c>reason: "keyframe"</c>).</para>
    /// <para><b>Bounded</b>: one entry per live entity; an entry leaves with its entity. A keyframe
    /// costs one pass over its entities; nothing is proportional to the run's history.</para>
    /// </remarks>
    public sealed class EntityLifecycle
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private readonly Dictionary<string, Entry> live = new Dictionary<string, Entry>();
        private readonly HashSet<string> inKeyframe = new HashSet<string>();
        private readonly List<string> scratch = new List<string>();
        private readonly Action<string, string> emit;
        private readonly StringBuilder sb = new StringBuilder(256);
        private int unrendered;

        /// <param name="emit">Receives (event kind, JSON fields without braces) for <c>events.ndjson</c>.</param>
        public EntityLifecycle(Action<string, string> emit)
        {
            this.emit = emit ?? throw new ArgumentNullException(nameof(emit));
        }

        public int Live => this.live.Count;

        public long Added { get; private set; }

        public long Removed { get; private set; }

        /// <summary>True while some live entity has not been drawn yet (the runner skips lookups otherwise).</summary>
        public bool AwaitingRender => this.unrendered > 0;

        public void OnSnapshot(long tick, bool full, IReadOnlyList<ResolvedEntity> entities, IReadOnlyList<string> removed)
        {
            if (entities != null)
            {
                if (full)
                {
                    this.inKeyframe.Clear();
                }

                for (int i = 0; i < entities.Count; i++)
                {
                    var e = entities[i];
                    if (full)
                    {
                        this.inKeyframe.Add(e.Id);
                    }

                    if (!this.live.ContainsKey(e.Id))
                    {
                        this.live[e.Id] = new Entry(e.Type, tick, e.X, e.Y);
                        this.unrendered++;
                        this.Added++;
                    }
                }
            }

            if (removed != null)
            {
                for (int i = 0; i < removed.Count; i++)
                {
                    this.Remove(removed[i], tick, "removed");
                }
            }

            if (full)
            {
                this.scratch.Clear();
                foreach (var id in this.live.Keys)
                {
                    if (!this.inKeyframe.Contains(id))
                    {
                        this.scratch.Add(id);
                    }
                }

                foreach (var id in this.scratch)
                {
                    this.Remove(id, tick, "keyframe");
                }
            }
        }

        /// <summary>An entity drawn this frame, at wire-space (<paramref name="rx"/>, <paramref name="ry"/>).</summary>
        public void OnRendered(string id, float rx, float ry, int frame)
        {
            if (this.unrendered == 0 || !this.live.TryGetValue(id, out var entry) || entry.Rendered)
            {
                return;
            }

            entry.Rendered = true;
            this.live[id] = entry;
            this.unrendered--;
            this.EmitAdded(id, entry, rx, ry, frame);
        }

        /// <summary>A new session: ids from the old one mean nothing now. Emits nothing per entity.</summary>
        public int Reset()
        {
            int count = this.live.Count;
            this.live.Clear();
            this.unrendered = 0;
            return count;
        }

        private void Remove(string id, long tick, string reason)
        {
            if (!this.live.TryGetValue(id, out var entry))
            {
                return;
            }

            if (!entry.Rendered)
            {
                this.unrendered--;
                this.EmitAdded(id, entry, float.NaN, float.NaN, -1);
            }

            this.live.Remove(id);
            this.Removed++;
            this.sb.Clear();
            this.sb.Append("\"id\":").Append(Quote(id))
                .Append(",\"type\":").Append(Quote(entry.Type))
                .Append(",\"tick\":").Append(tick.ToString(Inv))
                .Append(",\"added_tick\":").Append(entry.Tick.ToString(Inv))
                .Append(",\"rendered\":").Append(entry.Rendered ? "true" : "false")
                .Append(",\"reason\":").Append(Quote(reason));
            this.emit("entity_removed", this.sb.ToString());
        }

        private void EmitAdded(string id, Entry entry, float rx, float ry, int frame)
        {
            this.sb.Clear();
            this.sb.Append("\"id\":").Append(Quote(id))
                .Append(",\"type\":").Append(Quote(entry.Type))
                .Append(",\"tick\":").Append(entry.Tick.ToString(Inv))
                .Append(",\"x\":").Append(Num(entry.X))
                .Append(",\"y\":").Append(Num(entry.Y))
                .Append(",\"rx\":").Append(Num(rx))
                .Append(",\"ry\":").Append(Num(ry))
                .Append(",\"frame\":").Append(frame >= 0 ? frame.ToString(Inv) : "null");
            this.emit("entity_added", this.sb.ToString());
        }

        private static string Num(float v) =>
            float.IsNaN(v) || float.IsInfinity(v) ? "null" : v.ToString("R", Inv);

        internal static string Quote(string s)
        {
            if (s == null)
            {
                return "null";
            }

            var b = new StringBuilder(s.Length + 2).Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    default:
                        if (c < 0x20)
                        {
                            b.Append("\\u").Append(((int)c).ToString("x4", Inv));
                        }
                        else
                        {
                            b.Append(c);
                        }

                        break;
                }
            }

            return b.Append('"').ToString();
        }

        private struct Entry
        {
            public readonly string Type;
            public readonly long Tick;
            public readonly float X;
            public readonly float Y;
            public bool Rendered;

            public Entry(string type, long tick, float x, float y)
            {
                this.Type = type;
                this.Tick = tick;
                this.X = x;
                this.Y = y;
                this.Rendered = false;
            }
        }
    }
}
