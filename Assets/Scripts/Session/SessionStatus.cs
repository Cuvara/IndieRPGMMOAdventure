namespace Scripts.Session
{
    /// <summary>What the player should be told about their connection.</summary>
    public enum SessionStatusKind
    {
        /// <summary>Playing, or not started yet: nothing to show.</summary>
        Active,

        /// <summary>The link dropped and the client is trying to come back by itself.</summary>
        Reconnecting,

        /// <summary>The client has stopped trying. Only the player can act: Retry.</summary>
        Ended,
    }

    /// <summary>An immutable snapshot of <see cref="SessionRecovery.Status"/>.</summary>
    public sealed class SessionStatus
    {
        public static readonly SessionStatus Active = new SessionStatus(SessionStatusKind.Active, string.Empty, false);

        public SessionStatus(SessionStatusKind kind, string cause, bool canRetry)
        {
            this.Kind = kind;
            this.Cause = cause ?? string.Empty;
            this.CanRetry = canRetry;
        }

        public SessionStatusKind Kind { get; }

        /// <summary>Why, in the client's own words (a disconnect reason or an exception message).</summary>
        public string Cause { get; }

        /// <summary>True when Retry would do something: the session ended and no retry is running.</summary>
        public bool CanRetry { get; }

        /// <summary>True when the overlay should be on screen.</summary>
        public bool IsVisible => this.Kind != SessionStatusKind.Active;

        /// <summary>The line the overlay shows.</summary>
        public string Caption
        {
            get
            {
                switch (this.Kind)
                {
                    case SessionStatusKind.Reconnecting:
                        return "Connection lost. Reconnecting...";
                    case SessionStatusKind.Ended:
                        return string.IsNullOrEmpty(this.Cause)
                            ? "Disconnected."
                            : "Disconnected: " + this.Cause;
                    default:
                        return string.Empty;
                }
            }
        }

        public override string ToString() => $"{this.Kind} canRetry={this.CanRetry} cause='{this.Cause}'";
    }
}
