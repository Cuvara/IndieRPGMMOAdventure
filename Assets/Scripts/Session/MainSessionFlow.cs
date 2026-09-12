namespace Scripts.Session
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// The MainScene session sequence, as a pure state machine: authenticate the device, connect
    /// to the map, report each step with the exact markers the multi-client harness asserts on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The log lines are a contract.</b> <c>Tools/verify-multiclient.sh</c> counts distinct
    /// <c>user_id=</c> tokens on lines containing <c>Auth OK</c>, and counts logs containing
    /// <c>IN WORLD</c>. <see cref="AuthOkPrefix"/> and <see cref="InWorldPrefix"/> are the
    /// strings the netcode DOTS sample prints, byte for byte, so one harness reads both players.
    /// Change them here and in the harness in the same commit, or not at all.
    /// </para>
    /// <para>
    /// Pure C#: the endpoint is a seam (<see cref="IEndpoint"/>) so the sequence is tested with a
    /// fake, and the driver in the DI layer adapts <c>NakamaSessionService</c> + <c>NetworkClient</c>
    /// to it. Reconnects are not this class's business — the netcode client's own policy handles
    /// them and the DOTS bridge hooks <c>Reconnected</c>.
    /// </para>
    /// </remarks>
    public sealed class MainSessionFlow
    {
        public const string Tag = "[DOTSNet]";
        public const string AuthenticatingPrefix = Tag + " Authenticating device=";
        public const string AuthOkPrefix = Tag + " Auth OK, user_id=";
        public const string InWorldPrefix = Tag + " IN WORLD as ";

        /// <summary>Printed once the party exists, carrying the id a dungeon entry is keyed on.</summary>
        public const string PartyPrefix = Tag + " party ready: ";
        public const string FatalPrefix = Tag + " FATAL: ";
        public const string CancelledLine = Tag + " Cancelled";

        /// <summary>What the flow needs from the network stack.</summary>
        public interface IEndpoint
        {
            /// <summary>Authenticates the device (null = the machine's own id) and returns the user id.</summary>
            Task<string> AuthenticateDeviceAsync(string deviceId, CancellationToken cancellationToken);

            /// <summary>Authenticates to the gateway with the current session and joins the map.</summary>
            Task ConnectAsync(string mapId, CancellationToken cancellationToken);

            /// <summary>
            /// Creates a party (<paramref name="create"/>) or joins <paramref name="partyIdToJoin"/>,
            /// returning the party id.
            /// </summary>
            Task<string> EnsurePartyAsync(bool create, string partyIdToJoin, CancellationToken cancellationToken);

            /// <summary>Joins a dungeon instance of <paramref name="contentId"/> for the party.</summary>
            Task ConnectToDungeonAsync(string contentId, string partyId, CancellationToken cancellationToken);

            /// <summary>The user id the session is in world as; empty until connected.</summary>
            string UserId { get; }
        }

        public enum Phase
        {
            Idle,
            Authenticating,
            Partying,
            Connecting,
            InWorld,
            Failed,
            Cancelled,
        }

        public Phase CurrentPhase { get; private set; } = Phase.Idle;

        /// <summary>User id from authentication; empty until <see cref="Phase.Connecting"/>.</summary>
        public string UserId { get; private set; } = string.Empty;

        /// <summary>The failure, when <see cref="CurrentPhase"/> is <see cref="Phase.Failed"/>.</summary>
        public Exception Error { get; private set; }

        /// <summary>
        /// Runs the sequence once. Never throws: the outcome is <see cref="CurrentPhase"/>, and
        /// failures are logged with <see cref="FatalPrefix"/> so a headless run's log says why.
        /// </summary>
        /// <param name="deviceId">Device id to authenticate with; null for the machine's own.</param>
        /// <param name="log">Sink for the marker lines; the driver passes <c>Debug.Log</c>.</param>
        /// <param name="logError">Sink for the fatal line; the driver passes <c>Debug.LogError</c>.</param>
        /// <summary>
        /// The party this run created or joined; empty when it did neither. A dungeon entry is
        /// keyed on it (ADR-26 decision 2), so it is worth reading back rather than assuming.
        /// </summary>
        public string PartyId { get; private set; } = string.Empty;

        public async Task RunAsync(
            IEndpoint endpoint,
            string deviceId,
            string mapId,
            bool createParty,
            string partyIdToJoin,
            string dungeonContentId,
            Action<string> log,
            Action<string> logError,
            CancellationToken cancellationToken)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (string.IsNullOrEmpty(mapId)) throw new ArgumentException("mapId must not be empty", nameof(mapId));
            log ??= _ => { };
            logError ??= log;

            if (CurrentPhase != Phase.Idle)
            {
                throw new InvalidOperationException($"MainSessionFlow already ran (phase {CurrentPhase}); create a new one per attempt.");
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                CurrentPhase = Phase.Authenticating;
                log($"{AuthenticatingPrefix}{deviceId ?? "<machine>"}");
                UserId = await endpoint.AuthenticateDeviceAsync(deviceId, cancellationToken) ?? string.Empty;
                cancellationToken.ThrowIfCancellationRequested();
                log($"{AuthOkPrefix}{UserId}");

                // A party, if this run was told to want one. Before the world, because a
                // dungeon instance is keyed by the party (ADR-26 decision 2) -- there is
                // nothing to enter until the party exists.
                if (createParty || !string.IsNullOrEmpty(partyIdToJoin))
                {
                    CurrentPhase = Phase.Partying;
                    PartyId = await endpoint.EnsurePartyAsync(createParty, partyIdToJoin, cancellationToken)
                              ?? string.Empty;
                    cancellationToken.ThrowIfCancellationRequested();
                    log($"{PartyPrefix}{PartyId}");
                }

                CurrentPhase = Phase.Connecting;
                if (!string.IsNullOrEmpty(dungeonContentId))
                {
                    if (string.IsNullOrEmpty(PartyId))
                    {
                        // Loud, not a fallback to the map. A client configured for a dungeon
                        // with no party is a mistake in the configuration, and quietly putting
                        // that player in the open world is how the mistake survives a test run.
                        throw new InvalidOperationException(
                            "a dungeon was requested but this client is in no party; " +
                            "pass a party (create or an id) alongside the dungeon content id");
                    }

                    await endpoint.ConnectToDungeonAsync(dungeonContentId, PartyId, cancellationToken);
                }
                else
                {
                    await endpoint.ConnectAsync(mapId, cancellationToken);
                }
                cancellationToken.ThrowIfCancellationRequested();

                CurrentPhase = Phase.InWorld;
                var inWorldAs = string.IsNullOrEmpty(endpoint.UserId) ? UserId : endpoint.UserId;
                log($"{InWorldPrefix}{inWorldAs}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Only the session's own token makes this a cancel. An OperationCanceledException
                // from anywhere else — a superseded login generation, a library's internal
                // timeout token — is a failure with a cause worth printing, not a quiet exit.
                CurrentPhase = Phase.Cancelled;
                log(CancelledLine);
            }
            catch (Exception exception)
            {
                CurrentPhase = Phase.Failed;
                Error = exception;
                var cause = exception is OperationCanceledException
                    ? " (an OperationCanceledException while the session token was NOT cancelled — a superseded login or a foreign token)"
                    : string.Empty;
                logError($"{FatalPrefix}{exception}{cause}");
            }
        }
    }
}
