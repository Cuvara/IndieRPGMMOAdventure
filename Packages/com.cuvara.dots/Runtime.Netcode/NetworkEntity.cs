using Unity.Collections;
using Unity.Entities;

namespace Cuvara.DOTS.Netcode
{
    /// <summary>
    /// Marks an entity as the local mirror of a replicated id, and carries that id back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The id is stored on the entity as well as in the adapter's map because the map is one-way and
    /// private: a gameplay system that has an <see cref="Entity"/> and needs to name it on the wire
    /// — an attack target, a chat mention, a nameplate — would otherwise have to scan the map from
    /// the main thread. A <see cref="FixedString64Bytes"/> keeps that readable from a job.
    /// </para>
    /// <para>
    /// Nakama user ids are 36-byte UUIDs and the sample's <c>"enemy-"</c> ids are shorter still, so
    /// 61 bytes is not a live constraint — but it is a real one, and
    /// <see cref="DotsEntityView"/> refuses to spawn an id that does not fit rather than truncating
    /// it into something that would silently mis-target.
    /// </para>
    /// </remarks>
    public struct NetworkEntity : IComponentData
    {
        /// <summary>The replicated id, exactly as it arrived on the wire.</summary>
        public FixedString64Bytes Id;

        /// <summary>
        /// The server's entity kind — <c>"player"</c>, <c>"mob"</c>, and so on. Empty when the
        /// server sent none.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Carried so a system can filter by kind — "damage every mob in range" — without a managed
        /// lookup. It is what the reference implementation's <c>EnemyTag</c> was for, expressed as
        /// data rather than as a tag per kind, because the package cannot know the kinds and a tag
        /// it cannot name is not a tag it can add.
        /// </para>
        /// <para>
        /// <b>A shorter string than <see cref="Id"/>, and truncating.</b> 29 usable bytes holds every
        /// kind the schema names with room to spare, and a kind long enough to truncate is one this
        /// build does not understand anyway. The truncation is confined to this convenience field:
        /// archetype resolution runs on the managed string before the command is queued, so a long
        /// kind still resolves to the right archetype and only reads back clipped here.
        /// </para>
        /// </remarks>
        public FixedString32Bytes Type;

        /// <summary>True for the entity whose id equals the local player's.</summary>
        public bool IsLocal;
    }
}
