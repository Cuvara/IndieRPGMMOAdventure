namespace Scripts.Session
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEngine;

    /// <summary>
    /// Backend addresses for a player, resolved once at startup: command line first, then the
    /// <c>CUVARA_*</c> environment, then the defaults the caller passes. The same flags
    /// <c>Tools/run-clients.sh</c> passes and the netcode DOTS sample reads, so one harness drives
    /// both the sample player and the real MainScene player.
    /// </summary>
    /// <remarks>
    /// The game project's own copy of the sample's <c>BackendCommandLine</c>: the sample assembly
    /// is not a dependency the DI layer may take, and this one adds an injectable
    /// <see cref="Resolve(string[], Func{string, string}, string, int, string, string)"/> so the
    /// precedence rules are unit-tested rather than believed.
    /// </remarks>
    public static class BackendCommandLine
    {
        /// <summary>Legacy <c>{"type":N,...}</c> wire encoding. First body byte <c>0x7B</c>.</summary>
        public const string EncodingJson = "json";

        /// <summary>Protobuf wire encoding, the backend's default. First body byte <c>0x08</c>.</summary>
        public const string EncodingProtobuf = "proto";

        public struct Settings
        {
            public string GatewayHost;
            public int GatewayPort;
            public string MapId;
            public bool MapExplicit;
            public string NakamaScheme;
            public string NakamaHost;
            public int NakamaPort;
            public string NakamaServerKey;
            public bool NakamaExplicit;

            /// <summary><c>-cuvara-gateway-tls</c> / <c>CUVARA_GATEWAY_TLS</c>. The gateway
            /// terminates TLS itself (ADR-23); off unless the deployment turned it on.</summary>
            public bool GatewayTls;

            /// <summary><c>-cuvara-gateway-tls-cert</c> / <c>CUVARA_GATEWAY_TLS_CERT</c>: path to a
            /// PEM certificate to pin, for a gateway holding a self-signed one. Null pins nothing
            /// and leaves the platform trust store deciding, which is the stronger default.</summary>
            public string GatewayTlsCertPath;

            /// <summary>
            /// <c>-cuvara-sealed</c> / <c>CUVARA_SEALED</c>. Ask the game server for a sealed
            /// session (ADR-22) instead of a cleartext one. <b>Off unless asked for</b>, and
            /// that default is not timidity: there is no negotiation and no fallback, so a
            /// client that seals against a server running <c>GAMESERVER_SEALED=off</c> waits
            /// for a hello that never comes and the join times out. Every deployed environment
            /// pins the server to <c>off</c> today, so defaulting this to on would break every
            /// dev run to make one environment work.
            /// </summary>
            public bool Sealed;

            /// <summary>
            /// <c>-cuvara-encoding</c> / <c>CUVARA_ENCODING</c>, normalised to
            /// <see cref="EncodingJson"/> or <see cref="EncodingProtobuf"/>.
            /// </summary>
            /// <remarks>
            /// <para>
            /// <b>Defaults to protobuf</b>, which is a deliberate change of what a plain run
            /// does. Three reasons, in order of weight:
            /// </para>
            /// <list type="number">
            /// <item><description>
            /// A JSON client can never seal. The sealed handshake messages are absent from the
            /// JSON message set on purpose, so a server running <c>GAMESERVER_SEALED=require</c>
            /// refuses a JSON client at the join with <c>encoding_cannot_seal</c>. Leaving the
            /// default at JSON would make <see cref="Sealed"/> a flag that cannot work unless a
            /// second flag is remembered alongside it.
            /// </description></item>
            /// <item><description>
            /// Protobuf is what the backend defaults to and what the package's golden vectors
            /// cover; it is ~81% smaller on the wire once id interning is counted.
            /// </description></item>
            /// <item><description>
            /// It costs no server change. Both servers sniff the first body byte
            /// (<c>0x08</c> protobuf, <c>0x7B</c> JSON) and answer in kind, so an <c>off</c>
            /// server serves a protobuf client exactly as it served a JSON one. That is the
            /// claim the unsealed acceptance run exists to check.
            /// </description></item>
            /// </list>
            /// <para>
            /// <c>-cuvara-encoding json</c> puts the old behaviour back for anyone who needs a
            /// readable capture.
            /// </para>
            /// </remarks>
            public string Encoding;

            /// <summary>True when <see cref="Encoding"/> resolved to protobuf.</summary>
            public bool EncodingIsProtobuf =>
                string.Equals(Encoding, EncodingProtobuf, StringComparison.Ordinal);

            /// <summary>
            /// True for the one combination that cannot work: sealing asked for over JSON.
            /// Reported rather than silently corrected — forcing protobuf here would be
            /// guessing which of the two flags the operator meant.
            /// </summary>
            public bool SealedOverJsonIsImpossible => Sealed && !EncodingIsProtobuf;

            public string StatusUrl;
            public bool StatusUrlExplicit;

            /// <summary><c>-cuvara-device</c> / <c>CUVARA_DEVICE_ID</c>, verbatim; null when not given.</summary>
            public string DeviceId;

            /// <summary><c>-cuvara-instance</c> / <c>CUVARA_INSTANCE</c>; null when not given.</summary>
            public string InstanceLabel;

            public string NakamaBaseUrl => $"{NakamaScheme}://{NakamaHost}:{NakamaPort}";
        }

        /// <summary>Resolves from the process command line and environment.</summary>
        public static Settings Resolve(string defaultGatewayHost, int defaultGatewayPort, string defaultMapId, string defaultStatusUrl)
        {
            return Resolve(SafeArgs(), EnvThenFile(), defaultGatewayHost, defaultGatewayPort, defaultMapId, defaultStatusUrl);
        }

        /// <summary>Resolves from explicit arguments and an environment lookup — the testable core.</summary>
        public static Settings Resolve(
            string[] args,
            Func<string, string> env,
            string defaultGatewayHost,
            int defaultGatewayPort,
            string defaultMapId,
            string defaultStatusUrl)
        {
            args ??= Array.Empty<string>();
            env ??= _ => null;

            var s = new Settings
            {
                GatewayHost = Str(args, env, "-cuvara-gateway-host", "CUVARA_GATEWAY_HOST", defaultGatewayHost),
                GatewayPort = Int(args, env, "-cuvara-gateway-port", "CUVARA_GATEWAY_PORT", defaultGatewayPort),
                NakamaScheme = Str(args, env, "-cuvara-nakama-scheme", "CUVARA_NAKAMA_SCHEME", "http"),
                NakamaHost = Str(args, env, "-cuvara-nakama-host", "CUVARA_NAKAMA_HOST", "127.0.0.1"),
                NakamaPort = Int(args, env, "-cuvara-nakama-port", "CUVARA_NAKAMA_PORT", 7350),
                NakamaServerKey = Str(args, env, "-cuvara-nakama-key", "CUVARA_NAKAMA_SERVER_KEY", "defaultkey"),
                GatewayTls = Bool(args, env, "-cuvara-gateway-tls", "CUVARA_GATEWAY_TLS", false),
                GatewayTlsCertPath = Str(args, env, "-cuvara-gateway-tls-cert", "CUVARA_GATEWAY_TLS_CERT", null),
                Sealed = Bool(args, env, "-cuvara-sealed", "CUVARA_SEALED", false),
                Encoding = EncodingOf(args, env, "-cuvara-encoding", "CUVARA_ENCODING", EncodingProtobuf),
                NakamaExplicit =
                    Str(args, env, "-cuvara-nakama-scheme", "CUVARA_NAKAMA_SCHEME", null) != null ||
                    Str(args, env, "-cuvara-nakama-host", "CUVARA_NAKAMA_HOST", null) != null ||
                    Str(args, env, "-cuvara-nakama-port", "CUVARA_NAKAMA_PORT", null) != null,
                DeviceId = Str(args, env, "-cuvara-device", "CUVARA_DEVICE_ID", null),
                InstanceLabel = Str(args, env, "-cuvara-instance", "CUVARA_INSTANCE", null),
            };

            var map = Str(args, env, "-cuvara-map", "CUVARA_MAP_ID", null);
            s.MapExplicit = !string.IsNullOrEmpty(map);
            s.MapId = s.MapExplicit ? map : defaultMapId;

            var status = Str(args, env, "-cuvara-status-url", "CUVARA_STATUS_URL", null);
            s.StatusUrlExplicit = !string.IsNullOrEmpty(status);
            s.StatusUrl = s.StatusUrlExplicit ? status : defaultStatusUrl;

            return s;
        }

        private static int generatedDeviceIds;

        /// <summary>
        /// The device id this process authenticates with, or null to let the Nakama layer use
        /// the machine's own identifier: an explicit <c>-cuvara-device</c> wins; an instance
        /// label alone yields a generated id so several instances on one machine are different
        /// accounts; neither means a single ordinary player.
        /// </summary>
        /// <remarks>
        /// Generated format: <c>{prefix}-{label}-{pid}-{ticks}-{n}</c>, where <c>n</c> is a
        /// process-wide counter. The guarantee is uniqueness per call within a process and per
        /// process on a machine (pid); two processes on different machines that share a pid and
        /// a tick are not a case this needs to cover. The id is NOT stable across launches —
        /// every launch is a fresh account — which is what a throwaway harness instance wants.
        /// </remarks>
        public static string ResolveDeviceIdOrNull(in Settings settings, string fallbackPrefix)
        {
            if (!string.IsNullOrEmpty(settings.DeviceId)) return settings.DeviceId;
            if (string.IsNullOrEmpty(settings.InstanceLabel)) return null;

            var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
            var sequence = System.Threading.Interlocked.Increment(ref generatedDeviceIds);
            return $"{fallbackPrefix}-{settings.InstanceLabel}-{pid}-{DateTime.UtcNow.Ticks}-{sequence}";
        }

        private static string[] SafeArgs()
        {
            try
            {
                return Environment.GetCommandLineArgs() ?? Array.Empty<string>();
            }
            catch (Exception)
            {
                // Some platforms (WebGL) deny the command line outright. The environment
                // fallback still works, so this must not be fatal.
                return Array.Empty<string>();
            }
        }

        private static string Str(string[] args, Func<string, string> env, string flag, string envName, string fallback)
        {
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.Ordinal) && !string.IsNullOrEmpty(args[i + 1]))
                {
                    return args[i + 1];
                }
            }

            var fromEnv = env(envName);
            return string.IsNullOrEmpty(fromEnv) ? fallback : fromEnv;
        }

        private static int Int(string[] args, Func<string, string> env, string flag, string envName, int fallback)
        {
            var raw = Str(args, env, flag, envName, null);
            if (string.IsNullOrEmpty(raw)) return fallback;

            if (int.TryParse(raw, out var parsed) && parsed > 0 && parsed <= 65535) return parsed;

            Debug.LogWarning($"[backend-args] {flag}='{raw}' is not a usable port — keeping {fallback}.");
            return fallback;
        }

        /// <summary>
        /// Reads a boolean flag. Accepts the spellings a shell script and a CI variable
        /// actually produce; anything else warns and keeps the fallback.
        /// </summary>
        /// <remarks>
        /// Deliberately does NOT treat an unrecognised value as true. This gates TLS, and
        /// "CUVARA_GATEWAY_TLS=maybe" quietly meaning "on" would be no better than it
        /// quietly meaning "off" — either way the operator's intent is guessed. Warning and
        /// keeping the documented default at least says so out loud.
        /// </remarks>
        private static bool Bool(string[] args, Func<string, string> env, string flag, string envName, bool fallback)
        {
            var raw = Str(args, env, flag, envName, null);
            if (string.IsNullOrEmpty(raw)) return fallback;

            switch (raw.Trim().ToLowerInvariant())
            {
                case "1":
                case "true":
                case "yes":
                case "on":
                    return true;
                case "0":
                case "false":
                case "no":
                case "off":
                    return false;
                default:
                    Debug.LogWarning($"[backend-args] {flag}='{raw}' is not a boolean — keeping {fallback}.");
                    return fallback;
            }
        }

        /// <summary>
        /// Reads the wire encoding, normalising the spellings a script or a CI variable
        /// actually produces. Same refusal-to-guess rule as <see cref="Bool"/>: an
        /// unrecognised value warns and keeps the fallback rather than being read as one of
        /// the two, because picking wrong here does not fail loudly — it fails at the join,
        /// several steps away from the typo.
        /// </summary>
        private static string EncodingOf(string[] args, Func<string, string> env, string flag, string envName, string fallback)
        {
            var raw = Str(args, env, flag, envName, null);
            if (string.IsNullOrEmpty(raw)) return fallback;

            switch (raw.Trim().ToLowerInvariant())
            {
                case "json":
                    return EncodingJson;
                case "proto":
                case "protobuf":
                case "pb":
                    return EncodingProtobuf;
                default:
                    Debug.LogWarning(
                        $"[backend-args] {flag}='{raw}' is not a known encoding (json|proto) — keeping {fallback}.");
                    return fallback;
            }
        }

        /// <summary>
        /// The file name, inside <see cref="Application.persistentDataPath"/>, that supplies
        /// <c>CUVARA_*</c> values on a platform with no command line and no settable environment.
        /// </summary>
        public const string ConfigFileName = "backend.env";

        private static Dictionary<string, string> _fileValues;

        /// <summary>
        /// Environment lookup that falls back to a <c>KEY=VALUE</c> file on disk.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Why this exists: an Android build could not be pointed at a backend at all.</b>
        /// Every override here is a command-line flag or a <c>CUVARA_*</c> environment variable,
        /// and an Android app has neither — no argv to read, and no way to set the process
        /// environment without a debuggable wrap.sh. The defaults are a developer loopback
        /// (<c>127.0.0.1:8000</c>, Nakama's published <c>defaultkey</c>), so an Android player
        /// could only ever reach a backend that happened to match them. It could be built and
        /// installed; it could not be aimed.
        /// </para>
        /// <para>
        /// <b>Same names, lowest precedence.</b> The file uses the <c>CUVARA_*</c> names rather
        /// than inventing a second vocabulary, and it is consulted only when the real environment
        /// has nothing — so command line beats environment beats file beats default, and a
        /// desktop run is unaffected by a file someone forgot to delete.
        /// </para>
        /// <para>
        /// Read once. A missing file is the normal case and is silent; an unreadable one warns
        /// and is ignored, because a player that refuses to start over a config file is worse
        /// than one that starts on its defaults and says so.
        /// </para>
        /// <example>
        /// <code>
        /// adb push backend.env /sdcard/Android/data/&lt;package&gt;/files/backend.env
        /// </code>
        /// </example>
        /// </remarks>
        private static Func<string, string> EnvThenFile()
        {
            Dictionary<string, string> file = LoadConfigFile();
            if (file == null || file.Count == 0) return SafeEnv;

            return name =>
            {
                string fromEnv = SafeEnv(name);
                if (!string.IsNullOrEmpty(fromEnv)) return fromEnv;
                return file.TryGetValue(name, out string v) ? v : null;
            };
        }

        private static Dictionary<string, string> LoadConfigFile()
        {
            if (_fileValues != null) return _fileValues;
            _fileValues = new Dictionary<string, string>(StringComparer.Ordinal);

            try
            {
                string path = Path.Combine(Application.persistentDataPath, ConfigFileName);
                if (!File.Exists(path)) return _fileValues;

                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                    {
                        Debug.LogWarning($"[Backend] {ConfigFileName}: ignoring line without '=': {line}");
                        continue;
                    }

                    _fileValues[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }

                Debug.Log($"[Backend] read {_fileValues.Count} override(s) from {path}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Backend] could not read {ConfigFileName}: {e.Message}");
            }

            return _fileValues;
        }

        private static string SafeEnv(string name)
        {
            try
            {
                return Environment.GetEnvironmentVariable(name);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
