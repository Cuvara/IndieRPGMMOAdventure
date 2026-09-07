namespace Scripts.Session
{
    using System;
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
            return Resolve(SafeArgs(), SafeEnv, defaultGatewayHost, defaultGatewayPort, defaultMapId, defaultStatusUrl);
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
