namespace Tests.Editor
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using Scripts.Session;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>
    /// Precedence and parsing of the backend flags <c>Tools/run-clients.sh</c> passes: command
    /// line over environment over defaults, ports validated, device identity resolved per process.
    /// </summary>
    public sealed class BackendCommandLineTests
    {
        private static BackendCommandLine.Settings Resolve(string[] args = null, Dictionary<string, string> env = null)
        {
            env ??= new Dictionary<string, string>();
            return BackendCommandLine.Resolve(
                args,
                name => env.TryGetValue(name, out var value) ? value : null,
                "gw.default", 8000, "map_01", "http://status.default");
        }

        [Test]
        public void Defaults_WhenNothingIsGiven()
        {
            var s = Resolve();

            Assert.That(s.GatewayHost, Is.EqualTo("gw.default"));
            Assert.That(s.GatewayPort, Is.EqualTo(8000));
            Assert.That(s.MapId, Is.EqualTo("map_01"));
            Assert.That(s.MapExplicit, Is.False);
            Assert.That(s.NakamaBaseUrl, Is.EqualTo("http://127.0.0.1:7350"));
            Assert.That(s.NakamaServerKey, Is.EqualTo("defaultkey"));
            Assert.That(s.NakamaExplicit, Is.False);
            Assert.That(s.StatusUrl, Is.EqualTo("http://status.default"));
            Assert.That(s.DeviceId, Is.Null);
            Assert.That(s.InstanceLabel, Is.Null);
        }

        [Test]
        public void CommandLine_WinsOverEnvironment_WinsOverDefaults()
        {
            var env = new Dictionary<string, string>
            {
                ["CUVARA_GATEWAY_HOST"] = "gw.env",
                ["CUVARA_GATEWAY_PORT"] = "7000",
                ["CUVARA_MAP_ID"] = "map_env",
                ["CUVARA_NAKAMA_HOST"] = "nk.env",
            };
            var args = new[]
            {
                "player.exe",
                "-cuvara-gateway-host", "gw.cli",
                "-cuvara-nakama-key", "secret",
                "-cuvara-map", "map_cli",
            };

            var s = Resolve(args, env);

            Assert.That(s.GatewayHost, Is.EqualTo("gw.cli"), "flag over env");
            Assert.That(s.GatewayPort, Is.EqualTo(7000), "env over default");
            Assert.That(s.MapId, Is.EqualTo("map_cli"));
            Assert.That(s.MapExplicit, Is.True);
            Assert.That(s.NakamaHost, Is.EqualTo("nk.env"));
            Assert.That(s.NakamaExplicit, Is.True);
            Assert.That(s.NakamaServerKey, Is.EqualTo("secret"));
        }

        [Test]
        public void TheHarnessFlagSet_IsFullyRead()
        {
            // Exactly the flags Tools/run-clients.sh passes, in its order.
            var args = new[]
            {
                "player.exe",
                "-cuvara-gateway-host", "127.0.0.1",
                "-cuvara-gateway-port", "7000",
                "-cuvara-nakama-scheme", "https",
                "-cuvara-nakama-host", "nakama.local",
                "-cuvara-nakama-port", "7001",
                "-cuvara-nakama-key", "k",
                "-cuvara-map", "map_01",
                "-cuvara-device", "mc-abc-2",
                "-cuvara-instance", "2",
                "-cuvara-status-url", "http://127.0.0.1:19100/status",
            };

            var s = Resolve(args);

            Assert.That(s.GatewayPort, Is.EqualTo(7000));
            Assert.That(s.NakamaBaseUrl, Is.EqualTo("https://nakama.local:7001"));
            Assert.That(s.NakamaServerKey, Is.EqualTo("k"));
            Assert.That(s.DeviceId, Is.EqualTo("mc-abc-2"));
            Assert.That(s.InstanceLabel, Is.EqualTo("2"));
            Assert.That(s.StatusUrl, Is.EqualTo("http://127.0.0.1:19100/status"));
            Assert.That(s.StatusUrlExplicit, Is.True);
        }

        [Test]
        public void BadPort_KeepsTheFallback_AndWarns()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("not a usable port"));

            var s = Resolve(new[] { "x", "-cuvara-gateway-port", "99999" });

            Assert.That(s.GatewayPort, Is.EqualTo(8000));
        }

        [Test]
        public void EmptyFlagValue_FallsThrough()
        {
            var s = Resolve(new[] { "x", "-cuvara-map", "" }, new Dictionary<string, string> { ["CUVARA_MAP_ID"] = "map_env" });

            Assert.That(s.MapId, Is.EqualTo("map_env"));
        }

        [Test]
        public void DeviceId_ExplicitWins_InstanceLabelMakesAPerProcessId_NeitherMeansMachine()
        {
            var explicitId = Resolve(new[] { "x", "-cuvara-device", "dev-7", "-cuvara-instance", "3" });
            Assert.That(BackendCommandLine.ResolveDeviceIdOrNull(explicitId, "mainscene"), Is.EqualTo("dev-7"));

            var labelled = Resolve(new[] { "x", "-cuvara-instance", "3" });
            var generated = new System.Collections.Generic.HashSet<string>();
            for (var i = 0; i < 50; i++)
            {
                var id = BackendCommandLine.ResolveDeviceIdOrNull(labelled, "mainscene");
                StringAssert.StartsWith("mainscene-3-", id);
                Assert.That(generated.Add(id), Is.True, "every call yields a distinct id, even within one clock tick");
            }

            Assert.That(BackendCommandLine.ResolveDeviceIdOrNull(Resolve(), "mainscene"), Is.Null,
                "a plain player authenticates as the machine");
        }
        [Test]
        public void Sealing_IsOffUnlessAskedFor_AndEncodingDefaultsToProtobuf()
        {
            var s = Resolve();

            Assert.That(s.Sealed, Is.False,
                "every deployed environment runs GAMESERVER_SEALED=off; a client that seals by " +
                "default would stall the join everywhere");
            Assert.That(s.Encoding, Is.EqualTo(BackendCommandLine.EncodingProtobuf));
            Assert.That(s.EncodingIsProtobuf, Is.True);
            Assert.That(s.SealedOverJsonIsImpossible, Is.False);
        }

        [Test]
        public void Sealing_ReadsTheFlagThenTheEnvironment()
        {
            Assert.That(Resolve(new[] { "x", "-cuvara-sealed", "1" }).Sealed, Is.True);
            Assert.That(Resolve(new[] { "x", "-cuvara-sealed", "on" }).Sealed, Is.True);
            Assert.That(Resolve(null, new Dictionary<string, string> { ["CUVARA_SEALED"] = "true" }).Sealed, Is.True);

            Assert.That(
                Resolve(new[] { "x", "-cuvara-sealed", "0" },
                    new Dictionary<string, string> { ["CUVARA_SEALED"] = "1" }).Sealed,
                Is.False,
                "the command line wins over the environment");
        }

        [Test]
        public void Encoding_AcceptsTheSpellingsAScriptProduces()
        {
            Assert.That(Resolve(new[] { "x", "-cuvara-encoding", "json" }).Encoding,
                Is.EqualTo(BackendCommandLine.EncodingJson));
            Assert.That(Resolve(new[] { "x", "-cuvara-encoding", "JSON" }).Encoding,
                Is.EqualTo(BackendCommandLine.EncodingJson));
            Assert.That(Resolve(new[] { "x", "-cuvara-encoding", "protobuf" }).EncodingIsProtobuf, Is.True);
            Assert.That(Resolve(new[] { "x", "-cuvara-encoding", "pb" }).EncodingIsProtobuf, Is.True);
            Assert.That(Resolve(null, new Dictionary<string, string> { ["CUVARA_ENCODING"] = "json" }).Encoding,
                Is.EqualTo(BackendCommandLine.EncodingJson));
        }

        [Test]
        public void Encoding_UnknownValue_WarnsAndKeepsTheDefault()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("not a known encoding"));

            var s = Resolve(new[] { "x", "-cuvara-encoding", "cbor" });

            Assert.That(s.EncodingIsProtobuf, Is.True,
                "an unrecognised encoding must not be guessed into one of the two");
        }

        [Test]
        public void SealedOverJson_IsFlaggedAsImpossible()
        {
            var s = Resolve(new[] { "x", "-cuvara-sealed", "1", "-cuvara-encoding", "json" });

            Assert.That(s.Sealed, Is.True);
            Assert.That(s.EncodingIsProtobuf, Is.False);
            Assert.That(s.SealedOverJsonIsImpossible, Is.True,
                "the JSON message set has no sealed handshake, so the server refuses this at the join");

            Assert.That(Resolve(new[] { "x", "-cuvara-sealed", "1" }).SealedOverJsonIsImpossible, Is.False);
        }
    }
}