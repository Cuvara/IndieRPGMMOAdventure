namespace Scripts.Nakama.Tls
{
    using System;
    using UnityEngine.Networking;

    /// <summary>
    /// Accepts exactly one certificate on the Nakama hop, and no other.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>There is deliberately no "accept any certificate" mode</b>, and this class is
    /// built so one cannot be added by configuration: it has no boolean, the constructor
    /// throws on an empty pin, and <see cref="Accepts"/> answers false for a pin it does not
    /// have. A <c>CertificateHandler</c> whose <c>ValidateCertificate</c> returns true is
    /// <c>InsecureSkipVerify</c> with a Unity spelling — the passive-eavesdropper guarantee
    /// at the price of the authenticated one — and ADR-24 decision 4 rules it out in every
    /// environment, dev included.
    /// </para>
    /// <para>
    /// <b>Pinning is stricter than the platform trust store, not looser.</b> With no pin,
    /// Unity's own validation decides and a self-signed Nakama is correctly refused
    /// (<c>Curl error 60: Cert verify failed … UnityTls error code: 7</c>). With a pin, chain,
    /// expiry, hostname and CA stop mattering <i>because a stronger question already
    /// answered</i>: an attacker must present this exact certificate, which needs its private
    /// key. That is why a dev Nakama holding a self-signed certificate can be reached without
    /// weakening anything. It is the same discipline as <c>TlsOptions.PinnedCertificate</c> on
    /// the gateway hop; only the mechanism differs, because this hop is
    /// <c>UnityWebRequest</c> and that one is <c>SslStream</c>.
    /// </para>
    /// <para>
    /// <b>Two limits worth knowing before relying on this.</b>
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>WebGL never calls it.</b> The browser performs the TLS handshake, so
    /// <c>ValidateCertificate</c> is not invoked at all and a WebGL player needs a CA-issued
    /// certificate on this hop. Pinning is unavailable there and must not be claimed for it.
    /// </description></item>
    /// <item><description>
    /// <b>The pin is the leaf.</b> Rotating Nakama's certificate is therefore a client
    /// change, exactly as it is for the gateway hop.
    /// </description></item>
    /// </list>
    /// </remarks>
    public sealed class PinnedCertificateHandler : CertificateHandler
    {
        private readonly byte[] pin;

        /// <param name="pinnedDer">
        /// DER bytes of the one certificate Nakama is allowed to present —
        /// <c>Cuvara.Netcode.Transport.TlsOptions.FromPem</c> produces these from a PEM file.
        /// </param>
        /// <exception cref="ArgumentException">
        /// The pin is null or empty. Refused rather than defaulted, because a handler pinned
        /// to nothing that answered <c>true</c> would be the accept-anything mode this type
        /// exists to not have, and one that answered <c>false</c> would be an installed
        /// handler that refuses every certificate — a confusing way to spell "no pin". A
        /// caller with no certificate installs no handler at all.
        /// </exception>
        public PinnedCertificateHandler(byte[] pinnedDer)
        {
            if (pinnedDer == null || pinnedDer.Length == 0)
            {
                throw new ArgumentException(
                    "refusing to build a certificate handler with an empty pin — that is " +
                    "InsecureSkipVerify with a different name (ADR-24 decision 4). Pass no " +
                    "handler at all to use the platform trust store.",
                    nameof(pinnedDer));
            }

            this.pin = (byte[])pinnedDer.Clone();
        }

        /// <summary>
        /// Whether a presented certificate is the pinned one. Public so the refusal path is
        /// testable without a live TLS handshake; <see cref="ValidateCertificate"/> is
        /// <c>protected</c> and cannot be called from a test otherwise.
        /// </summary>
        public bool Accepts(byte[] presentedDer) => Matches(this.pin, presentedDer);

        /// <summary>
        /// Byte-for-byte comparison, in constant time with respect to the position of the
        /// first difference. A missing or empty pin matches nothing, which is what makes
        /// "pinned to nothing" a closed door rather than an open one.
        /// </summary>
        /// <remarks>
        /// The constant-time loop is not because a certificate is a secret — it is public by
        /// definition — but because the same routine is the obvious one to reuse next time,
        /// and the version that returns early is the one that gets copied.
        /// </remarks>
        public static bool Matches(byte[] pinnedDer, byte[] presentedDer)
        {
            if (pinnedDer == null || pinnedDer.Length == 0) return false;
            if (presentedDer == null || presentedDer.Length == 0) return false;
            if (pinnedDer.Length != presentedDer.Length) return false;

            var difference = 0;
            for (var i = 0; i < pinnedDer.Length; i++)
            {
                difference |= pinnedDer[i] ^ presentedDer[i];
            }

            return difference == 0;
        }

        /// <inheritdoc/>
        protected override bool ValidateCertificate(byte[] certificateData) => this.Accepts(certificateData);
    }
}
