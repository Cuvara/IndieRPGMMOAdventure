#if CUVARA_DOTS
namespace Scripts.Benchmark.Dots
{
    using System;
    using System.Diagnostics;
    using Cuvara.DOTS.Simulation;
    using Cuvara.DOTS.Views;
    using Unity.Collections;
    using Unity.Entities;
    using Unity.Mathematics;
    using Unity.Transforms;
    using UnityEngine;
    using Debug = UnityEngine.Debug;
#if UNITY_PHYSICS
    using Unity.Physics;
    using SphereCollider = Unity.Physics.SphereCollider;
#endif

    /// <summary>
    /// DOTS stress benchmark supporting both pure-ECS and hybrid (GameObjects) modes.
    /// Ramps through configurable entity tiers with optional Unity.Physics bodies.
    /// </summary>
    public sealed class DotsStressBenchmark : MonoBehaviour
    {
        private static readonly int[] DefaultTiers = { 100, 1_000, 10_000, 1_000_000, 10_000_000, 100_000_000 };
        private static readonly int[] QuickTiers = { 100, 1_000, 10_000, 1_000_000 };

        [SerializeField] private float secondsPerTier = 10f;
        [SerializeField] private float warmupSeconds = 3f;
        [SerializeField] private float boundsExtent = 200f;
        [SerializeField] private bool includePhysics = true;
        [SerializeField] private bool hybridMode;
        [SerializeField] private int maxViewEntities = 50_000;
        [SerializeField] private bool quickMode;

        private World world;
        private int[] tiers;
        private int currentTier;
        private float warmupRemaining;
        private float tierElapsed;
        private bool measuring;
        private int simCount;
        private int physCount;

        // Metrics — preallocated, zero GC during measurement.
        private int frameCount;
        private double frameTimeSum;
        private double frameTimeMax;
        private double frameTimeMin;
        private float[] frameSamples;
        private int sampleCount;
        private readonly Stopwatch tierStopwatch = new Stopwatch();

        private NativeList<Entity> allEntities;
        private Unity.Mathematics.Random random;

        // Hybrid view layer.
        private HybridViewPool viewPool;

        public void SetPlan(float tierSec, float warmup, bool physics,
            bool hybrid, int viewCap, bool quick)
        {
            this.secondsPerTier = tierSec;
            this.warmupSeconds = warmup;
            this.includePhysics = physics;
            this.hybridMode = hybrid;
            this.maxViewEntities = viewCap;
            this.quickMode = quick;
        }

        private void Start()
        {
            this.world = World.DefaultGameObjectInjectionWorld;
            if (this.world == null)
            {
                Debug.LogError("[STRESS-BENCH] No default ECS world.");
                this.enabled = false;
                return;
            }

            DotsSimulationBootstrap.InstallSimulationSystems(this.world);

            this.tiers = this.quickMode ? QuickTiers : DefaultTiers;
            this.allEntities = new NativeList<Entity>(Allocator.Persistent);
            this.random = new Unity.Mathematics.Random(0xDEADBEEFu);
            this.frameSamples = new float[65536];

            if (this.hybridMode)
            {
                this.viewPool = new HybridViewPool();
                this.viewPool.Prewarm("cube", this.maxViewEntities / 2);
                this.viewPool.Prewarm("sphere", this.maxViewEntities / 2);
                var viewRegistry = new EntityViewRegistry(this.viewPool);
                DotsViewBootstrap.Install(this.world, viewRegistry);
            }

            var mode = this.hybridMode ? "Hybrid" : "Pure DOTS";
            Debug.Log($"[STRESS-BENCH] {mode}. Tiers: {this.tiers.Length}. " +
                      $"Physics: {this.includePhysics}. Quick: {this.quickMode}.");

            this.SpawnTier(0);
        }

        private void OnDestroy()
        {
            this.viewPool?.Clear();
            if (this.allEntities.IsCreated)
            {
                if (this.world is { IsCreated: true })
                {
                    var em = this.world.EntityManager;
                    for (int i = 0; i < this.allEntities.Length; i++)
                        if (em.Exists(this.allEntities[i]))
                            em.DestroyEntity(this.allEntities[i]);
                }
                this.allEntities.Dispose();
            }
        }

