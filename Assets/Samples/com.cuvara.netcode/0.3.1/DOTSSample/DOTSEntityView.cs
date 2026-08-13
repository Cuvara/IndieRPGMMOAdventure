using System.Collections.Generic;
using Cuvara.Netcode.View;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

namespace DOTSSample
{
    /// <summary>
    /// <see cref="IEntityView"/> backed by ECS entities. Spawns/despawns/updates
    /// replicated entities as DOTS entities with 3D meshes rendered via Entities.Graphics.
    /// </summary>
    /// <remarks>
    /// Server coordinates are (x, y) on a 2D plane. These map to Unity (X, 0.5, Z)
    /// so a top-down camera sees the world as the server lays it out.
    /// Each player gets a unique colour from a fixed palette so multiple clients are
    /// visually distinguishable.
    /// </remarks>
    public sealed class DOTSEntityView : IEntityView
    {
        /// <summary>Per-entity display info exposed for the overlay.</summary>
        public readonly struct EntityLabel
        {
            public readonly string Id;
            public readonly bool IsLocal;
            public readonly float3 WorldPos;
            public readonly Color Color;
            public readonly int Hp;
            public readonly int MaxHp;

            public EntityLabel(string id, bool isLocal, float3 worldPos, Color color, int hp, int maxHp)
            {
                Id = id;
                IsLocal = isLocal;
                WorldPos = worldPos;
                Color = color;
                Hp = hp;
                MaxHp = maxHp;
            }
        }

        private static readonly Color[] Palette =
        {
            new Color(0.2f, 0.8f, 1f),    // 0: cyan — local player
            new Color(1f,   0.4f, 0.4f),   // 1: red
            new Color(0.4f, 1f,   0.4f),   // 2: green
            new Color(1f,   0.8f, 0.2f),   // 3: yellow
            new Color(0.8f, 0.4f, 1f),     // 4: purple
            new Color(1f,   0.6f, 0.2f),   // 5: orange
            new Color(0.4f, 0.8f, 0.8f),   // 6: teal
            new Color(1f,   0.4f, 0.8f),   // 7: pink
        };

        private readonly Dictionary<string, Entity> _entities = new Dictionary<string, Entity>();
        private readonly Dictionary<string, int> _playerColorIndex = new Dictionary<string, int>();
        private readonly Dictionary<string, (int hp, int maxHp)> _hpCache = new Dictionary<string, (int, int)>();
        private readonly EntityManager _em;
        private readonly Mesh _localMesh;
        private readonly Mesh _remoteMesh;
        private int _nextColorIndex = 1; // 0 reserved for local

        public bool IsValid { get; }

        public DOTSEntityView()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                Debug.LogWarning("[DOTSEntityView] DOTS World not ready — entity rendering disabled");
                IsValid = false;
                return;
            }

            _em = world.EntityManager;
            _localMesh = GetPrimitiveMesh(PrimitiveType.Capsule);
            _remoteMesh = GetPrimitiveMesh(PrimitiveType.Capsule);
            IsValid = true;
        }

        public int Count => _entities.Count;

        public void Spawn(string id, bool isLocal)
        {
            if (!IsValid || string.IsNullOrEmpty(id) || _entities.ContainsKey(id))
                return;

            int colorIdx;
            if (isLocal)
            {
                colorIdx = 0;
            }
            else
            {
                if (!_playerColorIndex.TryGetValue(id, out colorIdx))
                {
                    colorIdx = _nextColorIndex;
                    _nextColorIndex = (_nextColorIndex % (Palette.Length - 1)) + 1;
                    _playerColorIndex[id] = colorIdx;
                }
            }

            var color = Palette[colorIdx % Palette.Length];
            var material = CreatePlayerMaterial(color);
            var mesh = isLocal ? _localMesh : _remoteMesh;
            var scale = isLocal ? 1.2f : 1f;

            var entity = _em.CreateEntity();
            var shortId = id.Substring(0, System.Math.Min(8, id.Length));
            _em.SetName(entity, (isLocal ? "local:" : "remote:") + shortId);

            var renderMeshDescription = new RenderMeshDescription(ShadowCastingMode.On);
            var renderMeshArray = new RenderMeshArray(new[] { material }, new[] { mesh });
            RenderMeshUtility.AddComponents(entity, _em, renderMeshDescription, renderMeshArray,
                MaterialMeshInfo.FromRenderMeshArrayIndices(0, 0));

            _em.AddComponentData(entity, new LocalTransform
            {
                Position = float3.zero,
                Rotation = quaternion.identity,
                Scale = scale
            });

            _em.AddComponentData(entity, new NetworkEntityTag
            {
                IsLocal = isLocal,
                PlayerId = new FixedString64Bytes(shortId),
                ColorIndex = colorIdx
            });

            _entities[id] = entity;
            _hpCache[id] = (0, 0);
        }

        public void Despawn(string id)
        {
            if (!IsValid || id == null || !_entities.TryGetValue(id, out var entity))
                return;

            _entities.Remove(id);
            _hpCache.Remove(id);
            if (_em.Exists(entity))
                _em.DestroyEntity(entity);
        }

        public void SetState(string id, float x, float y, int hp, int maxHp)
        {
            if (!IsValid || !_entities.TryGetValue(id, out var entity) || !_em.Exists(entity))
                return;

            _em.SetComponentData(entity, new LocalTransform
            {
                Position = new float3(x, 0.5f, y),
                Rotation = quaternion.identity,
                Scale = _em.GetComponentData<LocalTransform>(entity).Scale
            });

            _hpCache[id] = (hp, maxHp);
        }

        /// <summary>
        /// Enumerates all live entities with their current positions and display info.
        /// Used by the OnGUI overlay to draw floating labels.
        /// </summary>
        public void GetEntityLabels(List<EntityLabel> result)
        {
            result.Clear();
            if (!IsValid) return;

            foreach (var kv in _entities)
            {
                var id = kv.Key;
                var entity = kv.Value;
                if (!_em.Exists(entity) || !_em.HasComponent<LocalTransform>(entity))
                    continue;

                var tag = _em.GetComponentData<NetworkEntityTag>(entity);
                var pos = _em.GetComponentData<LocalTransform>(entity).Position;
                var color = Palette[tag.ColorIndex % Palette.Length];
                var hp = _hpCache.TryGetValue(id, out var hpData) ? hpData : (0, 0);

                result.Add(new EntityLabel(id, tag.IsLocal, pos, color, hp.Item1, hp.Item2));
            }
        }

        private static Mesh GetPrimitiveMesh(PrimitiveType type)
        {
            var go = GameObject.CreatePrimitive(type);
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(go);
            return mesh;
        }

        private static Material CreatePlayerMaterial(Color color)
        {
            var baseMat = Resources.Load<Material>("DOTSRemoteMaterial");
            if (baseMat == null)
            {
                baseMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            }

            var mat = new Material(baseMat);
            mat.color = color;

            // Try common URP/HDRP property names
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);

            return mat;
        }
    }
}
