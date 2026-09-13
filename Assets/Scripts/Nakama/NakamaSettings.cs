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

        /// <summary>
        /// DER bytes of the one certificate Nakama is allowed to present, or null to let the
        /// platform trust store decide.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Only meaningful with <see cref="Scheme"/> <c>https</c>, which is ADR-24's meta-hop
        /// TLS. Null is the stronger default: Unity's own validation runs, which is right for
        /// a CA-issued certificate and correctly REFUSES a self-signed one with
        /// <c>Curl error 60: Cert verify failed … UnityTls error code: 7</c>.
        /// </para>
        /// <para>
        /// Set it and the client pins that exact certificate instead — <b>stricter</b> than
        /// the trust store, not looser, because an attacker must hold this certificate's
        /// private key rather than any certificate some CA will sign. That is how a
        /// self-signed dev or staging Nakama is reached without an accept-anything switch,
        /// which does not exist here or anywhere else in this client (ADR-24 decision 4).
        /// </para>
        /// <para>
        /// <c>Scripts.DI.TransportSecurityReport.LoadNakamaPinOrNull</c> produces these bytes
        /// from the PEM named by <c>-cuvara-nakama-tls-cert</c>.
        /// </para>
        /// </remarks>
        public byte[] PinnedCertificate { get; set; }

        /// <summary>
        /// Device id for device authentication. Null uses <c>SystemInfo.deviceUniqueIdentifier</c>.
        /// Set per process when several clients run on one machine — the identifier is the same
        /// for all of them, and shared identity makes the logins evict each other.
        /// </summary>
        public string DeviceId { get; set; }
    }
}
