namespace Scripts.DI
{
    using Scripts.Session;
    using VContainer;
    using VContainer.Unity;

    public class GameLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            base.Configure(builder);

            // Backend addresses come from the command line / CUVARA_* environment — the flags
            // Tools/run-clients.sh passes — with localhost defaults for a plain Editor run. Resolved
            // once, here, because NetworkSettings and NakamaSettings are container instances and
            // the addresses have to be known before the client and the Nakama service exist.
            // All I/O (command line, environment, backend.env, PEM files) stays in this method;
            // ClientRootInstaller only registers what it is handed.
            var backend = BackendCommandLine.Resolve("127.0.0.1", 8000, "map_01", "http://127.0.0.1:9101/status");
            var deviceId = BackendCommandLine.ResolveDeviceIdOrNull(backend, "mainscene");

            TransportSecurityReport.Warn(backend);

            ClientRootInstaller.Install(
                builder,
                new RootInstallerArgs
                {
                    Backend = new BackendSettings { Value = backend, DeviceId = deviceId },
                    GatewayPinnedCertificate = TransportSecurityReport.LoadPinOrNull(backend),
                    NakamaPinnedCertificate = TransportSecurityReport.LoadNakamaPinOrNull(backend),
                    ViewRoot = transform,
                });
        }
    }
}
