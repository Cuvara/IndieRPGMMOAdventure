namespace Scripts.Nakama
{
    /// <summary>
    /// Connection settings for the Nakama server.
    /// Passed as a registered instance in VContainer; override by registering
    /// a custom instance before <see cref="DI.NakamaRegistration.RegisterNakama"/>.
    /// </summary>
    public sealed class NakamaSettings
    {
        /// <summary>Nakama server scheme (http or https).</summary>
        public string Scheme { get; set; } = "http";

        /// <summary>Nakama server hostname.</summary>
        public string Host { get; set; } = "127.0.0.1";

        /// <summary>Nakama HTTP API port (default 7350).</summary>
        public int Port { get; set; } = 7350;

        /// <summary>
        /// Nakama server key. "defaultkey" is the Nakama default for
        /// unauthenticated client access — it is not a secret.
        /// </summary>
        public string ServerKey { get; set; } = "defaultkey";
    }
}
