namespace Scripts.DI.Dots
{
    using System;

    /// <summary>
    /// Says whether a session object is one the bridge has not yet begun. Every connect,
    /// reconnect, retry, dungeon join and map transfer builds a new session object, so identity
    /// is the one signal that covers all of them; the netcode <c>Reconnected</c> event is only
    /// one of the ways to learn about it.
    /// </summary>
    /// <remarks>
    /// Idempotent per session object: <see cref="TryBegin"/> returns true once for a given object
    /// no matter how many paths (the per-frame identity check, the <c>Reconnected</c> event) ask.
    /// Plain C# on purpose, so it is testable without a scene or a live client.
    /// </remarks>
    public sealed class SessionBoundary
    {
        private object last;

        /// <summary>Sessions begun so far.</summary>
        public int Count { get; private set; }

        /// <summary>
        /// True exactly when <paramref name="session"/> is non-null and not the object most
        /// recently begun; the caller must then run the session reset once.
        /// </summary>
        public bool TryBegin(object session)
        {
            if (session == null || ReferenceEquals(session, this.last))
            {
                return false;
            }

            this.last = session;
            this.Count++;
            return true;
        }
    }
}
