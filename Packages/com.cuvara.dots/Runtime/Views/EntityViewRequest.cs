using Unity.Collections;
using Unity.Entities;

namespace Cuvara.DOTS.Views
{
    /// <summary>
    /// "This entity wants a view of this prefab." Removed once the view exists.
    /// </summary>
    /// <remarks>
    /// The key is a <see cref="FixedString64Bytes"/> so the request stays unmanaged and can be
    /// written from a job. Resolving it to a <see cref="string"/> for the pool allocates, once per
    /// spawn — acceptable at spawn rates, and the alternative (an interned id table) is a
    /// different change that should be made when spawn rate is measured to matter.
    /// </remarks>
    public struct EntityViewRequest : IComponentData
    {
        /// <summary>Asset/pool key of the view prefab.</summary>
        public FixedString64Bytes ViewKey;
    }
}
