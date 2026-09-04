#if CUVARA_DOTS
namespace Scripts.DI.Dots
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Cuvara.DOTS.Provisioning;
    using UnityEngine;

    /// <summary>
    /// <see cref="IViewAssetProvider"/> over Unity primitives — the client's placeholder view
    /// provider until real art and a real asset pipeline exist.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this and not <c>GameFoundationViewAssetProvider</c>.</b> The GameFoundation-backed
    /// provider resolves <c>IAssetsManager</c> + <c>IObjectPoolManager</c>, which
    /// <c>RegisterGameFoundation()</c> registers — and this project's <c>GameLifetimeScope</c>
    /// never calls that. Wiring the whole GameFoundation service stack into the container just to
    /// feed the view layer is a project decision (audio, pooling and asset management all come
    /// with it), not something to smuggle in with the DOTS wiring. When that decision is made,
    /// replace the <c>IViewAssetProvider</c> registration in
    /// <see cref="DotsRegistration.RegisterDots"/> with
    /// <c>builder.RegisterGameFoundationViewProvisioning()</c> after
    /// <c>RegisterGameFoundation()</c> and delete this class.
    /// </para>
    /// <para>
    /// Adapted from the package's <c>Samples~/NetworkedPrediction/PrimitiveViewProvider</c>. It
    /// pools for real — a freed instance is reused — so the recycle path the view layer relies on
    /// is exercised rather than hidden behind Instantiate/Destroy.
    /// </para>
    /// </remarks>
    public sealed class PrimitiveViewAssetProvider : IViewAssetProvider
    {
        private readonly Dictionary<string, Stack<GameObject>> pools = new Dictionary<string, Stack<GameObject>>();
        private readonly Dictionary<GameObject, string> keys = new Dictionary<GameObject, string>();
        private readonly Dictionary<string, (PrimitiveType Shape, Color Colour, float Scale)> kinds;
        private readonly Transform root;

        /// <param name="root">Optional parent for spawned views. Null parents to the scene root.</param>
        public PrimitiveViewAssetProvider(Transform root)
        {
            this.root = root;
            this.kinds = new Dictionary<string, (PrimitiveType Shape, Color Colour, float Scale)>
            {
                [DotsViewArchetypes.PlayerLocal] = (PrimitiveType.Capsule, new Color(0.2f, 0.8f, 1f), 1.2f),
                [DotsViewArchetypes.PlayerRemote] = (PrimitiveType.Capsule, new Color(0.9f, 0.9f, 0.9f), 1f),
                [DotsViewArchetypes.Mob] = (PrimitiveType.Sphere, new Color(0.9f, 0.15f, 0.1f), 0.8f),
            };
        }

        public Task PrewarmAsync(string key, int count, CancellationToken cancellationToken = default)
        {
            var pool = this.Pool(key);
            while (pool.Count < count)
            {
                pool.Push(this.Create(key));
            }

            return Task.CompletedTask;
        }

        public bool IsWarm(string key) => key != null && this.kinds.ContainsKey(key);

        public GameObject Acquire(string key, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            var pool = this.Pool(key);
            var instance = pool.Count > 0 ? pool.Pop() : this.Create(key);
            instance.transform.SetPositionAndRotation(position, rotation);
            instance.SetActive(true);
            return instance;
        }

        public Task<GameObject> AcquireAsync(string key, Vector3 position, Quaternion rotation, Transform parent = null, CancellationToken cancellationToken = default)
            => Task.FromResult(this.Acquire(key, position, rotation, parent));

        public void ReleaseInstance(GameObject instance)
        {
            if (instance == null || !this.keys.TryGetValue(instance, out var key))
            {
                return;
            }

            instance.SetActive(false);
            this.Pool(key).Push(instance);
        }

        public void Release(string key)
        {
            if (key == null || !this.pools.TryGetValue(key, out var pool))
            {
                return;
            }

            while (pool.Count > 0)
            {
                var instance = pool.Pop();
                this.keys.Remove(instance);
                Object.Destroy(instance);
            }
        }

        private Stack<GameObject> Pool(string key)
        {
            if (!this.pools.TryGetValue(key, out var pool))
            {
                this.pools[key] = pool = new Stack<GameObject>();
            }

            return pool;
        }

        private GameObject Create(string key)
        {
            // Named tuple elements on both ternary operands: an unnamed fallback tuple drops the
            // names from the common type and only fails at the use site.
            var kind = this.kinds.TryGetValue(key, out var k)
                ? k
                : (Shape: PrimitiveType.Cube, Colour: Color.magenta, Scale: 1f);
            var instance = GameObject.CreatePrimitive(kind.Shape);
            instance.name = key;
            instance.transform.SetParent(this.root, false);
            instance.transform.localScale = Vector3.one * kind.Scale;

            // Nothing uses physics on views; a hundred pooled colliders is a hundred things the
            // physics system walks for no reason.
            var collider = instance.GetComponent<Collider>();
            if (collider != null)
            {
                Object.Destroy(collider);
            }

            var renderer = instance.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = kind.Colour;
            }

            instance.SetActive(false);
            this.keys[instance] = key;
            return instance;
        }
    }
}
#endif
