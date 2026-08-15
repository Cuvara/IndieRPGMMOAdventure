using Unity.Entities;

namespace Cuvara.DOTS.Views
{
    /// <summary>
    /// Links an entity to the GameObject view instance standing in for it.
    /// </summary>
    /// <remarks>
    /// Holds an integer handle rather than the <see cref="UnityEngine.GameObject"/> itself so the
    /// component stays unmanaged, stays in chunk memory, and can be read from a Bursted job. The
    /// handle resolves through <see cref="EntityViewRegistry"/> on the main thread.
    /// </remarks>
    public struct EntityViewLink : IComponentData
    {
        /// <summary>Registry handle. Zero is never a valid handle.</summary>
        public int ViewId;
    }
}
