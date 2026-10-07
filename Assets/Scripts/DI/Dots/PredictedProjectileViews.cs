#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER && CUVARA_NETCODE && CUVARA_SHARED_GAMELOGIC
namespace Scripts.DI.Dots
{
    using System.Collections.Generic;
    using Cuvara.Netcode.Prediction;
    using Scripts.Gameplay;
    using UnityEngine;

    /// <summary>
    /// Draws the local player's own predicted projectiles (<see cref="ProjectilePredictor"/>,
    /// ADR-29 decision 2) from the button press until the server's projectile entity takes over.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A prediction is not a replicated entity, so it has no DOTS mirror: it is a pooled
    /// placeholder sphere positioned from <see cref="PredictedProjectile.RenderPosition"/> every
    /// frame. When the predictor hands over (the authoritative entity with the same
    /// <c>spawn_seq</c> arrived) or gives up (no entity within its timeout), the prediction is no
    /// longer listed and its sphere goes back to the pool; the authoritative entity is drawn by
    /// the DOTS view library's <c>projectile</c> archetype at its extrapolated position, so the
    /// swap is a small jump at most (the server launches from its authoritative caster position).
    /// </para>
    /// <para>Main thread only. Placeholder art, like the rest of the view library.</para>
    /// </remarks>
    public sealed class PredictedProjectileViews
    {
        private readonly List<PredictedProjectile> scratch = new List<PredictedProjectile>();
        private readonly Dictionary<uint, GameObject> live = new Dictionary<uint, GameObject>();
        private readonly Stack<GameObject> pool = new Stack<GameObject>();
        private readonly HashSet<uint> seen = new HashSet<uint>();
        private readonly List<uint> gone = new List<uint>();
        private readonly Transform root;

        public PredictedProjectileViews(Transform root)
        {
            this.root = root;
        }

        /// <summary>Spheres currently drawn.</summary>
        public int LiveCount => this.live.Count;

        /// <summary>Positions one sphere per held prediction; recycles the rest.</summary>
        public void Sync(ProjectilePredictor predictor)
        {
            this.seen.Clear();
            if (predictor != null)
            {
                predictor.GetProjectiles(this.scratch);
                foreach (var projectile in this.scratch)
                {
                    this.seen.Add(projectile.SpawnSeq);
                    if (!this.live.TryGetValue(projectile.SpawnSeq, out var view))
                    {
                        view = this.Acquire();
                        this.live[projectile.SpawnSeq] = view;
                    }

                    view.transform.position = WireAxes.ToUnity(projectile.RenderPosition);
                    view.transform.localScale = Vector3.one * Mathf.Max(0.1f, projectile.State.Radius * 2f);
                }
            }

            this.gone.Clear();
            foreach (var pair in this.live)
            {
                if (!this.seen.Contains(pair.Key)) this.gone.Add(pair.Key);
            }

            foreach (var seq in this.gone)
            {
                var view = this.live[seq];
                this.live.Remove(seq);
                view.SetActive(false);
                this.pool.Push(view);
            }
        }

        /// <summary>Destroys every sphere, pooled or live.</summary>
        public void Dispose()
        {
            foreach (var view in this.live.Values) Object.Destroy(view);
            while (this.pool.Count > 0) Object.Destroy(this.pool.Pop());
            this.live.Clear();
        }

        private GameObject Acquire()
        {
            if (this.pool.Count > 0)
            {
                var pooled = this.pool.Pop();
                pooled.SetActive(true);
                return pooled;
            }

            var view = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            view.name = "predicted-projectile";
            view.transform.SetParent(this.root, false);

            var collider = view.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);

            var renderer = view.GetComponent<Renderer>();
            if (renderer != null) renderer.material.color = new Color(1f, 0.55f, 0.1f);

            return view;
        }
    }
}
#endif
