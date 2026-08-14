using Unity.Mathematics;

namespace Cuvara.DOTS.Simulation
{
    /// <summary>
    /// The entity state the simulation seam operates on: everything the shared rules read, and
    /// nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately <b>not</b> <c>Shared.GameLogic.Components.EntityState</c>. That type carries
    /// <c>string Id</c> and <c>string Type</c>, which makes it managed and therefore unusable in a
    /// Bursted job or an <see cref="Unity.Entities.IComponentData"/>. Identity stays on the ECS side
    /// as a <c>FixedString64Bytes</c>; this struct is pure simulation input.
    /// </para>
    /// <para>
    /// Owned by <c>com.cuvara.dots</c>, not by the shared library — the package owns the
    /// abstraction and its value types, and <c>Shared.GameLogic</c> is one implementation behind it.
    /// Consumer code compiles identically whether or not that git dependency is installed.
    /// </para>
    /// </remarks>
    public struct SimEntity
    {
        /// <summary>World position on the server's 2D plane.</summary>
        public float2 Position;

        /// <summary>Movement speed in world units per second. Non-positive means immobile.</summary>
        public float Speed;

        public int Hp;
        public int MaxHp;
        public int Attack;
        public int Defense;

        /// <summary>Dead entities are blocked from moving.</summary>
        public bool Dead;

        /// <summary>
        /// Simulation tick at which the attack cooldown expires. A simulation tick, never a
        /// wall-clock value — the rules have to be replayable for prediction rewind.
        /// </summary>
        public ulong CooldownUntilTick;
    }
}
