using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Cuvara.DOTS.Groups
{
    /// <summary>
    /// The package's own end-of-gameplay command buffer. Every structural change the package makes
    /// from a gameplay system targets this, never Unity's
    /// <c>EndSimulationEntityCommandBufferSystem</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why not Unity's.</b> Unity's end-of-simulation buffer plays back after
    /// <c>TransformSystemGroup</c>, at the very end of <see cref="SimulationSystemGroup"/>. This one
    /// plays back at the end of <see cref="GameplaySystemGroup"/>, which sits <i>before</i> the
    /// transform systems — so an entity destroyed this frame is gone before transforms are computed
    /// and long before <see cref="ViewSystemGroup"/> runs, and no view is ever synced against a dead
    /// entity. Targeting Unity's buffer would leave exactly a one-group window where it could be.
    /// </para>
    /// <para>
    /// Owning the playback point is also what keeps the package's ordering self-contained: a
    /// consumer reordering their own use of Unity's buffer cannot move when the package's structural
    /// changes land.
    /// </para>
    /// <para>
    /// The <see cref="Singleton"/> plumbing below is the boilerplate Entities requires of a custom
    /// <see cref="EntityCommandBufferSystem"/> — it is what lets a Bursted <c>ISystem</c> obtain a
    /// buffer through <c>SystemAPI.GetSingleton&lt;Singleton&gt;()</c> without touching a managed
    /// system reference.
    /// </para>
    /// </remarks>
    [DisableAutoCreation]
    [UpdateInGroup(typeof(GameplaySystemGroup), OrderLast = true)]
    public partial class DotsEndSimulationCommandBufferSystem : EntityCommandBufferSystem
    {
        /// <summary>
        /// Unmanaged handle onto this system's pending buffers, resolvable from a job-side system.
        /// </summary>
        public unsafe struct Singleton : IComponentData, IECBSingleton
        {
            internal UnsafeList<EntityCommandBuffer>* PendingBuffers;
            internal AllocatorManager.AllocatorHandle Allocator;

            public EntityCommandBuffer CreateCommandBuffer(WorldUnmanaged world)
            {
                return EntityCommandBufferSystem.CreateCommandBuffer(ref *PendingBuffers, Allocator, world);
            }

            public void SetPendingBufferList(ref UnsafeList<EntityCommandBuffer> buffers)
            {
                PendingBuffers = (UnsafeList<EntityCommandBuffer>*)UnsafeUtility.AddressOf(ref buffers);
            }

            public void SetAllocator(Allocator allocatorIn) => Allocator = allocatorIn;

            public void SetAllocator(AllocatorManager.AllocatorHandle allocatorIn) => Allocator = allocatorIn;
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            this.RegisterSingleton<Singleton>(ref PendingBuffers, World.Unmanaged);
        }
    }
}
