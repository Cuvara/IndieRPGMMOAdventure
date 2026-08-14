using Unity.Entities;

namespace Cuvara.DOTS.Configuration
{
    /// <summary>
    /// Singleton pointing at the session's <see cref="ViewConfigTable"/>.
    /// </summary>
    /// <remarks>
    /// Unmanaged, so a Bursted system can take it from <c>SystemAPI.GetSingleton</c> without the
    /// managed-component detour the view registry needs. Installed by
    /// <see cref="ViewConfigCatalog.Install"/>.
    /// </remarks>
    public struct ViewConfigTableReference : IComponentData
    {
        public BlobAssetReference<ViewConfigTable> Table;
    }
}
