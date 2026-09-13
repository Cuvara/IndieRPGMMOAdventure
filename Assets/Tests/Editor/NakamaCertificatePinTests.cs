namespace Tests.Editor
{
    using System;
    using System.IO;
    using Cuvara.Netcode.Transport;
    using NUnit.Framework;
    using Scripts.DI;
    using Scripts.Nakama.Tls;
    using Scripts.Session;

    /// <summary>
    /// The Nakama hop's certificate pin (ADR-24).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>These are mostly refusal tests, deliberately.</b> A handler that accepts the right
    /// certificate is observable in any working deploy; a handler that accepts <i>everything</i>
    /// is observable in none — it looks identical for as long as only the right certificate is
    /// ever presented, which in dev is always. So the certificate that must be rejected is a
    /// real, different, self-signed certificate with the SAME subject as the pinned one, and it
    /// is asserted on here rather than assumed.
    /// </para>
    /// <para>
    /// <c>ValidateCertificate</c> is <c>protected</c> on Unity's <c>CertificateHandler</c> and
    /// cannot be invoked from a test, so these drive <c>Accepts</c>, which the override does
    /// nothing but call. What that leaves unproven is Unity's own plumbing — that it calls the
    /// override at all on a real handshake — which no EditMode test can cover.
    /// </para>
    /// </remarks>
    public class NakamaCertificatePinTests
    {
        private const string PinnedPem =
            "-----BEGIN CERTIFICATE-----\n" +
            "MIIDDTCCAfWgAwIBAgIUPppBbkUYuRR3l1rNH9/gJWMYctIwDQYJKoZIhvcNAQEL\n" +
            "BQAwFjEUMBIGA1UEAwwLbmFrYW1hLnRlc3QwHhcNMjYwOTEzMDQ1NDI0WhcNMzYw\n" +
            "OTEwMDQ1NDI0WjAWMRQwEgYDVQQDDAtuYWthbWEudGVzdDCCASIwDQYJKoZIhvcN\n" +
            "AQEBBQADggEPADCCAQoCggEBANEzhsdzVOeU2q0AlpqYMg48SGPfwo7Cagx7rnLg\n" +
            "tmvoWewRs05mRBKv442N6+L0QROIt6XaDI8Gaj8ZUiwVsJrp+usrYNhnjvRXDYZA\n" +
            "dkUH2lDTloyLBzXluzJtyXOvA7wazc9ie5lK19oPmheteqmzLF7/t0OPjgfzQyzT\n" +
            "2JoP/qXpcqwh9crTd8JiTJ8R7IbBB6f0KulDyNu1G6YHE6jZxu10+ZDLgTnbEF2v\n" +
            "AxWQlGWhg9JrcW66ig8wC5GKNVvODcvI5069IIRNs3H1i2AZiEHwRvLcCDPzpQm6\n" +
            "5Btm7AWYZIeZVuSrdTd54n2wfCHKnrFUP1F6R4ZK5KVZiLUCAwEAAaNTMFEwHQYD\n" +
            "VR0OBBYEFOH53IqyAcdaMOaiyRW55M/I0orIMB8GA1UdIwQYMBaAFOH53IqyAcda\n" +
            "MOaiyRW55M/I0orIMA8GA1UdEwEB/wQFMAMBAf8wDQYJKoZIhvcNAQELBQADggEB\n" +
            "AGz1OgovrFQOQmTL1HKcEdRknjuMavB+ksgA8BBIba3Odr0fuuWbrbTkdaT7NtdH\n" +
            "/WXUYwG6UO3l2nlcoKtCOOkVzt6UpxhVGSIRXSY4cukStQbBoowziHHC+wEb9fwa\n" +
            "4axgT0NYlz5cCvHFZGfjxtpRXZmGHhaulTbvgH0YGiQy/5xGa05p/5unmDv3Jjg6\n" +
            "A9QpVCkVKYLFrZHNJ8MssezQm5GSdoWPqZo4RZdV/nE8dfEQsA7qCLYhx1p93Dzs\n" +
            "jXHKhvUH9gVdOx1P07VAlfmm/QGwoQSQjGDZbo/idiq/r1dn1sc0l3nqD5yxo4ty\n" +
            "RS8YJdRnVa0xA+jbDQ4VFt4=\n" +
            "-----END CERTIFICATE-----\n";

        private const string ImpostorPem =
            "-----BEGIN CERTIFICATE-----\n" +
            "MIIDDTCCAfWgAwIBAgIUZwj+V7bO7loc3uTaGGPAY1WZeJEwDQYJKoZIhvcNAQEL\n" +
            "BQAwFjEUMBIGA1UEAwwLbmFrYW1hLnRlc3QwHhcNMjYwOTEzMDQ1NDI1WhcNMzYw\n" +
            "OTEwMDQ1NDI1WjAWMRQwEgYDVQQDDAtuYWthbWEudGVzdDCCASIwDQYJKoZIhvcN\n" +
            "AQEBBQADggEPADCCAQoCggEBANqt2zFeDvwfwl9oRBSjc8+8fqfzHuF54AuCdFEl\n" +
            "XBAWf5XnIs8ZrSdTHR9gIm/xTCWfEZgpws2Nsr8I5bZ69AzIE6yGOkHMUvt15ZyA\n" +
            "BwKsOrePmm5T/nURP4+5htF2TbxfvZG28EAq+K8oHO6VbLCQrphqbuc8f6a3x1Ly\n" +
            "6cJiK3mv3tiPtZpkZgCZz9Ah1F50POdtnDuuzq0qjFHX2NbMAehwRaI3cMq0CPvm\n" +
            "YXfcPzjO0cvWFoTo+MQYDP7INYjzej1h9UECa4+1iY/qwxj947NlSLBSbi1d30UO\n" +
            "4k98zDM64jW6iczHXlDD31BimhhY0wryfBHVcCHXzzDhW60CAwEAAaNTMFEwHQYD\n" +
            "VR0OBBYEFONZYexfh1z4gAzY2r0JE++LinxJMB8GA1UdIwQYMBaAFONZYexfh1z4\n" +
            "gAzY2r0JE++LinxJMA8GA1UdEwEB/wQFMAMBAf8wDQYJKoZIhvcNAQELBQADggEB\n" +
            "AGleDzNi6/Z/+nFILllHXvQnbotabmF5LeDT+iU7cGa7OWuRbiaY/gSNgSYm+NrR\n" +
            "yPBHgBvIvwnnF4TJ2iZpDO4IOIhHHb/DTFvZyVcf32IAWAD1GrCKA2Xz59uknd3Y\n" +
            "gJMaX+pa6CGtQpUtWwMdYwoG0k+HeWUkZq73DwvfjRNkWNFBzibbcT/TfIUluGSE\n" +
            "UJDmidkl9Z1ZSWZREspcEPr+w1DEIGdHfsU1/fWHju+B7nXFPcmEVT2UY7gr1rXq\n" +
            "vGQEfyggDttTFd/stfO//Bs0bR13vs0pEr06Zam3JDxSuA/EgACRocZJLSh3sO+7\n" +
            "S1Hq6Lrs66D4j80pHuXpoeI=\n" +
            "-----END CERTIFICATE-----\n";

        private static byte[] PinnedDer => TlsOptions.FromPem(PinnedPem);

        private static byte[] ImpostorDer => TlsOptions.FromPem(ImpostorPem);

        [Test]
        public void AcceptsTheCertificateItPinned()
        {
            using var handler = new PinnedCertificateHandler(PinnedDer);

            Assert.IsTrue(handler.Accepts(PinnedDer));
        }

        [Test]
        public void RefusesADifferentCertificateWithTheSameSubject()
        {
            using var handler = new PinnedCertificateHandler(PinnedDer);

            Assert.IsFalse(handler.Accepts(ImpostorDer),
                "a different self-signed certificate for the same name must be refused -- " +
                "if this passes, the pin is not comparing what it claims to compare");
        }

        [Test]
        public void RefusesASingleFlippedByte()
        {
            var tampered = (byte[])PinnedDer.Clone();
            tampered[tampered.Length - 1] ^= 0x01;

            using var handler = new PinnedCertificateHandler(PinnedDer);

            Assert.IsFalse(handler.Accepts(tampered));
        }

        [Test]
        public void RefusesATruncatedCertificate()
        {
            var truncated = new byte[PinnedDer.Length - 1];
            Array.Copy(PinnedDer, truncated, truncated.Length);

            using var handler = new PinnedCertificateHandler(PinnedDer);

            Assert.IsFalse(handler.Accepts(truncated));
        }

        [Test]
        public void RefusesNothingAtAll()
        {
            using var handler = new PinnedCertificateHandler(PinnedDer);

            Assert.IsFalse(handler.Accepts(null));
            Assert.IsFalse(handler.Accepts(Array.Empty<byte>()));
        }

        [Test]
        public void ThereIsNoAcceptAnythingHandler()
        {
            // The only way to spell "trust everything" would be a handler with no pin, so the
            // constructor refuses to build one. This test is the guard against someone adding
            // that convenience later.
            Assert.Throws<ArgumentException>(() => new PinnedCertificateHandler(null));
            Assert.Throws<ArgumentException>(() => new PinnedCertificateHandler(Array.Empty<byte>()));
        }

        [Test]
        public void MatchesIsFalseForAnAbsentPin()
        {
            Assert.IsFalse(PinnedCertificateHandler.Matches(null, PinnedDer));
            Assert.IsFalse(PinnedCertificateHandler.Matches(Array.Empty<byte>(), PinnedDer));
        }

        [Test]
        public void TheFlagReachesTheSettings()
        {
            var settings = BackendCommandLine.Resolve(
                new[] { "player", "-cuvara-nakama-scheme", "https", "-cuvara-nakama-tls-cert", "/tmp/nakama.crt" },
                _ => null, "127.0.0.1", 8000, "map_01", "http://127.0.0.1:9101/status");

            Assert.AreEqual("/tmp/nakama.crt", settings.NakamaTlsCertPath);
            Assert.IsFalse(settings.NakamaPinWithoutHttps);
        }

        [Test]
        public void APinOnAPlaintextSchemeIsReportedAndNotLoaded()
        {
            var settings = BackendCommandLine.Resolve(
                new[] { "player", "-cuvara-nakama-tls-cert", "/tmp/nakama.crt" },
                _ => null, "127.0.0.1", 8000, "map_01", "http://127.0.0.1:9101/status");

            Assert.IsTrue(settings.NakamaPinWithoutHttps,
                "http plus a pinned certificate is the one combination that cannot work");
            Assert.IsNull(TransportSecurityReport.LoadNakamaPinOrNull(settings),
                "a pin must not be loaded for a plaintext hop -- it would read as protection " +
                "that is not there");
        }

        [Test]
        public void AReadablePemLoadsAndAnUnreadableOneDoesNot()
        {
            var path = Path.Combine(Path.GetTempPath(), "cuvara-nakama-pin-test.crt");
            File.WriteAllText(path, PinnedPem);
            try
            {
                var settings = BackendCommandLine.Resolve(
                    new[] { "player", "-cuvara-nakama-scheme", "https", "-cuvara-nakama-tls-cert", path },
                    _ => null, "127.0.0.1", 8000, "map_01", "http://127.0.0.1:9101/status");

                Assert.AreEqual(PinnedDer, TransportSecurityReport.LoadNakamaPinOrNull(settings));
            }
            finally
            {
                File.Delete(path);
            }

            var missing = BackendCommandLine.Resolve(
                new[] { "player", "-cuvara-nakama-scheme", "https", "-cuvara-nakama-tls-cert", path },
                _ => null, "127.0.0.1", 8000, "map_01", "http://127.0.0.1:9101/status");

            // Falls back to platform validation -- the safe direction -- after logging an error.
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex(
                "could not load the pinned Nakama certificate"));
            Assert.IsNull(TransportSecurityReport.LoadNakamaPinOrNull(missing));
        }
    }
}
