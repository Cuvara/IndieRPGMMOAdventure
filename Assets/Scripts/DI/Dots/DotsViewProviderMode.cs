#if CUVARA_DOTS
namespace Scripts.DI.Dots
{
    /// <summary>Which <c>IViewAssetProvider</c> <c>RegisterDots</c> stands up.</summary>
    public enum DotsViewProviderMode
    {
        /// <summary>
        /// <see cref="LeasedViewAssetProvider"/>: the package's pooled provider over Addressables
        /// prefabs from the <see cref="DotsViewLibraryAsset"/>. The MainScene path.
        /// </summary>
        Production = 0,

        /// <summary>
        /// <see cref="PrimitiveViewAssetProvider"/>: capsules and spheres, no assets. The netcode
        /// sample, device benchmark and any scene without authored art.
        /// </summary>
        Primitive = 1,
    }

    /// <summary>
    /// Container-registered holder for the session's view library so a scene component can
    /// inject it without VContainer refusing an absent asset — the field is null in
    /// <see cref="DotsViewProviderMode.Primitive"/> mode.
    /// </summary>
    public sealed class DotsViewLibraryReference
    {
        public DotsViewLibraryAsset Asset;

        public DotsViewProviderMode Mode;
    }
}
#endif
