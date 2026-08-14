using Cuvara.DOTS.Groups;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

namespace Cuvara.DOTS.Views
{
    /// <summary>
    /// Copies <see cref="LocalTransform"/> onto the linked GameObject's <c>Transform</c> once per
    /// frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two halves, split at the managed boundary. <see cref="CollectViewTransformsJob"/> is an
    /// <c>IJobEntity</c> and Bursted: it walks the chunks and writes blittable
    /// <see cref="ViewTransformSample"/> values into a <c>NativeList</c>. The second half is a flat
    /// main-thread loop over that list calling into <see cref="EntityViewRegistry"/>, because
    /// <c>UnityEngine.Transform</c> can only be touched from the main thread and cannot be Bursted
    /// at all.
    /// </para>
    /// <para>
    /// <b>No performance claim is made here.</b> This has not been profiled — no Unity Editor was
    /// available when it was written. The split is a structural choice (keep the managed part as
    /// small and as flat as possible), not a measured win, and it is entirely possible that at low
    /// view counts the job's scheduling overhead costs more than it saves. Profile before quoting
    /// a number.
    /// </para>
    /// <para>
    /// The <c>Complete()</c> before the drain is unavoidable: the managed loop reads what the job
    /// wrote. That is a sync point every frame.
    /// </para>
    /// </remarks>
    // Sole member of the sync group, which runs after the lifecycle group — so this frame's new
    // views are positioned this frame and no view it touches is about to be recycled.
    [DisableAutoCreation]
    [UpdateInGroup(typeof(ViewTransformSyncGroup))]
    internal partial struct EntityViewTransformSyncSystem : ISystem
    {
        private EntityQuery _linked;

        public void OnCreate(ref SystemState state)
        {
            _linked = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<EntityViewLink, LocalToWorld>()
                .Build(ref state);

            state.RequireForUpdate(_linked);
            state.RequireForUpdate<EntityViewRegistryReference>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var registry = SystemAPI.ManagedAPI.GetSingleton<EntityViewRegistryReference>().Registry;
            if (registry == null) return;

            var count = _linked.CalculateEntityCount();
            if (count == 0) return;

            // Exact capacity + AddNoResize: a ParallelWriter cannot grow the list, so the capacity
            // has to be right before the job starts.
            var samples = new NativeList<ViewTransformSample>(count, state.WorldUpdateAllocator);

            state.Dependency = new CollectViewTransformsJob
            {
                Samples = samples.AsParallelWriter(),
            }.ScheduleParallel(_linked, state.Dependency);

            state.Dependency.Complete();

            for (var i = 0; i < samples.Length; i++) registry.ApplyTransform(samples[i]);
        }
    }
}
