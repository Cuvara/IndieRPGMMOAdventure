namespace Scripts.Gameplay.Content
{
    using System;
    using System.Collections.Generic;
    using Cuvara.Netcode.Content;
    using Cuvara.Netcode.Json;
    using Shared.GameLogic.Content;

    /// <summary>
    /// The protocol 3 content the client reads by KEY rather than by number: stat ids
    /// (<c>stats.json</c>), status ids (<c>statuses.json</c>), abilities
    /// (<c>abilities.json</c>) and the item definitions (<c>items.json</c>), all taken from the
    /// one composed document the game server serves at <c>/content</c> (ADR-19, ADR-30).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why ids are resolved by key.</b> A stat id is permanent once shipped, but which id
    /// "mana" got is a content decision, not a client constant. The HUD asks
    /// <see cref="TryGetStatId"/> for <c>"level"</c> and <c>"mana"</c>; hard-coding 1 and 2
    /// would be correct only until the first content edit.
    /// </para>
    /// <para>
    /// <b>Items</b> go through the netcode package's <see cref="ContentJsonReader"/>, so they are
    /// the shared <see cref="ItemDefinition"/> type validated by the shared rules. The netcode
    /// reader (0.46.0) reads only <c>items</c>; stats, statuses and abilities are read here, with
    /// only the fields the client uses. When the package reader learns the other three, this
    /// class shrinks to a view over its <see cref="ContentDatabase"/>.
    /// </para>
    /// </remarks>
    public sealed class GameContentCatalog
    {
        /// <summary>One ability, as far as the client needs to know it.</summary>
        public sealed class AbilityInfo
        {
            public uint Id;
            public string Name;

            /// <summary><c>self</c>, <c>entity</c>, <c>ground</c> or <c>projectile</c>.</summary>
            public string Delivery;

            public float Range;
            public float Radius;
            public float ProjectileSpeed;
            public float ProjectileRadius;
            public float ProjectileRange;

            public bool IsProjectile => string.Equals(Delivery, "projectile", StringComparison.Ordinal);
        }

        private readonly Dictionary<string, uint> statIdsByKey = new Dictionary<string, uint>(StringComparer.Ordinal);
        private readonly Dictionary<uint, string> statKeysById = new Dictionary<uint, string>();
        private readonly Dictionary<uint, string> statusKeysById = new Dictionary<uint, string>();
        private readonly List<AbilityInfo> abilities = new List<AbilityInfo>();

        private GameContentCatalog(string hash)
        {
            this.Hash = hash ?? string.Empty;
        }

        /// <summary>An empty catalog: nothing resolves. What the client holds before a fetch.</summary>
        public static GameContentCatalog Empty { get; } = new GameContentCatalog(string.Empty);

        /// <summary>The content hash the server reported, or empty.</summary>
        public string Hash { get; }

        /// <summary>Items, as the shared schema. Empty when the document carried none.</summary>
        public ContentDatabase Items { get; private set; } = ContentDatabase.Empty;

        public int StatCount => this.statIdsByKey.Count;

        public int StatusCount => this.statusKeysById.Count;

        public IReadOnlyList<AbilityInfo> Abilities => this.abilities;

        /// <summary>Resolves a stat key (<c>"level"</c>, <c>"mana"</c>) to its content id.</summary>
        public bool TryGetStatId(string key, out uint id)
        {
            id = 0u;
            return key != null && this.statIdsByKey.TryGetValue(key, out id);
        }

        /// <summary>The key of a stat id, or null.</summary>
        public string StatKey(uint id) => this.statKeysById.TryGetValue(id, out var key) ? key : null;

        /// <summary>The key of a status id (<c>"burning"</c>), or null when the content does not define it.</summary>
        public string StatusKey(uint id) => this.statusKeysById.TryGetValue(id, out var key) ? key : null;

        /// <summary>An ability by id, or null.</summary>
        public AbilityInfo Ability(uint id)
        {
            foreach (var ability in this.abilities)
            {
                if (ability.Id == id) return ability;
            }

            return null;
        }

        /// <summary>The lowest-id projectile ability, or null: what the client binds to its cast key.</summary>
        public AbilityInfo FirstProjectileAbility()
        {
            AbilityInfo best = null;
            foreach (var ability in this.abilities)
            {
                if (ability.IsProjectile && (best == null || ability.Id < best.Id)) best = ability;
            }

            return best;
        }

        /// <summary>
        /// Parses a content document: the composed <c>/content</c> body, or any single content
        /// file. Missing sections are empty, not errors — the server serves only the files it
        /// has. A section that is present but malformed is an error.
        /// </summary>
        public static bool TryParse(string json, string hash, out GameContentCatalog catalog, out string error)
        {
            catalog = null;
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "content document is empty";
                return false;
            }

            JsonValue root;
            try
            {
                root = JsonParser.Parse(json);
            }
            catch (JsonParseException ex)
            {
                error = "content is not valid JSON: " + ex.Message;
                return false;
            }

            if (root.Kind != JsonKind.Object)
            {
                error = "content document must be a JSON object";
                return false;
            }

            var result = new GameContentCatalog(hash);

            var stats = root.GetArray("stats");
            for (var i = 0; i < stats.Count; i++)
            {
                var id = stats[i].GetUInt("id");
                var key = stats[i].GetString("key");
                if (id == 0u || string.IsNullOrEmpty(key))
                {
                    error = $"stats[{i}] needs an id >= 1 and a key";
                    return false;
                }

                if (result.statIdsByKey.ContainsKey(key) || result.statKeysById.ContainsKey(id))
                {
                    error = $"stats[{i}] ('{key}', id {id}) duplicates another stat's key or id";
                    return false;
                }

                result.statIdsByKey[key] = id;
                result.statKeysById[id] = key;
            }

            var statuses = root.GetArray("statuses");
            for (var i = 0; i < statuses.Count; i++)
            {
                var id = statuses[i].GetUInt("id");
                var key = statuses[i].GetString("key");
                if (id == 0u || string.IsNullOrEmpty(key))
                {
                    error = $"statuses[{i}] needs an id >= 1 and a key";
                    return false;
                }

                result.statusKeysById[id] = key;
            }

            var rawAbilities = root.GetArray("abilities");
            for (var i = 0; i < rawAbilities.Count; i++)
            {
                var a = rawAbilities[i];
                var info = new AbilityInfo
                {
                    Id = a.GetUInt("id"),
                    Name = a.GetString("name"),
                    Delivery = a.GetString("delivery"),
                    Range = a.GetFloat("range"),
                    Radius = a.GetFloat("radius"),
                };

                if (info.Id == 0u)
                {
                    error = $"abilities[{i}] needs an id >= 1";
                    return false;
                }

                if (a.TryGetMember("projectile", out var projectile) && projectile.Kind == JsonKind.Object)
                {
                    info.ProjectileSpeed = projectile.GetFloat("speed");
                    info.ProjectileRadius = projectile.GetFloat("radius");
                    info.ProjectileRange = projectile.GetFloat("range");
                }

                if (info.IsProjectile && !(info.ProjectileSpeed > 0f && info.ProjectileRange > 0f))
                {
                    error = $"ability {info.Id} is a projectile but has no usable projectile block (speed, radius, range)";
                    return false;
                }

                result.abilities.Add(info);
            }

            if (root.TryGetMember("items", out _))
            {
                if (!ContentJsonReader.TryRead(json, result.Hash, out var database, out var itemError))
                {
                    error = itemError;
                    return false;
                }

                result.Items = database;
            }

            catalog = result;
            return true;
        }
    }
}
