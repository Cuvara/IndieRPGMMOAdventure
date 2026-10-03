namespace Scripts.Gameplay.Stats
{
    using System;
    using System.Globalization;
    using System.Text;
    using Scripts.Gameplay.Content;
    using Shared.GameLogic.Components;

    /// <summary>
    /// The local player's replicated stat block and statuses (ADR-30 decisions 1 and 2), turned
    /// into the few values the HUD shows. Plain data; compare with <see cref="Equals(PlayerStatsSnapshot)"/>
    /// to push only on change.
    /// </summary>
    public readonly struct PlayerStatsSnapshot : IEquatable<PlayerStatsSnapshot>
    {
        public const string StatLevel = "level";
        public const string StatMana = "mana";

        public PlayerStatsSnapshot(bool present, int? level, int? mana, string statuses)
        {
            this.Present = present;
            this.Level = level;
            this.Mana = mana;
            this.Statuses = statuses ?? string.Empty;
        }

        /// <summary>False while the local player is not in the world.</summary>
        public bool Present { get; }

        /// <summary>The stat keyed <c>"level"</c>; null when the content or the snapshot lacks it.</summary>
        public int? Level { get; }

        /// <summary>The stat keyed <c>"mana"</c>; null when the content or the snapshot lacks it.</summary>
        public int? Mana { get; }

        /// <summary>"burning x2 (2.5s), rooted" — empty when no status is active.</summary>
        public string Statuses { get; }

        public string LevelCaption => this.Level.HasValue ? "Level " + this.Level.Value.ToString(CultureInfo.InvariantCulture) : "Level —";

        public string ManaCaption => this.Mana.HasValue ? "Mana " + this.Mana.Value.ToString(CultureInfo.InvariantCulture) : "Mana —";

        public string StatusCaption => this.Statuses.Length == 0 ? "No status effects" : this.Statuses;

        /// <summary>
        /// Reads <paramref name="entity"/>'s merged stat block and statuses. Stat ids are resolved
        /// from their content KEYS through <paramref name="catalog"/>, never from numbers baked
        /// into the client; a status the content does not name is shown by its id.
        /// </summary>
        /// <param name="worldTick">The world's current tick, to turn <c>expires_tick</c> into seconds.</param>
        /// <param name="tickRate">Server ticks per second (the join's advertised rate); 0 hides durations.</param>
        public static PlayerStatsSnapshot From(in EntitySnapshotData entity, GameContentCatalog catalog, long worldTick, uint tickRate)
        {
            catalog ??= GameContentCatalog.Empty;

            int? level = null;
            int? mana = null;
            var levelKnown = catalog.TryGetStatId(StatLevel, out var levelId);
            var manaKnown = catalog.TryGetStatId(StatMana, out var manaId);

            var stats = entity.Stats;
            if (stats != null)
            {
                foreach (var stat in stats)
                {
                    if (levelKnown && stat.StatId == levelId) level = stat.Value;
                    else if (manaKnown && stat.StatId == manaId) mana = stat.Value;
                }
            }

            var text = new StringBuilder();
            var statuses = entity.Statuses;
            if (statuses != null)
            {
                foreach (var status in statuses)
                {
                    if (text.Length > 0) text.Append(", ");
                    text.Append(catalog.StatusKey(status.EffectId) ?? "status " + status.EffectId.ToString(CultureInfo.InvariantCulture));
                    if (status.Stacks > 1) text.Append(" x").Append(status.Stacks.ToString(CultureInfo.InvariantCulture));

                    // expires_tick 0 means "until removed"; a tick already passed reads as 0s
                    // until the server's removal arrives.
                    if (status.ExpiresTick > 0 && tickRate > 0)
                    {
                        var remaining = Math.Max(0d, ((double)status.ExpiresTick - worldTick) / tickRate);
                        text.Append(" (").Append(remaining.ToString("0.0", CultureInfo.InvariantCulture)).Append("s)");
                    }
                }
            }

            return new PlayerStatsSnapshot(true, level, mana, text.ToString());
        }

        public static PlayerStatsSnapshot Absent => new PlayerStatsSnapshot(false, null, null, string.Empty);

        public bool Equals(PlayerStatsSnapshot other) =>
            this.Present == other.Present && this.Level == other.Level && this.Mana == other.Mana &&
            string.Equals(this.Statuses, other.Statuses, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is PlayerStatsSnapshot other && this.Equals(other);

        public override int GetHashCode() => (this.Level ?? -1) * 397 ^ (this.Mana ?? -1) ^ this.Statuses.GetHashCode();
    }
}
