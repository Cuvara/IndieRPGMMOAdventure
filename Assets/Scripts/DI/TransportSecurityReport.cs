namespace Scripts.DI
{
    using System;
    using System.IO;
    using Cuvara.Netcode.Transport;
    using Scripts.Session;
    using UnityEngine;

    /// <summary>
    /// Says out loud, at startup, what each of the three hops is actually protected by.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client talks to three things and each hop is protected differently — Nakama by
    /// the URL scheme, the gateway by TLS it terminates itself (ADR-23), the game server by
    /// message-layer sealing (ADR-22). They are configured independently, so "the
    /// connection is encrypted" is a sentence that is true of one hop and believed about
    /// all three.
    /// </para>
    /// <para>
    /// A plaintext hop to <c>127.0.0.1</c> is the normal dev case and says nothing. A
    /// plaintext hop to a <b>remote</b> host is a build shipping credentials in the clear,
    /// and that is worth a line in the log that is hard to miss — the Nakama hop carries
    /// the session token, which is reusable for hours.
    /// </para>
    /// <para>
    /// Lives in the DI assembly rather than next to <see cref="BackendCommandLine"/>
    /// because <c>NDC.Scripts.Session</c> references no assemblies at all — a
    /// <c>using Cuvara.Netcode.Transport</c> there compiles in a hand-written csproj and
    /// fails in Unity, which is a slow way to learn where an asmdef boundary is.
    /// </para>
    /// </remarks>
    public static class TransportSecurityReport
    {
        /// <summary>Logs one line per hop, escalating to an error for plaintext to a remote host.</summary>
        public static void Warn(BackendCommandLine.Settings backend)
        {
            ReportHop(
                "Nakama (auth, meta)",
                backend.NakamaHost,
                secure: IsHttps(backend.NakamaScheme),
                secureDetail: "https",
                plainDetail: "http — the session token crosses this hop in the clear",
                howToFix: "-cuvara-nakama-scheme https");

            ReportHop(
                "gateway",
                backend.GatewayHost,
                secure: backend.GatewayTls,
                secureDetail: backend.GatewayTlsCertPath == null
                    ? "TLS, platform trust store"
                    : "TLS, pinned to " + backend.GatewayTlsCertPath,
                plainDetail: "plaintext — the join token crosses this hop in the clear",
                howToFix: "-cuvara-gateway-tls 1");

            ReportGameplayHop(backend);
        }

        /// <summary>
        /// Reports what the client will <b>request</b> for the gameplay hop, and says that the
        /// outcome is not this line's to report.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Whether the session ends up sealed is decided at the join, by what the server
        /// answers — <c>GAMESERVER_SEALED</c> is the server's setting and there is no
        /// negotiation. So this deliberately reports the <i>request</i> and nothing more.
        /// Until 0.36.1 it reported nothing at all, which was honest but unhelpful once the
        /// client could ask: the request is a real, knowable fact at startup and it is the
        /// one the operator just typed.
        /// </para>
        /// <para>
        /// The line that says the session <i>is</i> sealed comes from the netcode client after
        /// the handshake, and it carries the caveat this one cannot: the server's binding is
        /// not verified, so the session is confidential against a passive eavesdropper and
        /// offers nothing against an active one until ADR-22's pinned identity key lands.
        /// </para>
        /// <para>
        /// Two mismatches are worth naming here rather than leaving to a symptom:
        /// </para>
        /// <list type="bullet">
        /// <item><description>
        /// <b>Sealing asked for over JSON</b> cannot ever work — the sealed handshake messages
        /// are absent from the JSON message set — and the server refuses it at the join with
        /// <c>encoding_cannot_seal</c>, which reaches the client as nothing more informative
        /// than a closed connection. That is an error, not a warning.
        /// </description></item>
        /// <item><description>
        /// <b>Sealing asked for against a server that is not sealing</b> stalls the join until
        /// the handshake timeout. The netcode client names that cause itself when it fires;
        /// this line exists so the request is on the record <i>before</i> the stall, not only
        /// after it.
        /// </description></item>
        /// </list>
        /// </remarks>
        private static void ReportGameplayHop(BackendCommandLine.Settings backend)
        {
            var encoding = backend.EncodingIsProtobuf ? "protobuf" : "JSON";

            if (backend.SealedOverJsonIsImpossible)
            {
                Debug.LogError(
                    "[transport-security] game server: sealing was REQUESTED over JSON, which cannot " +
                    "work — the sealed handshake messages do not exist in the JSON message set, and a " +
                    "server running GAMESERVER_SEALED=require refuses a JSON client at the join " +
                    "(encoding_cannot_seal), which arrives here as a bare closed connection. " +
                    "Add -cuvara-encoding proto, or drop -cuvara-sealed.");
                return;
            }

            if (backend.Sealed)
            {
                Debug.Log(
                    "[transport-security] game server (address assigned at join): will REQUEST a sealed " +
                    $"session (ADR-22) over {encoding}. Whether it is sealed is decided at the join by " +
                    "the server's GAMESERVER_SEALED; there is no negotiation and no cleartext fallback, " +
                    "so against a server with sealing off the join will time out rather than downgrade.");
                return;
            }

            Debug.Log(
                "[transport-security] game server (address assigned at join): will NOT request sealing — " +
                $"gameplay frames cross this hop in the clear, over {encoding}. This matches every " +
                "deployed environment today (GAMESERVER_SEALED=off). Turn it on with -cuvara-sealed 1, " +
                "which requires the server to be set to require at the same time.");
        }

        /// <summary>
        /// Loads the pinned certificate, or returns null to leave the platform trust store
        /// deciding.
        /// </summary>
        /// <remarks>
        /// A path that was given but cannot be read is an <b>error</b> and returns null,
        /// which means platform validation. That is the safe direction — it fails closed
        /// against a self-signed gateway rather than open — but it is not what the operator
        /// asked for, so it must not pass quietly.
        /// </remarks>
        public static byte[] LoadPinOrNull(BackendCommandLine.Settings backend)
        {
            var path = backend.GatewayTlsCertPath;
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            if (!backend.GatewayTls)
            {
                Debug.LogWarning(
                    $"[transport-security] a gateway certificate was pinned ({path}) but -cuvara-gateway-tls " +
                    "is off, so the connection is PLAINTEXT and the pin is ignored.");
                return null;
            }

            try
            {
                return TlsOptions.FromPem(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[transport-security] could not load the pinned gateway certificate from '{path}': " +
                    $"{ex.GetType().Name}: {ex.Message}. Falling back to the platform trust store, which " +
                    "will REFUSE a self-signed gateway — the refusal you see next is this, not the gateway.");
                return null;
            }
        }

        private static void ReportHop(string hop, string host, bool secure,
            string secureDetail, string plainDetail, string howToFix)
        {
            if (secure)
            {
                Debug.Log($"[transport-security] {hop} → {host}: {secureDetail}");
                return;
            }

            if (IsLoopback(host))
            {
                Debug.Log($"[transport-security] {hop} → {host}: plaintext (loopback, expected in dev)");
                return;
            }

            Debug.LogError(
                $"[transport-security] {hop} → {host}: {plainDetail}. " +
                $"This is a REMOTE host, so anyone on the path can read it. Turn it on with {howToFix}.");
        }

        private static bool IsHttps(string scheme)
            => string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Loopback by literal address or name. Deliberately narrow: a host this does not
        /// recognise is reported as remote, so the failure direction is a warning too many
        /// rather than a silent plaintext link to a real server.
        /// </summary>
        private static bool IsLoopback(string host)
        {
            if (string.IsNullOrEmpty(host))
            {
                return false;
            }

            return host == "127.0.0.1"
                   || host == "::1"
                   || host == "[::1]"
                   || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                   || host.StartsWith("127.", StringComparison.Ordinal);
        }
    }
}
