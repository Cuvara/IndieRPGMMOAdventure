namespace TestLab
{
    /// <summary>
    /// The user id this client joined as, kept after the session that carried it is gone.
    /// </summary>
    /// <remarks>
    /// <c>NetworkClient.UserId</c> reads the live game session, so it is empty once the session is
    /// released - after <c>Disconnect()</c> (the <c>quit</c> plan action), a failed reconnect, or a
    /// closed link. The runner writes <c>summary.json</c> and <c>status.json</c> exactly then, and a
    /// leaver's summary with <c>user_id: ""</c> cannot be matched to the server's view of the same
    /// player. The latch remembers the last non-empty id it observed; the live id still wins while
    /// there is one, so a rejoin under a different id is reported as that id.
    /// </remarks>
    public sealed class UserIdLatch
    {
        /// <summary>The last non-empty id observed, or null.</summary>
        public string Last { get; private set; }

        public void Observe(string userId)
        {
            if (!string.IsNullOrEmpty(userId))
            {
                this.Last = userId;
            }
        }

        /// <summary>
        /// <paramref name="live"/> when it is set; else the last id observed; else
        /// <paramref name="live"/> unchanged (so a client that never joined reports what it did before).
        /// </summary>
        public string Resolve(string live)
        {
            this.Observe(live);
            return string.IsNullOrEmpty(live) && this.Last != null ? this.Last : live;
        }
    }
}
