using Cuvara.DOTS.Groups;
using Shared.GameLogic.Components;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Cuvara.DOTS.Netcode.Prediction
{
    /// <summary>
    /// Drives <c>LocalMovePredictor</c> from ECS: reconciles it against the local entity's
    /// <see cref="ReconciliationAnchor"/>, advances it, and writes the result to
    /// <c>LocalTransform</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The DOTS half of the seam, and only that half.</b> The predictor owns the algorithm — the
    /// input buffer, the replay through <c>TryMove</c>, the smoothing. This system owns reading the
    /// anchor, supplying the tick, claiming and releasing <see cref="PredictedTransform"/>, and
    /// writing the transform. It lives here rather than in netcode because the alternative is
    /// netcode taking a dependency on Entities, and the arrow between these packages is one-way.
    /// </para>
    /// <para>
    /// <b>It reconciles from <c>ServerPosition</c>, never from <c>Position</c>.</b> The predictor
    /// replays the shared simulation, which clamps against map bounds expressed in server
    /// coordinates, so it must be handed the coordinates the server actually sent. <c>Position</c>
    /// is the view's field; recovering server space from it would mean inverting
    /// <c>SnapshotSpaceMapping</c>, and a float round trip through a projection is not bit-exact —
    /// replay would integrate from a position the server never held. That is why the anchor carries
    /// both and why the mapping still has no inverse.
    /// </para>
    /// <para>
    /// <b>Input is not sampled here.</b> <c>RecordInput</c> belongs to whatever reads the device and
    /// sends the move to the server, because the tick it records must be the tick that was sent. A
    /// system that invented its own input would produce a buffer the server never saw, and replay
    /// against it would diverge by construction.
    /// </para>
    /// <para>
    /// Not Bursted: it reaches a managed predictor through a managed singleton.
    /// </para>
    /// </remarks>
    // In PredictionSystemGroup, which runs after SnapshotApplyGroup — so the anchor read here was
    // written by this frame's snapshot, not the previous one. Reconciling against a stale anchor is
    // a one-frame-old correction, which looks like mistuned prediction rather than like a bug.
    [DisableAutoCreation]
    [UpdateInGroup(typeof(PredictionSystemGroup))]
    internal partial struct LocalPredictionSystem : ISystem
    {
        private EntityQuery _localEntities;
        private long _lastAckTick;

        public void OnCreate(ref SystemState state)
        {
            _localEntities = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<NetworkEntity, ReconciliationAnchor, LocalTransform>()
                .Build(ref state);

            state.RequireForUpdate<LocalPredictionReference>();
            state.RequireForUpdate(_localEntities);
        }

        public void OnUpdate(ref SystemState state)
        {
            var reference = SystemAPI.ManagedAPI.GetSingleton<LocalPredictionReference>();
            var predictor = reference.Predictor;
            if (predictor == null) return;

            var entityManager = state.EntityManager;
            var entities = _localEntities.ToEntityArray(Allocator.Temp);

            for (var i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                if (!entityManager.GetComponentData<NetworkEntity>(entity).IsLocal) continue;

                if (!predictor.IsEnabled)
                {
                    // Release the transform rather than leaving the marker on. A disabled predictor
                    // writes nothing, so a marker left behind would leave LocalTransform with NO
                    // writer at all and freeze the avatar in place — the marker's failure mode
                    // reached from the other side, and one that surfaces in a build rather than in a
                    // test, because a disabled predictor is a runtime configuration.
                    if (entityManager.HasComponent<PredictedTransform>(entity))
                    {
                        entityManager.RemoveComponent<PredictedTransform>(entity);
                    }

                    continue;
                }

                // Claimed before the first write, so the adapter stops writing LocalTransform in the
                // same frame this system starts. Claiming after would give both a writer for one
                // frame, which is the two-writer race this whole component pair exists to prevent.
                if (!entityManager.HasComponent<PredictedTransform>(entity))
                {
                    entityManager.AddComponent<PredictedTransform>(entity);
                }

                var anchor = entityManager.GetComponentData<ReconciliationAnchor>(entity);

                // Only on a new acknowledgement. Reconciling every frame against an unchanged tick
                // would replay the same unacknowledged inputs repeatedly and count corrections that
                // never happened, which is a diagnostics lie as well as wasted work.
                var ackTick = reference.World?.AckTick ?? 0L;
                if (ackTick > _lastAckTick)
                {
                    _lastAckTick = ackTick;
                    predictor.Reconcile(ToVec2(anchor.ServerPosition), ackTick);
                }

                predictor.Advance(SystemAPI.Time.DeltaTime);

                // Mapped here, on the way out, using the same SnapshotSpaceMapping the adapter uses —
                // read from the view singleton rather than duplicated, so the predicted and the
                // authoritative paths cannot drift apart in how they place the world.
                var mapping = SystemAPI.ManagedAPI.GetSingleton<NetworkEntityViewReference>().View.Mapping;
                var predicted = predictor.Position;

                var transform = entityManager.GetComponentData<LocalTransform>(entity);
                transform.Position = mapping.ToWorld(predicted.X, predicted.Y);
                entityManager.SetComponentData(entity, transform);

                entityManager.SetComponentData(entity, new LocalToWorld
                {
                    Value = float4x4.TRS(
                        transform.Position,
                        transform.Rotation,
                        new float3(transform.Scale, transform.Scale, transform.Scale)),
                });
            }

            entities.Dispose();
        }

        /// <summary>
        /// The one conversion site between ECS maths and the shared simulation's vector type.
        /// </summary>
        /// <remarks>
        /// <c>SimConversions</c> in <c>Cuvara.DOTS.GameLogic</c> already has this extension method,
        /// and it is <c>internal</c> — reaching it would mean either widening that assembly's public
        /// API or an <c>InternalsVisibleTo</c> grant that couples two independently gated assemblies,
        /// for one line. Kept as a single private site here instead, which is what "convert at the
        /// boundary, not once per call" is actually asking for.
        /// </remarks>
        private static Vec2 ToVec2(in float2 value) => new Vec2(value.x, value.y);
    }
}
