namespace Scripts.DI
{
    using UnityEngine;
#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER
    using Scripts.DI.Dots;
    using Unity.Entities;
#endif

    /// <summary>
    /// Everything <see cref="ClientRootInstaller"/> needs, already resolved. The installer reads no
    /// command line, environment, file or PEM: <c>GameLifetimeScope</c> does that and hands the
    /// results over here, so a test can build the production composition from plain values.
    /// </summary>
    public sealed class RootInstallerArgs
    {
        /// <summary>Resolved backend addresses and the device id; registered as an instance.</summary>
        public BackendSettings Backend;

        /// <summary>Gateway TLS pin (PEM bytes) or null; see <c>TransportSecurityReport.LoadPinOrNull</c>.</summary>
        public byte[] GatewayPinnedCertificate;

        /// <summary>Nakama TLS pin (PEM bytes) or null; see <c>TransportSecurityReport.LoadNakamaPinOrNull</c>.</summary>
        public byte[] NakamaPinnedCertificate;

        /// <summary>Parent for spawned DOTS views; the root scope's transform in production, null in tests.</summary>
        public Transform ViewRoot;

#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER
        /// <summary>ECS world for the view bootstrap; null means the default injection world at build time.</summary>
        public World World;

        /// <summary>View provider mode; production is <see cref="DotsViewProviderMode.Production"/>.</summary>
        public DotsViewProviderMode Mode = DotsViewProviderMode.Production;

        /// <summary>View library override; null loads it from Resources.</summary>
        public DotsViewLibraryAsset Library;

        /// <summary>Prefab loader override; null means Addressables.</summary>
        public IViewPrefabLoader Loader;
#endif
    }
}