        private void Update()
        {
            if (this.currentTier >= this.tiers.Length) return;
            var dt = Time.unscaledDeltaTime;

            if (this.warmupRemaining > 0f)
            {
                this.warmupRemaining -= dt;
                if (this.warmupRemaining <= 0f)
                    this.StartMeasuring();
                return;
            }

            if (!this.measuring) return;

            this.tierElapsed += dt;
            this.RecordFrame(dt * 1000f);

            if (this.tierElapsed >= this.secondsPerTier)
            {
                this.FinishTier();
                this.currentTier++;
                if (this.currentTier < this.tiers.Length)
                    this.SpawnTier(this.currentTier);
                else
                    this.AllDone();
            }
        }

        private void SpawnTier(int index)
        {
            var target = this.tiers[index];

            // Memory guard: estimate ~128 bytes/entity. Skip if > 60% RAM.
            var estimatedMb = (long)target * 128 / (1024 * 1024);
            if (estimatedMb > SystemInfo.systemMemorySize * 0.6f)
            {
                Debug.LogWarning($"[STRESS-BENCH] SKIP tier {FormatCount(target)} " +
                                 $"(~{estimatedMb:N0} MB > 60% of {SystemInfo.systemMemorySize} MB RAM).");
                this.currentTier++;
                if (this.currentTier < this.tiers.Length)
                    this.SpawnTier(this.currentTier);
                else
                    this.AllDone();
                return;
            }

            this.DestroyAll();
            var sw = Stopwatch.StartNew();

            var em = this.world.EntityManager;
            var extent = this.boundsExtent;
            var physTarget = this.includePhysics ? (int)(target * 0.3f) : 0;
            var simTarget = target - physTarget;

            var simArch = em.CreateArchetype(
                ComponentType.ReadWrite<LocalTransform>(),
                ComponentType.ReadWrite<LocalToWorld>(),
                ComponentType.ReadWrite<MoveData>(),
                ComponentType.ReadWrite<SpinSpeed>(),
                ComponentType.ReadWrite<Health>(),
                ComponentType.ReadWrite<TimeToLive>());

            this.SpawnBatch(em, simArch, simTarget, extent, false);

#if UNITY_PHYSICS
            if (physTarget > 0)
            {
                var physArch = em.CreateArchetype(
                    ComponentType.ReadWrite<LocalTransform>(),
                    ComponentType.ReadWrite<LocalToWorld>(),
                    ComponentType.ReadWrite<MoveData>(),
                    ComponentType.ReadWrite<SpinSpeed>(),
                    ComponentType.ReadWrite<Health>(),
                    ComponentType.ReadWrite<TimeToLive>(),
                    ComponentType.ReadWrite<PhysicsVelocity>(),
                    ComponentType.ReadWrite<PhysicsMass>(),
                    ComponentType.ReadWrite<PhysicsCollider>());

                this.SpawnBatch(em, physArch, physTarget, extent, true);
            }
#endif

            this.simCount = simTarget;
            this.physCount = physTarget;
            sw.Stop();

            // Hybrid: attach views to a subset.
            if (this.hybridMode)
            {
                var viewCount = math.min(this.allEntities.Length, this.maxViewEntities);
                for (int i = 0; i < viewCount; i++)
                {
                    var key = (i & 1) == 0 ? "cube" : "sphere";
                    em.AddComponentData(this.allEntities[i], new EntityViewRequest
                    {
                        ViewKey = new FixedString64Bytes(key),
                    });
                }
            }

            Debug.Log($"[STRESS-BENCH] Spawned {FormatCount(target)}: " +
                      $"sim={this.simCount:N0} phys={this.physCount:N0} " +
                      $"spawn={sw.ElapsedMilliseconds}ms");

            this.warmupRemaining = this.warmupSeconds;
            this.measuring = false;
        }

