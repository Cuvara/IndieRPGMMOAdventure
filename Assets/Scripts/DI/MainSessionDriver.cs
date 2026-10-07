namespace Scripts.DI
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Cuvara.Netcode.Client;
    using Cysharp.Threading.Tasks;
    using System.Collections.Generic;
    using Scripts.Nakama;
    using Scripts.Nakama.Characters;
    using Scripts.Nakama.Social;
    using Scripts.Session;
    using UnityEngine;
    using VContainer.Unity;

    /// <summary>
    /// Backend addresses and the device identity this process plays as, resolved once by
    /// <c>GameLifetimeScope</c> from the command line / <c>CUVARA_*</c> environment and shared
    /// with whatever needs them.
    /// </summary>
    public sealed class BackendSettings
    {
        public BackendCommandLine.Settings Value;

        /// <summary>Device id to authenticate with; null means the machine's own identifier.</summary>
        public string DeviceId;
    }

    /// <summary>
    /// The MainScene session: authenticates the device with Nakama, connects to the map through
    /// the gateway, and reports each step with the markers the multi-client harness reads. Runs
    /// as a VContainer entry point of <c>MainSceneScope</c>, so it starts with the scene and is
    /// cancelled and disconnected when the scope is disposed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A container entry point rather than a scene component: the scene needs no object added,
    /// the dependencies (<c>NetworkClient</c>, <c>NakamaSessionService</c>, the resolved backend)
    /// arrive by injection instead of <c>FindObjectOfType</c>, and the scope's disposal is the one
    /// place a session end is guaranteed to run. <c>IStartable</c> plus an owned
    /// <see cref="CancellationTokenSource"/> rather than <c>IAsyncStartable</c>, whose return type
    /// depends on VContainer's UniTask-integration define.
    /// </para>
    /// <para>
    /// The sequence itself is <see cref="MainSessionFlow"/> (pure, tested); this class adapts the
    /// real stack to its endpoint seam. Input and presentation are <c>DotsWorldBridge</c>'s and
    /// start on their own once <c>NetworkClient.State</c> reaches <c>InWorld</c> and the bridge's
    /// prewarm completes. Reconnects are the client's policy; the bridge hooks <c>Reconnected</c>.
    /// </para>
    /// </remarks>
    public sealed class MainSessionDriver : IStartable, IDisposable
    {
        private const string DeviceIdPrefix = "mainscene";

        private readonly NetworkClient client;
        private readonly NakamaSessionService nakama;
        private readonly BackendSettings backend;
        private readonly PartyService party;
        private readonly CharacterService characters;
        private readonly CharacterSelectionState characterSelection;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly int instanceId = Interlocked.Increment(ref instances);
        private static int instances;
        private bool disposed;

        public MainSessionDriver(
            NetworkClient client,
            NakamaSessionService nakama,
            BackendSettings backend,
            PartyService party,
            CharacterService characters,
            CharacterSelectionState characterSelection)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.nakama = nakama ?? throw new ArgumentNullException(nameof(nakama));
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            this.party = party ?? throw new ArgumentNullException(nameof(party));
            this.characters = characters ?? throw new ArgumentNullException(nameof(characters));
            this.characterSelection = characterSelection ?? throw new ArgumentNullException(nameof(characterSelection));
        }

        /// <summary>The sequence this driver ran; null until <see cref="Start"/>.</summary>
        public MainSessionFlow Flow { get; private set; }

        public void Start()
        {
            var settings = this.backend.Value;
            Debug.Log(
                $"{MainSessionFlow.Tag} backend gateway={settings.GatewayHost}:{settings.GatewayPort} " +
                $"nakama={settings.NakamaBaseUrl} map={settings.MapId} device={this.backend.DeviceId ?? "<machine>"}");

            // Probe, kept on purpose: a session that logs "Cancelled" straight after starting is
            // either a token cancelled before its first await or a cancel from somewhere else,
            // and this line plus the one in Dispose tell the two apart from a player log.
            Debug.Log(
                $"{MainSessionFlow.Tag} session driver #{this.instanceId} start: disposed={this.disposed} " +
                $"tokenCancelled={this.lifetime.IsCancellationRequested} clientState={this.client.State}");

            this.client.Reconnected += this.OnReconnected;
            this.client.ReconnectFailed += this.OnReconnectFailed;
            this.client.StateChanged += this.OnStateChanged;

            this.RunAsync(this.lifetime.Token).Forget();
        }

        private async UniTaskVoid RunAsync(CancellationToken cancellationToken)
        {
            this.Flow = new MainSessionFlow();
            await this.Flow.RunAsync(
                new Endpoint(
                    this.client, this.nakama, this.party, this.characters, this.characterSelection,
                    this.backend.Value.Character),
                this.backend.DeviceId,
                this.backend.Value.MapId,
                this.backend.Value.CreatesParty,
                this.backend.Value.PartyIdToJoin,
                this.backend.Value.DungeonContentId,
                Debug.Log,
                Debug.LogError,
                cancellationToken);
        }

        private void OnStateChanged(NetworkClientState state) => Debug.Log($"{MainSessionFlow.Tag} State -> {state}");

        private void OnReconnected() => Debug.Log($"{MainSessionFlow.Tag} Reconnected as {this.client.UserId}");

        private void OnReconnectFailed(Exception exception) =>
            Debug.LogWarning($"{MainSessionFlow.Tag} Reconnect gave up: {exception?.Message}");

        public void Dispose()
        {
            if (this.disposed) return;
            this.disposed = true;

            Debug.Log(
                $"{MainSessionFlow.Tag} session driver #{this.instanceId} disposed (phase {this.Flow?.CurrentPhase.ToString() ?? "not started"}) at:\n" +
                Environment.StackTrace);

            this.client.Reconnected -= this.OnReconnected;
            this.client.ReconnectFailed -= this.OnReconnectFailed;
            this.client.StateChanged -= this.OnStateChanged;

            this.lifetime.Cancel();
            this.lifetime.Dispose();

            // The scope owned the session; the root-scoped client outlives it and must not keep a
            // dead scene's connection open into the next one.
            this.client.Disconnect();
        }

        /// <summary>Adapts the Nakama service and the netcode client to the flow's seam.</summary>
        private sealed class Endpoint : MainSessionFlow.IEndpoint
        {
            private readonly NetworkClient client;
            private readonly NakamaSessionService nakama;
            private readonly PartyService party;
            private readonly CharacterService characters;
            private readonly CharacterSelectionState selection;
            private readonly string requestedCharacter;

            public Endpoint(
                NetworkClient client,
                NakamaSessionService nakama,
                PartyService party,
                CharacterService characters,
                CharacterSelectionState selection,
                string requestedCharacter)
            {
                this.client = client;
                this.nakama = nakama;
                this.party = party;
                this.characters = characters;
                this.selection = selection;
                this.requestedCharacter = requestedCharacter;
            }

            /// <summary>PlayerPrefs key of the last character an account played on this device.</summary>
            internal static string LastCharacterKey(string userId) => "character.last_used." + userId;

            public async Task<CharacterChoice> SelectCharacterAsync(string userId, CancellationToken cancellationToken)
            {
                CharacterRoster roster;
                try
                {
                    roster = await this.characters.ListAsync(cancellationToken);
                }
                catch (Exception exception) when (!(exception is OperationCanceledException) &&
                                                  string.IsNullOrEmpty(this.requestedCharacter))
                {
                    // Rollout tolerance, not a silent fallback: a Nakama predating ADR-31 has no
                    // roster RPCs, and the account's default character (no cid) is exactly what
                    // such a backend serves. Said out loud; an explicit -cuvara-character still
                    // fails, because then the caller asked for something specific.
                    Debug.LogWarning(
                        $"{MainSessionFlow.Tag} character roster unavailable ({exception.Message}); " +
                        "playing the account's default character.");
                    this.Apply(CharacterChoice.Default, userId);
                    return CharacterChoice.Default;
                }

                var entries = new List<CharacterEntry>(roster.Characters.Count);
                foreach (var character in roster.Characters)
                {
                    entries.Add(new CharacterEntry(character.Id, character.Name, character.Slot));
                }

                var lastUsed = PlayerPrefs.GetString(LastCharacterKey(userId), string.Empty);
                var decision = CharacterSelection.Decide(entries, this.requestedCharacter, lastUsed, userId);

                CharacterChoice choice;
                if (decision.Create)
                {
                    var created = await this.characters.CreateAsync(decision.Name, -1, cancellationToken);
                    choice = new CharacterChoice(created.Id, created.Name, created: true);
                }
                else
                {
                    choice = new CharacterChoice(decision.Id, decision.Name, created: false);
                }

                Debug.Log(
                    $"{MainSessionFlow.Tag} character roster: {entries.Count}/{roster.MaxSlots} " +
                    $"-> {(decision.Create ? "create" : "use")} '{choice.Name}' ({decision.Reason})");
                this.Apply(choice, userId);
                return choice;
            }

            private void Apply(CharacterChoice choice, string userId)
            {
                // Both, always: the token's cid (minted by the auth provider from the selection)
                // and the enter-world character_id must name the same character, or the gateway
                // answers character_mismatch.
                var id = choice.IsDefault ? null : choice.Id;
                this.selection.CharacterId = id;
                this.client.CharacterId = id;

                if (!choice.IsDefault && !string.IsNullOrEmpty(userId))
                {
                    PlayerPrefs.SetString(LastCharacterKey(userId), choice.Id);
                    PlayerPrefs.Save();
                }
            }

            public string UserId => this.client.UserId;

            public async Task<string> AuthenticateDeviceAsync(string deviceId, CancellationToken cancellationToken)
            {
                // Explicit device auth first, so the IAuthProvider the client resolves finds a
                // valid session for THIS device and only mints the gateway token.
                var session = await this.nakama.AuthenticateDeviceAsync(deviceId, cancellationToken);
                return session.UserId;
            }

            public async Task ConnectAsync(string mapId, CancellationToken cancellationToken)
            {
                // The provider overload: the registered NakamaAuthProvider turns the session into
                // a gateway JWT, and the client keeps the provider for its own reconnects.
                await this.client.ConnectAsync(mapId, cancellationToken);
            }

            public async Task<string> EnsurePartyAsync(
                bool create, string partyIdToJoin, CancellationToken cancellationToken)
            {
                var info = create
                    ? await this.party.CreateAsync(cancellationToken)
                    : await this.party.JoinAsync(partyIdToJoin, cancellationToken);
                return info.PartyId;
            }

            public async Task ConnectToDungeonAsync(
                string contentId, string partyId, CancellationToken cancellationToken)
            {
                // Same provider overload as ConnectAsync above, for the same reason: the client
                // keeps the provider so its own reconnects re-authenticate -- and a dungeon
                // reconnect must re-enter the SAME instance, which it does because the client
                // remembers the party id alongside the map.
                await this.client.ConnectToDungeonAsync(contentId, partyId, cancellationToken);
            }
        }
    }
}
