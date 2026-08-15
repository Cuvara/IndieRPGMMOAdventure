using Unity.Entities;

namespace Cuvara.DOTS.Views
{
    /// <summary>
    /// Survives the destruction of its entity so the view can be recycled afterwards.
    /// </summary>
    /// <remarks>
    /// When an entity is destroyed, Entities strips every component except its
    /// <see cref="ICleanupComponentData"/> and leaves the entity itself alive until those are
    /// removed too. That is the only reliable way to notice a destruction from a system —
    /// polling for "entities that used to exist" is not a query you can write. So the spawn
    /// system adds this next to <see cref="EntityViewLink"/>, and
    /// <see cref="EntityViewDespawnSystem"/> matches on
    /// <c>WithAll&lt;EntityViewLinkCleanup&gt;().WithNone&lt;EntityViewLink&gt;()</c> — a shape
    /// only a destroyed entity can have.
    /// </remarks>
    public struct EntityViewLinkCleanup : ICleanupComponentData
    {
        /// <summary>Copy of <see cref="EntityViewLink.ViewId"/>, still readable after destruction.</summary>
        public int ViewId;
    }
}
