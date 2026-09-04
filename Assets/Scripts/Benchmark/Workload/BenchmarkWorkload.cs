#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER
namespace Scripts.Benchmark.Workload
{
    using System.Collections.Generic;
    using Cuvara.DOTS.Provisioning;
    using Cuvara.DOTS.Simulation;
    using Cuvara.DOTS.Views;
    using Scripts.Benchmark;
    using Scripts.DI.Dots;
    using Unity.Collections;
    using Unity.Entities;
    using Unity.Mathematics;
    using Unity.Transforms;
    using UnityEngine;
    using VContainer;

    /// <summary>
    /// The benchmark's entity workload: keeps the recorder's per-phase entity count alive as
    /// moving, spinning, view-backed entities — the HybridViews sample's spawning pattern
    /// (LocalTransform + LocalToWorld + <see cref="EntityViewRequest"/>) over the game's own
    /// container-provided view layer, driven by the dots package's Burst-compiled local
    /// simulation (<see cref="MoveData"/> bounce + <see cref="SpinSpeed"/>). No netcode, no
    /// server: the point is to price simulation + view sync + rendering alone.
    /// </summary>
    /// <remarks>
    /// <para>Entities split half "mob" (spheres) / half "player-remote" (capsules) so both
    /// pooled view kinds and two view scales are exercised, in a ±24 unit box — roughly the
    /// area an AOI radius of 50 would keep visible, so counts here translate to counts in
    /// game terms.</para>
    /// <para>Deterministic: positions and velocities come from a fixed-seed
    /// <see cref="Unity.Mathematics.Random"/>, so two runs on two devices measure the same
    /// workload.</para>
    /// <para>Spawning allocates (the request key resolves to a managed pool key, and the
    /// spawn list grows) — that is why it happens only at phase boundaries, which the
    /// recorder's settle window excludes from phase aggregates.</para>
    /// </remarks>
    public sealed class BenchmarkWorkload : MonoBehaviour
    {
        private const uint RandomSeed = 0x9E3779B9u;

        [Tooltip("The recorder whose ramp this workload follows.")]
        [SerializeField] private BenchmarkRecorder recorder;

        [Tooltip("Half-size of the box the entities bounce in.")]
        [SerializeField] private float boundsExtent = 24f;

        private IViewAssetProvider viewAssetProvider;
        private World world;
        private Unity.Mathematics.Random random = new Unity.Mathematics.Random(RandomSeed);
        private readonly List<Entity> spawned = new List<Entity>();
        private bool installed;

        [Inject]
        public void Construct(IViewAssetProvider provider)
        {
            this.viewAssetProvider = provider;
        }

        private void Start()
        {
            if (this.recorder == null)
            {
                Debug.LogError("[BenchmarkWorkload] no recorder assigned — workload disabled.");
                this.enabled = false;
                return;
            }

            this.world = World.DefaultGameObjectInjectionWorld;
            if (this.world == null)
            {
                Debug.LogError("[BenchmarkWorkload] no default ECS world — workload disabled.");
                this.enabled = false;
                return;
            }

            if (this.viewAssetProvider == null)
            {
                // No container built (defines off, or the scope is missing from the scene).
                // The sim still runs; entities would just be invisible — better to say so.
                Debug.LogWarning("[BenchmarkWorkload] no IViewAssetProvider injected — entities will have no views.");
            }

            // Same call DotsWorldBridge makes; idempotent, and the view bootstrap itself was
            // already installed by RegisterDots at container build.
            DotsSimulationBootstrap.InstallSimulationSystems(this.world);

            // Prewarm to the ramp's peak up front: pool growth mid-run would be measured as
            // workload cost. Synchronous by construction with the primitive provider.
            var peak = 0;
            foreach (var phase in this.recorder.Phases)
            {
                peak = math.max(peak, phase.EntityCount);
            }

            var perKind = (peak + 1) / 2;
            this.viewAssetProvider?.PrewarmAsync(DotsViewArchetypes.Mob, perKind).GetAwaiter().GetResult();
            this.viewAssetProvider?.PrewarmAsync(DotsViewArchetypes.PlayerRemote, perKind).GetAwaiter().GetResult();

            this.recorder.PhaseStarted += this.OnPhaseStarted;
            this.installed = true;
        }

        private void OnDestroy()
        {
            if (this.recorder != null)
            {
                this.recorder.PhaseStarted -= this.OnPhaseStarted;
            }

            if (!this.installed || this.world is not { IsCreated: true })
            {
                return;
            }

            var entityManager = this.world.EntityManager;
            foreach (var entity in this.spawned)
            {
                if (entityManager.Exists(entity))
                {
                    entityManager.DestroyEntity(entity);
                }
            }

            this.spawned.Clear();
        }

        private void OnPhaseStarted(int index, BenchmarkPhase phase)
        {
            this.EnsureEntityCount(phase.EntityCount);
        }

        /// <summary>Spawns up to <paramref name="target"/>; the ramp only ever grows.</summary>
        private void EnsureEntityCount(int target)
        {
            var entityManager = this.world.EntityManager;
            while (this.spawned.Count < target)
            {
                this.spawned.Add(this.SpawnOne(entityManager, this.spawned.Count));
            }

            Debug.Log($"[BenchmarkWorkload] entities alive: {this.spawned.Count}");
        }

        private Entity SpawnOne(EntityManager entityManager, int index)
        {
            var extent = this.boundsExtent;
            var position = new float3(
                this.random.NextFloat(-extent, extent),
                this.random.NextFloat(0.5f, 6f),
                this.random.NextFloat(-extent, extent));
            var velocity = this.random.NextFloat3Direction() * this.random.NextFloat(2f, 6f);

            var entity = entityManager.CreateEntity();
            entityManager.AddComponentData(entity, LocalTransform.FromPosition(position));

            // LocalToWorld explicitly, as the HybridViews sample documents: baking would add
            // it, code-created entities do not get it, and the view sync system reads it.
            entityManager.AddComponentData(entity, new LocalToWorld
            {
                Value = float4x4.TRS(position, quaternion.identity, new float3(1f)),
            });

            entityManager.AddComponentData(entity, new MoveData
            {
                Velocity = velocity,
                BoundsMin = new float3(-extent, 0.5f, -extent),
                BoundsMax = new float3(extent, 6f, extent),
            });

            entityManager.AddComponentData(entity, new SpinSpeed
            {
                RadiansPerSecond = this.random.NextFloat(0.5f, 3f),
            });

            var key = (index & 1) == 0 ? DotsViewArchetypes.Mob : DotsViewArchetypes.PlayerRemote;
            entityManager.AddComponentData(entity, new EntityViewRequest
            {
                ViewKey = new FixedString64Bytes(key),
            });

            return entity;
        }
    }
}
#endif
