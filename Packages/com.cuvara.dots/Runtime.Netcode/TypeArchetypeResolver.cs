using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cuvara.DOTS.Netcode
{
    /// <summary>
    /// Maps the server's entity kind to an archetype name, with an optional separate archetype for
    /// the local player.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exact, ordinal matching on <c>NetworkEntityDescriptor.Type</c> — no prefixes, no
    /// case-folding, no fuzzy fallback. The type is a wire enum in string clothing: the server sends
    /// <c>"mob"</c> or it does not, and treating <c>"Mob"</c> as a match would paper over a schema
    /// disagreement that should be visible.
    /// </para>
    /// <para>
    /// <b>The local override wins over the type rule when both apply.</b> <c>IsLocal</c> is derived
    /// by comparing the entity id with the client's own <c>NetworkClient.UserId</c>, so it is the
    /// one field in a snapshot that does not depend on the server's vocabulary matching what this
    /// build expects — and presenting the player's own avatar as something else is the single most
    /// visible way this layer can be wrong. The useful case is ordinary: type <c>"player"</c> plus
    /// <c>IsLocal</c> resolving to a distinct local archetype. The incoherent case — a
    /// <c>"mob"</c> whose id is the local player's — is server confusion, and the override answers
    /// it with the client's own belief rather than the server's.
    /// </para>
    /// <para>
    /// <b>An unrecognised or empty type is refused, loudly, once.</b> Not mapped to a "default"
    /// archetype silently: a build talking to a newer server, or to one that never populated the
    /// field, would then render every unknown kind as a player and look like it was working. A
    /// consumer that genuinely wants a catch-all passes <paramref name="unknownArchetype"/> to the
    /// constructor and has said so out loud.
    /// </para>
    /// </remarks>
    public sealed class TypeArchetypeResolver : INetworkArchetypeResolver
    {
        /// <summary>One "entities of this server kind are that archetype" rule.</summary>
        public readonly struct Rule
        {
            /// <summary>The server's entity kind, matched exactly.</summary>
            public readonly string Type;

            /// <summary>The archetype name, as authored in the library.</summary>
            public readonly string ArchetypeName;

            public Rule(string type, string archetypeName)
            {
                Type = type;
                ArchetypeName = archetypeName;
            }
        }

        private readonly Dictionary<string, string> _byType;
        private readonly HashSet<string> _reported = new HashSet<string>();
        private readonly string _localArchetype;
        private readonly string _unknownArchetype;

        /// <param name="localArchetype">
        /// Archetype for the local player, whatever kind the server called it. Null or empty means
        /// the local player is presented by its type like anything else.
        /// </param>
        /// <param name="unknownArchetype">
        /// Catch-all for a kind no rule names, including the empty kind. Null or empty means such an
        /// entity is not presented at all, and the first occurrence of each unmapped kind is logged.
        /// </param>
        /// <param name="rules">
        /// Type → archetype mappings. A duplicate type throws rather than silently letting list
        /// order decide, because the two rules are equally plausible and picking one is a coin flip
        /// nobody would want made for them.
        /// </param>
        /// <remarks>
        /// One constructor, taking the rules last as <c>params</c>. An <c>IReadOnlyList</c> overload
        /// was written and removed: with both present, <c>new TypeArchetypeResolver(null, "x")</c>
        /// binds to neither unambiguously (CS0121), and the two-string call is the one a caller with
        /// no rules writes first. A caller holding a list passes <c>.ToArray()</c>.
        /// </remarks>
        public TypeArchetypeResolver(string localArchetype, string unknownArchetype, params Rule[] rules)
        {
            rules = rules ?? Array.Empty<Rule>();

            _byType = new Dictionary<string, string>(rules.Length, StringComparer.Ordinal);
            for (var i = 0; i < rules.Length; i++)
            {
                var rule = rules[i];
                if (string.IsNullOrEmpty(rule.Type)) throw new ArgumentException($"Rule {i} has no type.", nameof(rules));
                if (string.IsNullOrEmpty(rule.ArchetypeName)) throw new ArgumentException($"Rule for type '{rule.Type}' has no archetype name.", nameof(rules));
                if (_byType.ContainsKey(rule.Type)) throw new ArgumentException($"Type '{rule.Type}' is mapped twice.", nameof(rules));

                _byType.Add(rule.Type, rule.ArchetypeName);
            }

            _localArchetype = string.IsNullOrEmpty(localArchetype) ? null : localArchetype;
            _unknownArchetype = string.IsNullOrEmpty(unknownArchetype) ? null : unknownArchetype;
        }

        /// <summary>The archetype the local player is presented as, or null when it has no override.</summary>
        public string LocalArchetype => _localArchetype;

        /// <summary>The catch-all archetype, or null when an unmapped kind is refused.</summary>
        public string UnknownArchetype => _unknownArchetype;

        /// <summary>Number of type rules.</summary>
        public int RuleCount => _byType.Count;

        public bool TryResolve(in NetworkEntityDescriptor entity, out string archetypeName)
        {
            if (_localArchetype != null && entity.IsLocal)
            {
                archetypeName = _localArchetype;
                return true;
            }

            if (entity.HasType && _byType.TryGetValue(entity.Type, out archetypeName))
            {
                return true;
            }

            if (_unknownArchetype != null)
            {
                archetypeName = _unknownArchetype;
                return true;
            }

            // Once per distinct kind, not once per spawn: an unmapped kind affects every entity of
            // that kind, and a snapshot-rate log would bury the one line that matters. Reported here
            // rather than by the adapter because this is the object that knows a *kind* went
            // unrecognised — the adapter only sees a refusal.
            var reportKey = entity.HasType ? entity.Type : string.Empty;
            if (_reported.Add(reportKey))
            {
                Debug.LogError(entity.HasType
                    ? $"[Cuvara.DOTS] No archetype is mapped for server entity type '{entity.Type}'; " +
                      "entities of that kind will not be presented. Add a rule, or pass unknownArchetype."
                    : "[Cuvara.DOTS] The server sent entities with no type; they will not be presented. " +
                      "Pass unknownArchetype if this server predates typed snapshots.");
            }

            archetypeName = null;
            return false;
        }
    }
}