        private void SpawnBatch(EntityManager em, EntityArchetype arch,
            int count, float extent, bool isPhysics)
        {
            const int batchSize = 65536;
            var remaining = count;
            while (remaining > 0)
            {
                var n = math.min(remaining, batchSize);
                using (var batch = new NativeArray<Entity>(n, Allocator.Temp))
                {
                    em.CreateEntity(arch, batch);
                    for (int i = 0; i < batch.Length; i++)
                    {
                        this.InitEntity(em, batch[i], extent);
#if UNITY_PHYSICS
                        if (isPhysics)
                            this.InitPhysics(em, batch[i]);
#endif
                        this.allEntities.Add(batch[i]);
                    }
                }
                remaining -= n;
            }
        }

        private void InitEntity(EntityManager em, Entity e, float extent)
        {
            var pos = new float3(
                this.random.NextFloat(-extent, extent),
                this.random.NextFloat(0.5f, 10f),
                this.random.NextFloat(-extent, extent));
            em.SetComponentData(e, LocalTransform.FromPosition(pos));
            em.SetComponentData(e, new LocalToWorld
            {
                Value = float4x4.TRS(pos, quaternion.identity, new float3(1f)),
            });
            em.SetComponentData(e, new MoveData
            {
                Velocity = this.random.NextFloat3Direction() * this.random.NextFloat(2f, 8f),
                BoundsMin = new float3(-extent, 0.5f, -extent),
                BoundsMax = new float3(extent, 10f, extent),
            });
            em.SetComponentData(e, new SpinSpeed
            {
                RadiansPerSecond = this.random.NextFloat(0.5f, 4f),
            });
            em.SetComponentData(e, new Health { Current = 100, Max = 100 });
            em.SetComponentData(e, new TimeToLive { Remaining = 9999f });
        }

#if UNITY_PHYSICS
        private void InitPhysics(EntityManager em, Entity e)
        {
            var collider = SphereCollider.Create(
                new SphereGeometry { Center = float3.zero, Radius = 0.5f },
                CollisionFilter.Default);
            em.SetComponentData(e, new PhysicsVelocity
            {
                Linear = this.random.NextFloat3Direction() * this.random.NextFloat(1f, 5f),
                Angular = this.random.NextFloat3Direction() * this.random.NextFloat(0.5f, 2f),
            });
            em.SetComponentData(e, PhysicsMass.CreateDynamic(MassProperties.UnitSphere, 1f));
            em.SetComponentData(e, new PhysicsCollider { Value = collider });
        }
#endif

        private void DestroyAll()
        {
            if (!this.allEntities.IsCreated || this.world is not { IsCreated: true }) return;
            var em = this.world.EntityManager;
            for (int i = 0; i < this.allEntities.Length; i++)
                if (em.Exists(this.allEntities[i]))
                    em.DestroyEntity(this.allEntities[i]);
            this.allEntities.Clear();
        }

        private void StartMeasuring()
        {
            this.measuring = true;
            this.tierElapsed = 0f;
            this.frameCount = 0;
            this.frameTimeSum = 0;
            this.frameTimeMax = 0;
            this.frameTimeMin = double.MaxValue;
            this.sampleCount = 0;
            this.tierStopwatch.Restart();
        }

        private void RecordFrame(float ms)
        {
            this.frameCount++;
            this.frameTimeSum += ms;
            if (ms > this.frameTimeMax) this.frameTimeMax = ms;
            if (ms < this.frameTimeMin) this.frameTimeMin = ms;
            if (this.sampleCount < this.frameSamples.Length)
                this.frameSamples[this.sampleCount++] = ms;
        }

        private void FinishTier()
        {
            this.tierStopwatch.Stop();
            var target = this.tiers[this.currentTier];
            var wall = this.tierStopwatch.Elapsed.TotalSeconds;
            var mean = this.frameTimeSum / math.max(this.frameCount, 1);
            var fps = this.frameCount / wall;
            var n = math.min(this.sampleCount, this.frameSamples.Length);
            Array.Sort(this.frameSamples, 0, n);

            Debug.Log($"[STRESS-BENCH] tier={FormatCount(target)} " +
                      $"sim={this.simCount:N0} phys={this.physCount:N0} " +
                      $"frames={this.frameCount} wall={wall:F2}s " +
                      $"fps={fps:F1} mean={mean:F3}ms " +
                      $"median={Pct(0.50f, n):F3}ms p95={Pct(0.95f, n):F3}ms " +
                      $"p99={Pct(0.99f, n):F3}ms max={this.frameTimeMax:F3}ms " +
                      $"min={this.frameTimeMin:F3}ms " +
                      $"mode={ModeLabel()}");
        }

