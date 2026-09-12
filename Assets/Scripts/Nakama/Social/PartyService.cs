using System;
using System.Collections.Generic;
using System.Threading;
using Cuvara.Netcode.Json;
using Cysharp.Threading.Tasks;
using Nakama;

namespace Scripts.Nakama.Social
{
    /// <summary>
    /// A party, as the Nakama <c>social</c> module reports it.
    /// </summary>
    public readonly struct PartyInfo
    {
        public PartyInfo(string partyId, string leaderId, IReadOnlyList<string> members)
        {
            PartyId = partyId;
            LeaderId = leaderId;
            Members = members ?? Array.Empty<string>();
        }

        public string PartyId { get; }

        /// <summary>Always one of <see cref="Members"/>; the server holds that invariant.</summary>
        public string LeaderId { get; }

        public IReadOnlyList<string> Members { get; }

        /// <summary>Whether this party has room for another member.</summary>
        public bool HasRoom => Members.Count < PartyService.MaxMembers;

        public bool Contains(string userId)
        {
            for (var i = 0; i < Members.Count; i++)
            {
                if (Members[i] == userId) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Thrown when the party RPC refused the request. Distinguish this from a transport
    /// failure: this one means the server answered and said no.
    /// </summary>
    public sealed class PartyException : Exception
    {
        public PartyException(string message, Exception inner = null) : base(message, inner) { }
    }

    /// <summary>
    /// The client half of the party API (roadmap C2): four RPCs on the Nakama
    /// <c>social</c> module.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Parties live in Nakama, not in the gateway.</b> The gateway asks Nakama whether a
    /// player is really in the party they named, once, before it allocates a dungeon instance
    /// (ADR-26 decision 3) — so the party id this class returns is the one to hand to
    /// <c>NetworkClient.ConnectToDungeonAsync</c>, and a party this client has not actually
    /// joined will be refused there rather than here.
    /// </para>
    /// <para>
    /// <b>Storage RPCs, not Nakama's realtime Party API.</b> This client has no Nakama socket —
    /// its two realtime connections go to the gateway and the game server (ADR-3) — and
    /// realtime party state cannot answer a server-to-server question, which is the one thing
    /// the gateway needs.
    /// </para>
    /// <para>
    /// <b>A player is in at most one party.</b> Joining while already in a different party
    /// fails rather than silently moving the caller; re-joining the party you are already in
    /// succeeds, so a client retrying after a timeout is not told it is already in a party
    /// about the party it just asked for.
    /// </para>
    /// </remarks>
    public sealed class PartyService
    {
        /// <summary>
        /// The server's cap, mirrored here only so a UI can render "3/4" before a refusal.
        /// The **server** enforces it under a version check; this constant is a label, and a
        /// client that ignored it would still be refused.
        /// </summary>
        public const int MaxMembers = 4;

        const string CreateRpc = "party_create";
        const string JoinRpc = "party_join";
        const string LeaveRpc = "party_leave";
        const string GetRpc = "party_get";

        readonly NakamaSessionService _nakama;

        public PartyService(NakamaSessionService nakama)
        {
            _nakama = nakama ?? throw new ArgumentNullException(nameof(nakama));
        }

        /// <summary>Creates a party with the caller as its leader and only member.</summary>
        public UniTask<PartyInfo> CreateAsync(CancellationToken ct = default) =>
            CallAsync(CreateRpc, "{}", ct);

        /// <summary>
        /// Joins <paramref name="partyId"/>. Fails if the party is full, gone, or if the
        /// caller is already in a different one.
        /// </summary>
        public UniTask<PartyInfo> JoinAsync(string partyId, CancellationToken ct = default)
        {
            RequirePartyId(partyId);
            return CallAsync(JoinRpc, Payload(partyId), ct);
        }

        /// <summary>
        /// Leaves the caller's current party. If they were its leader, leadership transfers;
        /// if they were its last member, the party is deleted.
        /// </summary>
        /// <remarks>
        /// This does NOT leave a dungeon. A party that is inside an instance leaves it with a
        /// map transfer; dropping the party while still in the instance would leave the player
        /// on a server allocated to a party they are no longer in.
        /// </remarks>
        public async UniTask LeaveAsync(CancellationToken ct = default)
        {
            await RpcAsync(LeaveRpc, "{}", ct);
        }

        /// <summary>Reads a party's leader and members.</summary>
        public UniTask<PartyInfo> GetAsync(string partyId, CancellationToken ct = default)
        {
            RequirePartyId(partyId);
            return CallAsync(GetRpc, Payload(partyId), ct);
        }

        // ---- plumbing ------------------------------------------------------------------

        static void RequirePartyId(string partyId)
        {
            if (string.IsNullOrEmpty(partyId))
            {
                throw new ArgumentException("party id must not be empty", nameof(partyId));
            }
        }

        // Hand-built rather than a serializer: one field, and the id is server-generated hex,
        // so there is nothing here that needs escaping.
        static string Payload(string partyId) => "{\"party_id\":\"" + partyId + "\"}";

        async UniTask<PartyInfo> CallAsync(string rpc, string payload, CancellationToken ct)
        {
            string json = await RpcAsync(rpc, payload, ct);

            JsonValue root;
            try
            {
                root = JsonParser.Parse(json);
            }
            catch (Exception ex)
            {
                throw new PartyException($"Nakama RPC '{rpc}' returned a payload that is not JSON: {json}", ex);
            }

            string partyId = root.GetString("party_id");
            if (string.IsNullOrEmpty(partyId))
            {
                throw new PartyException($"Nakama RPC '{rpc}' returned no party_id: {json}");
            }

            // leader_id, not leader. Verified against backend/nakama/social/party.go rather
            // than assumed -- every other field in that module is <thing>_id.
            string leaderId = root.GetString("leader_id");

            var raw = root.GetArray("members");
            var members = new List<string>(raw.Count);
            for (var i = 0; i < raw.Count; i++)
            {
                string id = raw[i].AsString();
                if (!string.IsNullOrEmpty(id)) members.Add(id);
            }

            return new PartyInfo(partyId, leaderId, members);
        }

        async UniTask<string> RpcAsync(string rpc, string payload, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            ISession session = _nakama.Session;
            if (session == null)
            {
                throw new InvalidOperationException(
                    $"no Nakama session; authenticate before calling '{rpc}'.");
            }

            IApiRpc result;
            try
            {
                result = await _nakama.Client.RpcAsync(session, rpc, payload, canceller: ct);
            }
            catch (OperationCanceledException)
            {
                // A cancel is a cancel, not a refusal by the server.
                throw;
            }
            catch (Exception ex)
            {
                // Deliberately NOT a PartyException: the server may never have seen this. A
                // caller that cannot tell "the party is full" from "Nakama is unreachable"
                // will either retry a refusal forever or report an outage as a game rule.
                throw new InvalidOperationException(
                    $"Nakama RPC '{rpc}' failed. Verify the social module is registered on the " +
                    $"Nakama server. Cause: {ex.Message}", ex);
            }

            string json = result?.Payload;
            if (string.IsNullOrEmpty(json))
            {
                throw new PartyException($"Nakama RPC '{rpc}' returned an empty payload.");
            }
            return json;
        }
    }
}
