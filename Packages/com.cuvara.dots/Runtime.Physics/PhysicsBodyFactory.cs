using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace Cuvara.DOTS.Physics
{
    /// <summary>
    /// Factory methods for adding Unity.Physics components to entities. Keeps the
    /// boilerplate out of game code and ensures consistent collider construction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>No gameplay rules live here.</b> This creates colliders and mass; what happens
    /// when two entities collide is decided by the game via <see cref="EntityCollision"/>
    /// events, not by this factory.
    /// </para>
    /// <para>
    /// All colliders are created with <c>CollisionFilter.Default</c>. The game overrides
    /// the filter after creation if it needs layers.
    /// </para>
    /// </remarks>
    public static class PhysicsBodyFactory
    {
        /// <summary>
        /// Adds a dynamic physics body (moves, collides, responds to forces).
        /// </summary>
        public static void AddDynamicBody(
            EntityManager em,
            Entity entity,
            ColliderShape shape,
            float3 size,
            float mass = 1f)
        {
            var collider = CreateCollider(shape, size);

            em.AddComponentData(entity, new PhysicsCollider { Value = collider });
            em.AddComponentData(entity, new PhysicsVelocity
            {
                Linear = float3.zero,
                Angular = float3.zero,
            });
            em.AddComponentData(entity, PhysicsMass.CreateDynamic(
                collider.Value.MassProperties, mass));

            EnsureTransform(em, entity);
        }

        /// <summary>
        /// Adds a static physics body (collides, does not move).
        /// Walls, terrain, obstacles.
        /// </summary>
        public static void AddStaticBody(
            EntityManager em,
            Entity entity,
            ColliderShape shape,
            float3 size)
        {
            var collider = CreateCollider(shape, size);
            em.AddComponentData(entity, new PhysicsCollider { Value = collider });

            EnsureTransform(em, entity);
        }

        /// <summary>
        /// Adds a kinematic physics body (moves via code, collides, ignores forces).
        /// Server-authoritative entities that the client renders but does not simulate.
        /// </summary>
        public static void AddKinematicBody(
            EntityManager em,
            Entity entity,
            ColliderShape shape,
            float3 size)
        {
            var collider = CreateCollider(shape, size);

            em.AddComponentData(entity, new PhysicsCollider { Value = collider });
            em.AddComponentData(entity, new PhysicsVelocity());
            em.AddComponentData(entity, PhysicsMass.CreateKinematic(
                collider.Value.MassProperties));

            EnsureTransform(em, entity);
        }

        /// <summary>
        /// Creates a collider blob asset from a shape enum and size.
        /// </summary>
        public static BlobAssetReference<Collider> CreateCollider(
            ColliderShape shape,
            float3 size,
            CollisionFilter? filter = null)
        {
            var f = filter ?? CollisionFilter.Default;

            switch (shape)
            {
                case ColliderShape.Sphere:
                    return Unity.Physics.SphereCollider.Create(
                        new SphereGeometry { Center = float3.zero, Radius = size.x },
                        f);

                case ColliderShape.Box:
                    return Unity.Physics.BoxCollider.Create(
                        new BoxGeometry
                        {
                            Center = float3.zero,
                            Orientation = quaternion.identity,
                            Size = size,
                            BevelRadius = 0.05f,
                        },
                        f);

                case ColliderShape.Capsule:
                    var halfHeight = size.y * 0.5f;
                    return Unity.Physics.CapsuleCollider.Create(
                        new CapsuleGeometry
                        {
                            Vertex0 = new float3(0, -halfHeight + size.x, 0),
                            Vertex1 = new float3(0, halfHeight - size.x, 0),
                            Radius = size.x,
                        },
                        f);

                case ColliderShape.Cylinder:
                    return Unity.Physics.CylinderCollider.Create(
                        new CylinderGeometry
                        {
                            Center = float3.zero,
                            Orientation = quaternion.identity,
                            Height = size.y,
                            Radius = size.x,
                            BevelRadius = 0.05f,
                            SideCount = 12,
                        },
                        f);

                default:
                    return Unity.Physics.SphereCollider.Create(
                        new SphereGeometry { Center = float3.zero, Radius = size.x },
                        f);
            }
        }

        private static void EnsureTransform(EntityManager em, Entity entity)
        {
            if (!em.HasComponent<LocalTransform>(entity))
                em.AddComponentData(entity, LocalTransform.Identity);
            if (!em.HasComponent<LocalToWorld>(entity))
                em.AddComponentData(entity, new LocalToWorld { Value = float4x4.identity });
        }
    }
}
