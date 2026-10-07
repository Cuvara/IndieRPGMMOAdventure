#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER && CUVARA_NETCODE && CUVARA_SHARED_GAMELOGIC
namespace Scripts.DI.Dots
{
    using System.Collections.Generic;
    using Cuvara.DOTS.Groups;
    using Cuvara.DOTS.Netcode;
    using Unity.Collections;
    using Unity.Entities;
    using Unity.Mathematics;
    using Unity.Transforms;

    /// <summary>What to do with one mirror's transform this frame. Wire axes; see <c>WireAxes</c>.</summary>
    public struct EntityElevation
    {
        /// <summary>Wire <c>z</c>: becomes Unity <c>y</c>.</summary>
        public float Height;

        /// <summary>When true, <see cref="X"/>/<see cref="Y"/> replace the adapter's ground-plane position too.</summary>
        public bool OverridesPlane;

        /// <summary>Wire <c>x</c> (Unity <c>x</c>), used only with <see cref="OverridesPlane"/>.</summary>
        public float X;

        /// <summary>Wire <c>y</c> (Unity <c>z</c>), used only with <see cref="OverridesPlane"/>.</summary>
        public float Y;
    }

    /// <summary>
    /// Per-entity elevation for this frame, keyed by the mirror's wire id. Written on the main
    /// thread by <see cref="EntityElevationFeeder"/> and read by <see cref="EntityElevationSystem"/>
    /// on the same thread, in the same frame.
    /// </summary>
    public sealed class EntityElevationTable
    {
        private readonly Dictionary<FixedString64Bytes, EntityElevation> entries =
            new Dictionary<FixedString64Bytes, EntityElevation>();

        public int Count => this.entries.Count;

        public void Clear() => this.entries.Clear();

        public void Set(string id, in EntityElevation elevation)
        {
            if (string.IsNullOrEmpty(id) || id.Length > FixedString64Bytes.UTF8MaxLengthInBytes) return;
            this.entries[new FixedString64Bytes(id)] = elevation;
        }

        public bool TryGet(in FixedString64Bytes id, out EntityElevation elevation) => this.entries.TryGetValue(id, out elevation);
    }

    /// <summary>
    /// Puts protocol 3 height (ADR-28) onto the DOTS mirrors: wire <c>z</c> becomes Unity
    /// <c>y</c>, and a projectile's whole position is replaced by its extrapolated one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a client system.</b> <c>com.cuvara.dots</c> 0.29.0 maps the server's plane only
    /// (<c>SnapshotSpaceMapping.ToWorld(x, y)</c>): its remote interpolation and its prediction
    /// system both write <c>LocalTransform.Position</c> with Unity <c>y = 0</c> every frame. This
    /// system runs after both (<see cref="ViewInterpolationGroup"/> in Presentation; prediction is
    /// in Initialization) and before the view lifecycle and transform sync, and sets the height
    /// absolutely — never adds — so it is idempotent on frames where no other writer ran.
    /// Remove it once the adapter carries z itself.
    /// </para>
    /// <para>
    /// Writes <c>LocalToWorld</c> as well as <c>LocalTransform</c> for the reason the package's
    /// own writers do: the view sync reads <c>LocalToWorld</c>, and the transform system that
    /// would derive it already ran this frame (Simulation precedes Presentation).
    /// </para>
    /// </remarks>
    [DisableAutoCreation]
    [UpdateInGroup(typeof(ViewSystemGroup))]
    [UpdateAfter(typeof(ViewInterpolationGroup))]
    [UpdateBefore(typeof(ViewLifecycleGroup))]
    public partial class EntityElevationSystem : SystemBase
    {
        /// <summary>The table to read; null makes the system a no-op.</summary>
        public EntityElevationTable Table { get; set; }

        /// <summary>Mirrors whose transform this system wrote last update. Diagnostics and tests.</summary>
        public int LastApplied { get; private set; }

        protected override void OnUpdate()
        {
            var table = this.Table;
            if (table == null || table.Count == 0)
            {
                this.LastApplied = 0;
                return;
            }

            // The remote interpolation is a scheduled job writing the same components.
            this.CompleteDependency();

            var applied = 0;
            foreach (var (network, transform, toWorld) in
                     SystemAPI.Query<RefRO<NetworkEntity>, RefRW<LocalTransform>, RefRW<LocalToWorld>>())
            {
                if (!table.TryGet(network.ValueRO.Id, out var elevation)) continue;

                var position = transform.ValueRO.Position;
                if (elevation.OverridesPlane)
                {
                    position.x = elevation.X;
                    position.z = elevation.Y;
                }

                position.y = elevation.Height;

                var scale = transform.ValueRO.Scale;
                transform.ValueRW.Position = position;
                toWorld.ValueRW.Value = float4x4.TRS(position, transform.ValueRO.Rotation, new float3(scale, scale, scale));
                applied++;
            }

            this.LastApplied = applied;
        }
    }

    /// <summary>Installs <see cref="EntityElevationSystem"/> into the view group, the way the package bootstraps do.</summary>
    public static class EntityElevationBootstrap
    {
        /// <summary>
        /// Adds the system to <see cref="ViewSystemGroup"/> (created by the view bootstrap at root
        /// build) and points it at <paramref name="table"/>. Idempotent. Returns null when the
        /// world has no view group — DOTS presentation is not installed, so there is nothing to lift.
        /// </summary>
        public static EntityElevationSystem Install(World world, EntityElevationTable table)
        {
            if (world == null || !world.IsCreated) return null;

            var group = world.GetExistingSystemManaged<ViewSystemGroup>();
            if (group == null) return null;

            var system = world.GetOrCreateSystemManaged<EntityElevationSystem>();
            system.Table = table;
            group.AddSystemToUpdateList(system);
            group.SortSystems();
            return system;
        }

        public static void Uninstall(World world)
        {
            if (world == null || !world.IsCreated) return;

            var system = world.GetExistingSystemManaged<EntityElevationSystem>();
            if (system == null) return;

            world.GetExistingSystemManaged<ViewSystemGroup>()?.RemoveSystemFromUpdateList(system);
            world.DestroySystemManaged(system);
        }
    }
}
#endif
