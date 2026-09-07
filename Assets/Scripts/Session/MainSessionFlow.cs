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
        public const string FatalPrefix = Tag + " FATAL: ";
        public const string CancelledLine = Tag + " Cancelled";

        /// <summary>What the flow needs from the network stack.</summary>
        public interface IEndpoint
        {
            /// <summary>Authenticates the device (null = the machine's own id) and returns the user id.</summary>
            Task<string> AuthenticateDeviceAsync(string deviceId, CancellationToken cancellationToken);

            /// <summary>Authenticates to the gateway with the current session and joins the map.</summary>
            Task ConnectAsync(string mapId, CancellationToken cancellationToken);

            /// <summary>The user id the session is in world as; empty until connected.</summary>
            string UserId { get; }
        }

        public enum Phase
        {
            Idle,
            Authenticating,
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
        public async Task RunAsync(
            IEndpoint endpoint,
            string deviceId,
            string mapId,
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

                CurrentPhase = Phase.Connecting;
                await endpoint.ConnectAsync(mapId, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                CurrentPhase = Phase.InWorld;
                var inWorldAs = string.IsNullOrEmpty(endpoint.UserId) ? UserId : endpoint.UserId;
                log($"{InWorldPrefix}{inWorldAs}");
            }
            catch (OperationCanceledException)
            {
                CurrentPhase = Phase.Cancelled;
                log(CancelledLine);
            }
            catch (Exception exception)
            {
                CurrentPhase = Phase.Failed;
                Error = exception;
                logError($"{FatalPrefix}{exception}");
            }
        }
    }
}
