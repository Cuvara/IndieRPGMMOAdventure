using Unity.Entities;

namespace Cuvara.DOTS.Netcode
{
    /// <summary>
    /// The newest authoritative hit points for a replicated entity, as the server reported them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Separate from <see cref="Cuvara.DOTS.Simulation.Health"/>, and that is the whole point.</b>
    /// In this package <c>Health</c> means "destroy at zero" — <c>HealthDeathSystem</c> acts on it.
    /// Mirroring server hp straight into <c>Health</c> therefore hands entity lifetime to a
    /// client-side system while the server is still listing the entity: a snapshot carrying 0 hp for
    /// an entity in its death animation would destroy the mirror, and the next snapshot would spawn
    /// a fresh one. The reference implementation does exactly this and gets away with it only
    /// because its enemies are despawned server-side in the same tick.
    /// </para>
    /// <para>
    /// So the wire value lands here, unconditionally and with no behaviour attached, and writing
    /// <c>Health</c> as well is opt-in on <see cref="DotsEntityView"/> for consumers that want the
    /// death system to act on server hp and have accepted what that means.
    /// </para>
    /// <para>
    /// Position is deliberately not duplicated here: it goes straight to <c>LocalTransform</c>,
    /// where every transform system and the view sync already read it.
    /// </para>
    /// </remarks>
    public struct NetworkEntityState : IComponentData
    {
        public int Hp;

        public int MaxHp;
    }
}
