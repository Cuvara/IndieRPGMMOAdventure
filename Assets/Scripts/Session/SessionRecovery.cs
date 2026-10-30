namespace Scripts.Session
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// The player-facing side of a session that can end: holds the <see cref="SessionStatus"/>
    /// the overlay shows and runs Retry (audit CN-02). Pure C#, driven by whoever watches the
    /// network client; <c>MainSessionDriver</c> feeds it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Retry runs a <b>fresh</b> <see cref="MainSessionFlow"/> (a flow is single-shot), through
    /// the <c>startFlow</c> delegate the owner supplies. A retry that fails leaves the session
    /// <see cref="SessionStatusKind.Ended"/> with the new cause so the player can try again.
    /// </para>
    /// <para>
    /// Not thread-safe: call it from the main thread, as the network client's events already are.
    /// </para>
    /// </remarks>
    public sealed class SessionRecovery
    {
        private readonly Func<CancellationToken, Task<MainSessionFlow>> startFlow;
        private bool retrying;
        private bool wasInWorld;

        /// <param name="startFlow">Runs a new <see cref="MainSessionFlow"/> to completion and returns it.</param>
        public SessionRecovery(Func<CancellationToken, Task<MainSessionFlow>> startFlow)
        {
            this.startFlow = startFlow ?? throw new ArgumentNullException(nameof(startFlow));
        }

        public SessionStatus Status { get; private set; } = SessionStatus.Active;

        /// <summary>Raised whenever <see cref="Status"/> changes.</summary>
        public event Action<SessionStatus> StatusChanged;

        /// <summary>The client is playing (or the first login just succeeded).</summary>
        public void InWorld()
        {
            this.wasInWorld = true;
            this.Set(SessionStatus.Active);
        }

        /// <summary>
        /// The client retries a transient failure of the <b>first</b> connect (netcode reports it
        /// through <c>ReconnectProgress</c> while the state stays Authenticating). Ignored once
        /// the session has been in the world, where the same event belongs to a reconnect that
        /// <see cref="Reconnecting"/> already covers, and while Ended or a Retry is running.
        /// </summary>
        public void ConnectRetrying(int attempt, int maxAttempts)
        {
            if (this.wasInWorld || this.retrying) return;
            var kind = this.Status.Kind;
            if (kind != SessionStatusKind.Active && kind != SessionStatusKind.Connecting) return;

            this.Set(new SessionStatus(attempt, maxAttempts));
        }

        /// <summary>The link dropped and the client is reconnecting on its own.</summary>
        public void Reconnecting() => this.Set(new SessionStatus(SessionStatusKind.Reconnecting, string.Empty, false));

        /// <summary>
        /// The session ended with nothing trying to bring it back: a kick, a protocol mismatch,
        /// or an exhausted reconnect. <paramref name="cause"/> is shown to the player as is.
        /// </summary>
        public void Ended(string cause) =>
            this.Set(new SessionStatus(SessionStatusKind.Ended, cause, canRetry: !this.retrying));

        /// <summary>
        /// Folds the outcome of a <see cref="MainSessionFlow"/> in: a failure ends the session
        /// with its message, an arrival clears the overlay, a cancel changes nothing.
        /// </summary>
        public void FlowFinished(MainSessionFlow flow)
        {
            if (flow == null) throw new ArgumentNullException(nameof(flow));

            switch (flow.CurrentPhase)
            {
                case MainSessionFlow.Phase.Failed:
                    this.Ended(flow.Error?.Message);
                    break;
                case MainSessionFlow.Phase.InWorld:
                    this.InWorld();
                    break;
            }
        }

        /// <summary>
        /// Runs a fresh session flow. Does nothing unless the session is
        /// <see cref="SessionStatusKind.Ended"/> and no retry is already running, so a double
        /// click cannot start two logins.
        /// </summary>
        public async Task RetryAsync(CancellationToken cancellationToken)
        {
            if (this.retrying || this.Status.Kind != SessionStatusKind.Ended) return;

            this.retrying = true;
            try
            {
                this.Set(new SessionStatus(SessionStatusKind.Reconnecting, string.Empty, false));
                var flow = await this.startFlow(cancellationToken);
                this.retrying = false;
                this.FlowFinished(flow);
            }
            catch (OperationCanceledException)
            {
                // The scope is going away; nobody is left to show anything to.
            }
            catch (Exception exception)
            {
                this.retrying = false;
                this.Ended(exception.Message);
            }
            finally
            {
                this.retrying = false;
            }
        }

        private void Set(SessionStatus next)
        {
            this.Status = next;
            this.StatusChanged?.Invoke(next);
        }
    }
}
