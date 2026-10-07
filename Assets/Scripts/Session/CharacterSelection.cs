namespace Scripts.Session
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>One roster entry, as the selection logic needs it.</summary>
    public readonly struct CharacterEntry
    {
        public CharacterEntry(string id, string name, int slot)
        {
            this.Id = id ?? string.Empty;
            this.Name = name ?? string.Empty;
            this.Slot = slot;
        }

        public string Id { get; }

        public string Name { get; }

        public int Slot { get; }
    }

    /// <summary>What the session should do about a character.</summary>
    public readonly struct CharacterDecision
    {
        private CharacterDecision(bool create, string id, string name, string reason)
        {
            this.Create = create;
            this.Id = id ?? string.Empty;
            this.Name = name ?? string.Empty;
            this.Reason = reason ?? string.Empty;
        }

        /// <summary>True: create a character named <see cref="Name"/>. False: play <see cref="Id"/>.</summary>
        public bool Create { get; }

        /// <summary>The roster id to play; empty when <see cref="Create"/>.</summary>
        public string Id { get; }

        public string Name { get; }

        /// <summary>Why this one — for the log line, so a run says how it chose.</summary>
        public string Reason { get; }

        public static CharacterDecision Use(in CharacterEntry entry, string reason) =>
            new CharacterDecision(false, entry.Id, entry.Name, reason);

        public static CharacterDecision CreateNew(string name, string reason) =>
            new CharacterDecision(true, string.Empty, name, reason);
    }

    /// <summary>The character the session asks for, once chosen or created.</summary>
    public readonly struct CharacterChoice
    {
        public CharacterChoice(string id, string name, bool created)
        {
            this.Id = id ?? string.Empty;
            this.Name = name ?? string.Empty;
            this.Created = created;
        }

        /// <summary>Empty = the account's default character (no <c>cid</c>, protocol 2 behaviour).</summary>
        public string Id { get; }

        public string Name { get; }

        public bool Created { get; }

        public bool IsDefault => this.Id.Length == 0;

        public static CharacterChoice Default => new CharacterChoice(string.Empty, string.Empty, false);
    }

    /// <summary>
    /// Headless character selection (ADR-31): which roster character a session plays, as a pure
    /// function of the roster, the explicit request and the last character this account
    /// played on this device.
    /// </summary>
    /// <remarks>
    /// <para>Order, first match wins:</para>
    /// <list type="number">
    /// <item><description>An explicit request (<c>-cuvara-character</c> / <c>CUVARA_CHARACTER</c>)
    /// naming a roster character by id, then by name (case-insensitive).</description></item>
    /// <item><description>An explicit request naming no roster character but spelling a valid name:
    /// create it. A harness launching <c>-cuvara-character Scout1</c> gets that character on the
    /// first run and the same one on every later run.</description></item>
    /// <item><description>An explicit request that is neither: refused, loudly. Playing some other
    /// character than the one asked for is how a test run proves the wrong thing.</description></item>
    /// <item><description>An empty roster: create the default name (<see cref="DefaultName"/>).</description></item>
    /// <item><description>The last-used id, if it is still on the roster.</description></item>
    /// <item><description>The lowest slot.</description></item>
    /// </list>
    /// <para>
    /// The name rule mirrors the server's PLACEHOLDER rule (<c>character.ValidateName</c>: 3-16
    /// ASCII letters, digits and underscores). It is checked here only to choose between "create"
    /// and "refuse" for an explicit request; the server is the authority and refuses on its own.
    /// </para>
    /// </remarks>
    public static class CharacterSelection
    {
        public const int NameMinLength = 3;
        public const int NameMaxLength = 16;

        /// <summary>Prefix of the generated default name.</summary>
        public const string DefaultNamePrefix = "Hero";

        /// <summary>True when <paramref name="name"/> passes the server's placeholder name rule.</summary>
        public static bool IsValidName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length < NameMinLength || name.Length > NameMaxLength) return false;
            foreach (var c in name)
            {
                var ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';
                if (!ok) return false;
            }

            return true;
        }

        /// <summary>
        /// <c>Hero_</c> plus up to the first 11 letters/digits of <paramref name="userId"/>, so two
        /// accounts' first characters are told apart in a log; plain <c>Hero</c> when the id has
        /// none. Always passes <see cref="IsValidName"/>.
        /// </summary>
        public static string DefaultName(string userId)
        {
            var suffix = new StringBuilder();
            if (userId != null)
            {
                foreach (var c in userId)
                {
                    if (suffix.Length == NameMaxLength - DefaultNamePrefix.Length - 1) break;
                    if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) suffix.Append(c);
                }
            }

            return suffix.Length == 0 ? DefaultNamePrefix : DefaultNamePrefix + "_" + suffix;
        }

        /// <summary>Decides per the order in the class remarks.</summary>
        /// <exception cref="ArgumentException">An explicit request matches nothing and is not a valid name.</exception>
        public static CharacterDecision Decide(
            IReadOnlyList<CharacterEntry> roster, string requested, string lastUsedId, string userId)
        {
            roster ??= Array.Empty<CharacterEntry>();

            if (!string.IsNullOrWhiteSpace(requested))
            {
                requested = requested.Trim();
                foreach (var entry in roster)
                {
                    if (string.Equals(entry.Id, requested, StringComparison.Ordinal))
                        return CharacterDecision.Use(entry, "requested by id");
                }

                foreach (var entry in roster)
                {
                    if (string.Equals(entry.Name, requested, StringComparison.OrdinalIgnoreCase))
                        return CharacterDecision.Use(entry, "requested by name");
                }

                if (IsValidName(requested))
                    return CharacterDecision.CreateNew(requested, "requested name not on the roster");

                throw new ArgumentException(
                    $"requested character '{requested}' is neither on this account's roster (by id or name) nor a " +
                    $"valid new name ({NameMinLength}-{NameMaxLength} letters, digits or '_')", nameof(requested));
            }

            if (roster.Count == 0)
            {
                return CharacterDecision.CreateNew(DefaultName(userId), "empty roster");
            }

            if (!string.IsNullOrEmpty(lastUsedId))
            {
                foreach (var entry in roster)
                {
                    if (string.Equals(entry.Id, lastUsedId, StringComparison.Ordinal))
                        return CharacterDecision.Use(entry, "last used");
                }
            }

            var first = roster[0];
            for (var i = 1; i < roster.Count; i++)
            {
                if (roster[i].Slot < first.Slot) first = roster[i];
            }

            return CharacterDecision.Use(first, "first slot");
        }
    }
}
