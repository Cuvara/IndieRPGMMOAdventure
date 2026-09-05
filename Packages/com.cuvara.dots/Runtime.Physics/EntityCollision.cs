using Unity.Mathematics;

namespace Cuvara.DOTS.Physics
{
    /// <summary>
    /// Published when two physics entities collide. The game decides what to do with it —
    /// damage, knockback, sound — this package only reports it.
    /// </summary>
    public readonly struct EntityCollision
    {
        public readonly int EntityIndexA;
        public readonly int EntityIndexB;
        public readonly float3 Normal;
        public readonly float3 Position;
        public readonly float Impulse;

        public EntityCollision(int entityIndexA, int entityIndexB, float3 normal, float3 position, float impulse)
        {
            EntityIndexA = entityIndexA;
            EntityIndexB = entityIndexB;
            Normal = normal;
            Position = position;
            Impulse = impulse;
        }
    }
}