        private float Pct(float p, int n)
        {
            if (n == 0) return 0f;
            return this.frameSamples[math.clamp((int)(p * n), 0, n - 1)];
        }

        private string ModeLabel() => this.hybridMode ? "hybrid" : "pure";

        private void AllDone()
        {
            Debug.Log("[STRESS-BENCH] All tiers complete.");
#if !UNITY_EDITOR
            Application.Quit();
#endif
        }

        private static string FormatCount(int n) => n switch
        {
            >= 1_000_000 => $"{n / 1_000_000}M",
            >= 1_000 => $"{n / 1_000}K",
            _ => n.ToString(),
        };
    }

    /// <summary>Minimal primitive pool implementing <see cref="IViewAssetProvider"/>.</summary>
    internal sealed class HybridViewPool : Cuvara.DOTS.Provisioning.IViewAssetProvider
    {
        private readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.Queue<GameObject>> pools
            = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Queue<GameObject>>();

        private readonly System.Collections.Generic.HashSet<string> warmed
            = new System.Collections.Generic.HashSet<string>();

        public void Prewarm(string key, int count)
        {
            if (!this.pools.TryGetValue(key, out var pool))
            {
                pool = new System.Collections.Generic.Queue<GameObject>();
                this.pools[key] = pool;
            }
            var type = key == "sphere" ? PrimitiveType.Sphere : PrimitiveType.Cube;
            for (int i = 0; i < count; i++)
            {
                var go = GameObject.CreatePrimitive(type);
                go.SetActive(false);
                go.transform.localScale = Vector3.one * 0.3f;
                pool.Enqueue(go);
            }
            this.warmed.Add(key);
        }

        public System.Threading.Tasks.Task PrewarmAsync(string key, int count,
            System.Threading.CancellationToken ct = default)
        {
            this.Prewarm(key, count);
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public bool IsWarm(string key) => this.warmed.Contains(key);

        public GameObject Acquire(string key, Vector3 position, Quaternion rotation,
            Transform parent = null)
        {
            if (this.pools.TryGetValue(key, out var pool) && pool.Count > 0)
            {
                var go = pool.Dequeue();
                go.transform.SetPositionAndRotation(position, rotation);
                if (parent != null) go.transform.SetParent(parent, true);
                go.SetActive(true);
                return go;
            }
            var type = key == "sphere" ? PrimitiveType.Sphere : PrimitiveType.Cube;
            var fresh = GameObject.CreatePrimitive(type);
            fresh.transform.localScale = Vector3.one * 0.3f;
            fresh.transform.SetPositionAndRotation(position, rotation);
            if (parent != null) fresh.transform.SetParent(parent, true);
            return fresh;
        }

        public System.Threading.Tasks.Task<GameObject> AcquireAsync(string key,
            Vector3 position, Quaternion rotation, Transform parent = null,
            System.Threading.CancellationToken ct = default)
        {
            return System.Threading.Tasks.Task.FromResult(this.Acquire(key, position, rotation, parent));
        }

        public void ReleaseInstance(GameObject instance)
        {
            if (instance == null) return;
            instance.SetActive(false);
        }

        public void Release(string key)
        {
            if (!this.pools.TryGetValue(key, out var pool)) return;
            while (pool.Count > 0)
            {
                var go = pool.Dequeue();
                if (go != null) UnityEngine.Object.Destroy(go);
            }
            this.pools.Remove(key);
            this.warmed.Remove(key);
        }

        public void Clear()
        {
            foreach (var k in new System.Collections.Generic.List<string>(this.pools.Keys))
                this.Release(k);
        }
    }
}
#endif
