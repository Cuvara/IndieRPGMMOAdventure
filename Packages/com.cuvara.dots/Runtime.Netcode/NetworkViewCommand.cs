using Unity.Collections;

namespace Cuvara.DOTS.Netcode
{
    /// <summary>What kind of change a <see cref="NetworkViewCommand"/> describes.</summary>
    internal enum NetworkViewCommandKind : byte
    {
        Spawn = 0,
        State = 1,
        Despawn = 2,
    }

    /// <summary>
    /// One queued <c>IEntityView</c> call, in a form the drain system can apply without touching a
    /// managed object.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One struct for all three kinds rather than three types plus a discriminator: the queue is a
    /// FIFO and the order between kinds is the whole guarantee (spawn before its first state,
    /// despawn after the last), so they have to share a lane. Three queues would need a sequence
    /// number to merge them back, which is this field by another name.
    /// </para>
    /// <para>
    /// Unused fields per kind are the cost — a despawn carries four dead floats. At 15 Hz over an
    /// AOI that is a few kilobytes a second of queue traffic, and the alternative costs an
    /// allocation per command.
    /// </para>
    /// </remarks>
    internal struct NetworkViewCommand
    {
        public NetworkViewCommandKind Kind;

        public FixedString64Bytes Id;

        /// <summary>Server entity kind, for <c>NetworkEntity.Type</c>. Spawn only.</summary>
        public FixedString32Bytes Type;

        public bool IsLocal;

        /// <summary>Config table index resolved at enqueue time, or -1 when the entity has no config.</summary>
        public int ConfigIndex;

        /// <summary>View key resolved at enqueue time. Empty when unconfigured.</summary>
        public FixedString64Bytes ViewKey;

        public float X;

        public float Y;

        public int Hp;

        public int MaxHp;
    }
}
