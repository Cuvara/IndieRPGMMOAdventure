namespace Scripts.Gameplay.Presentation
{
    using System;
    using System.Collections.Generic;
    using Shared.GameLogic.Components;

    /// <summary>Finds the nearest replicated entity of a kind — what the pickup key targets.</summary>
    public static class NearestEntity
    {
        /// <summary>Server entity kind of a dropped item (ADR-30 loot).</summary>
        public const string ItemType = "item";

        /// <summary>Server entity kind of a projectile (ADR-29).</summary>
        public const string ProjectileType = "projectile";

        /// <summary>
        /// The id of the entity of <paramref name="type"/> nearest to (<paramref name="x"/>,
        /// <paramref name="y"/>) on the ground plane within <paramref name="maxRange"/>, or null.
        /// Ties go to the lower id so the answer does not depend on dictionary order.
        /// </summary>
        public static string Find(IEnumerable<KeyValuePair<string, EntitySnapshotData>> entities, string type, float x, float y, float maxRange)
        {
            if (entities == null) return null;

            string best = null;
            var bestDistSq = maxRange * maxRange;
            foreach (var pair in entities)
            {
                var e = pair.Value;
                if (!string.Equals(e.Type, type, StringComparison.Ordinal)) continue;

                var dx = e.X - x;
                var dy = e.Y - y;
                var distSq = dx * dx + dy * dy;
                if (distSq > bestDistSq) continue;
                if (distSq == bestDistSq && best != null && string.CompareOrdinal(pair.Key, best) > 0) continue;

                best = pair.Key;
                bestDistSq = distSq;
            }

            return best;
        }
    }
}
