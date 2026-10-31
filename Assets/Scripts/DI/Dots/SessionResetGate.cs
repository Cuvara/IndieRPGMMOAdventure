namespace Scripts.DI.Dots
{
    using System;

    /// <summary>What <see cref="SessionResetGate{TSession}.OnReconnected"/> did with a <c>Reconnected</c> event.</summary>
    public enum ReconnectOutcome
    {
        /// <summary>The event began the session: the reset ran now.</summary>
        Began,

        /// <summary>The per-frame check had already begun this session object; no second reset.</summary>
        AlreadyBegun,

        /// <summary>No session object at callback time; the per-frame check will begin it when one appears.</summary>
        NoSession,
    }

    /// <summary>
    /// The production routing of the session-start reset: both the per-frame check and the
    /// <c>Reconnected</c> event go through here, and the frame path guarantees the reset runs
    /// before the frame's binder feed. <c>DotsWorldBridge</c> delegates to this class and holds
    /// no copy of the decision, so tests of this class are tests of the bridge's routing.
    /// </summary>
    /// <remarks>
    /// Plain C# on purpose, so it is testable without a scene or a live client. Idempotent per
    /// session object through <see cref="SessionBoundary"/>.
    /// </remarks>
    public sealed class SessionResetGate<TSession>
        where TSession : class
    {
        private readonly SessionBoundary boundary = new SessionBoundary();

        /// <summary>Sessions begun so far.</summary>
        public int Count => this.boundary.Count;

        /// <summary>
        /// One frame. While <paramref name="inWorld"/>, a session object not yet begun runs
        /// <paramref name="beginSession"/> first; then <paramref name="feed"/> runs, always, so the
        /// feed of a new session never precedes its reset.
        /// </summary>
        public void Tick(bool inWorld, TSession session, Action<TSession> beginSession, Action feed)
        {
            if (inWorld && this.boundary.TryBegin(session))
            {
                beginSession(session);
            }

            feed();
        }

        /// <summary>The <c>Reconnected</c> event: begins <paramref name="session"/> unless the frame check already did.</summary>
        public ReconnectOutcome OnReconnected(TSession session, Action<TSession> beginSession)
        {
            if (session == null)
            {
                return ReconnectOutcome.NoSession;
            }

            if (!this.boundary.TryBegin(session))
            {
                return ReconnectOutcome.AlreadyBegun;
            }

            beginSession(session);
            return ReconnectOutcome.Began;
        }
    }
}
