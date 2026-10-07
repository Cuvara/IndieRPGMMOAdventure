using System;
using System.Collections.Generic;
using System.Threading;
using Cuvara.Netcode.Json;
using Cysharp.Threading.Tasks;
using Nakama;

namespace Scripts.Nakama.Characters
{
    /// <summary>One roster character, as the Nakama <c>character</c> module reports it (ADR-31).</summary>
    public readonly struct CharacterInfo
    {
        public CharacterInfo(string id, int slot, string name, long createdAt)
        {
            Id = id ?? string.Empty;
            Slot = slot;
            Name = name ?? string.Empty;
            CreatedAt = createdAt;
        }

        /// <summary>UUID minted by Nakama; the key of the character's state on the game server.</summary>
        public string Id { get; }

        public int Slot { get; }

        public string Name { get; }

        /// <summary>Unix seconds.</summary>
        public long CreatedAt { get; }

        public override string ToString() => $"{Name} (slot {Slot}, id {Id})";
    }

    /// <summary>The account's roster: characters ordered by slot, and the slot cap.</summary>
    public readonly struct CharacterRoster
    {
        public CharacterRoster(IReadOnlyList<CharacterInfo> characters, int maxSlots)
        {
            Characters = characters ?? Array.Empty<CharacterInfo>();
            MaxSlots = maxSlots;
        }

        public IReadOnlyList<CharacterInfo> Characters { get; }

        /// <summary>The server's PLACEHOLDER cap (4 today); a label, the server enforces it.</summary>
        public int MaxSlots { get; }
    }

    /// <summary>
    /// The server answered and refused (<c>invalid character name</c>, <c>character roster is
    /// full</c>, ...), as opposed to a transport failure, which is an
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    public sealed class CharacterException : Exception
    {
        public CharacterException(string message, Exception inner = null) : base(message, inner) { }
    }

    /// <summary>
    /// The client half of the character roster (ADR-31): the <c>character_list</c>,
    /// <c>character_create</c> and <c>character_delete</c> Nakama RPCs
    /// (<c>rpg-mmo-server/backend/nakama/docs/API.md</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Nakama owns the roster; the game server owns the character.</b> Deleting here removes
    /// the roster entry only; the character's state, bag and equipment stay in the game-state
    /// database (the plugin makes no outbound call). Playing a character is a separate step: its
    /// id goes in the <c>gateway_token</c> payload (<see cref="CharacterSelectionState"/>) and in
    /// <c>NetworkClient.CharacterId</c>.
    /// </para>
    /// <para>
    /// Rate limits are server-side (roster write 0.2/s burst 5, read 1/s burst 10): this class
    /// does not retry, so a caller looping on <see cref="CreateAsync"/> is refused with
    /// <c>rate limited</c> rather than hidden behind a backoff.
    /// </para>
    /// </remarks>
    public sealed class CharacterService
    {
        public const string ListRpc = "character_list";
        public const string CreateRpc = "character_create";
        public const string DeleteRpc = "character_delete";

        readonly NakamaSessionService _nakama;

        public CharacterService(NakamaSessionService nakama)
        {
            _nakama = nakama ?? throw new ArgumentNullException(nameof(nakama));
        }

        /// <summary>The caller's roster, ordered by slot. Empty for a new account.</summary>
        public async UniTask<CharacterRoster> ListAsync(CancellationToken ct = default)
        {
            string json = await RpcAsync(ListRpc, "{}", ct);
            return ParseRoster(json);
        }

        /// <summary>
        /// Creates a character named <paramref name="name"/> in <paramref name="slot"/>, or the
        /// lowest free slot when <paramref name="slot"/> is negative.
        /// </summary>
        public async UniTask<CharacterInfo> CreateAsync(string name, int slot = -1, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("name must not be empty", nameof(name));
            string json = await RpcAsync(CreateRpc, CreatePayload(name, slot), ct);
            return ParseCreated(json);
        }

        /// <summary>Removes <paramref name="characterId"/> from the roster.</summary>
        public async UniTask DeleteAsync(string characterId, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(characterId)) throw new ArgumentException("character id must not be empty", nameof(characterId));
            string payload = new JsonBuilder().BeginObject().String("character_id", characterId).EndObject().ToString();
            string json = await RpcAsync(DeleteRpc, payload, ct);

            JsonValue root = Parse(DeleteRpc, json);
            if (!root.GetBool("deleted"))
            {
                throw new CharacterException($"Nakama RPC '{DeleteRpc}' did not confirm the delete: {json}");
            }
        }

        // ---- payloads (public static: the contract with backend/nakama/character, tested) ----

        /// <summary><c>{"name":..., "slot":N}</c>; <c>slot</c> omitted when negative (= lowest free).</summary>
        public static string CreatePayload(string name, int slot)
        {
            var builder = new JsonBuilder().BeginObject().String("name", name);
            if (slot >= 0) builder.Number("slot", (long)slot);
            return builder.EndObject().ToString();
        }

        /// <summary>Parses a <c>character_list</c> response.</summary>
        public static CharacterRoster ParseRoster(string json)
        {
            JsonValue root = Parse(ListRpc, json);
            var raw = root.GetArray("characters");
            var characters = new List<CharacterInfo>(raw.Count);
            for (var i = 0; i < raw.Count; i++)
            {
                var character = ToInfo(raw[i]);
                if (character.Id.Length > 0) characters.Add(character);
            }

            characters.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            return new CharacterRoster(characters, root.GetInt("max_slots"));
        }

        /// <summary>Parses a <c>character_create</c> response.</summary>
        public static CharacterInfo ParseCreated(string json)
        {
            JsonValue root = Parse(CreateRpc, json);
            if (!root.TryGetMember("character", out var character) || character.Kind != JsonKind.Object)
            {
                throw new CharacterException($"Nakama RPC '{CreateRpc}' returned no 'character': {json}");
            }

            var info = ToInfo(character);
            if (info.Id.Length == 0)
            {
                throw new CharacterException($"Nakama RPC '{CreateRpc}' returned a character with no id: {json}");
            }

            return info;
        }

        static CharacterInfo ToInfo(JsonValue value) =>
            new CharacterInfo(value.GetString("id"), value.GetInt("slot"), value.GetString("name"), value.GetLong("created_at"));

        static JsonValue Parse(string rpc, string json)
        {
            try
            {
                var root = JsonParser.Parse(json);
                if (root.Kind != JsonKind.Object) throw new CharacterException($"Nakama RPC '{rpc}' returned a non-object: {json}");
                return root;
            }
            catch (JsonParseException ex)
            {
                throw new CharacterException($"Nakama RPC '{rpc}' returned a payload that is not JSON: {json}", ex);
            }
        }

        async UniTask<string> RpcAsync(string rpc, string payload, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            ISession session = _nakama.Session;
            if (session == null)
            {
                throw new InvalidOperationException($"no Nakama session; authenticate before calling '{rpc}'.");
            }

            IApiRpc result;
            try
            {
                result = await _nakama.Client.RpcAsync(session, rpc, payload, canceller: ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ApiResponseException ex)
            {
                // The server answered and said no: a game rule (name, slot, cap) or an
                // unregistered RPC on an older Nakama. Either way it is the server's answer.
                throw new CharacterException($"Nakama RPC '{rpc}' was refused: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Nakama RPC '{rpc}' failed; verify the character module is registered on the Nakama server. " +
                    $"Cause: {ex.Message}", ex);
            }

            string json = result?.Payload;
            if (string.IsNullOrEmpty(json))
            {
                throw new CharacterException($"Nakama RPC '{rpc}' returned an empty payload.");
            }

            return json;
        }
    }
}
