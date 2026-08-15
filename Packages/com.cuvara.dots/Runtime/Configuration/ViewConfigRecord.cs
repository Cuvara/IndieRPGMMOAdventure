using Unity.Collections;
using Unity.Mathematics;

namespace Cuvara.DOTS.Configuration
{
    /// <summary>
    /// One <see cref="ViewConfig"/> in unmanaged form, as stored inside <see cref="ViewConfigTable"/>.
    /// </summary>
    /// <remarks>
    /// Blittable and fixed-size so it can live in a blob and be read from a Bursted job. The string
    /// key becomes a <see cref="FixedString64Bytes"/> for the same reason
    /// <c>EntityViewRequest</c> uses one: a managed string cannot be reached from a job, and the
    /// pool is asked for it by value.
    /// </remarks>
    public struct ViewConfigRecord
    {
        /// <summary>Stable hash of the archetype name, for lookup without a managed string.</summary>
        public int NameHash;

        public FixedString64Bytes ViewKey;
        public int PoolSize;
        public float Scale;
        public float3 PositionOffset;
        public quaternion RotationOffset;

        /// <summary>2D sorting layer. Carried, not yet applied — no sprite path exists in the package.</summary>
        public int SortingLayerId;

        /// <summary>2D sorting order. Carried, not yet applied.</summary>
        public int SortingOrder;
    }
}
