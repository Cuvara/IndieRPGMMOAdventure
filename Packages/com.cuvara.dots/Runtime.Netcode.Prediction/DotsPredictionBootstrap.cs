using System;
using Cuvara.DOTS.Groups;
using Cuvara.Netcode.Prediction;
using Cuvara.Netcode.World;
using Unity.Entities;

namespace Cuvara.DOTS.Netcode.Prediction
{
    /// <summary>
    /// Installs the prediction driver into one world.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A third entry point rather than an addition to <c>DotsNetcodeBootstrap</c>, for the reason
    /// that one was separate from <c>DotsViewBootstrap</c>: this assembly is gated on <b>two</b>
    /// optional packages, and the adapter must keep compiling with only one of them present.
    /// Consumers call the three in order — views, adapter, prediction.
    /// </para>
    /// <para>
    /// <b>The predictor is a parameter, deliberately.</b> It must be the same instance whatever
    /// samples input calls <c>RecordInput</c> on; see <see cref="LocalPredictionReference"/> for
    /// what happens when it is not. Constructing one here would guarantee it is not.
    /// </para>
    /// </remarks>
    public static class DotsPredictionBootstrap
    {
        /// <param name="predictor">
        /// The session's single predictor, owned by the composition root. The same object whose
        /// <c>RecordInput</c> is called when input is sent.
        /// </param>
        /// <param name="world">
        /// The netcode <c>WorldState</c> being merged into, read for <c>AckTick</c> — the tick the
        /// anchor's position belongs to, which cannot travel on the entity because
        /// <c>IEntityView.SetState</c> carries no tick.
        /// </param>
        public static Entity Install(Unity.Entities.World world, LocalMovePredictor predictor, WorldState worldState)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (predictor == null) throw new ArgumentNullException(nameof(predictor));
            if (worldState == null) throw new ArgumentNullException(nameof(worldState));

            var entityManager = world.EntityManager;
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadWrite<LocalPredictionReference>());

            Entity entity;
            if (query.IsEmpty)
            {
                entity = entityManager.CreateEntity();
                entityManager.AddComponentObject(entity, new LocalPredictionReference
                {
                    Predictor = predictor,
                    World = worldState,
                });
#if UNITY_EDITOR
                entityManager.SetName(entity, "LocalPrediction");
#endif
            }
            else
            {
                entity = query.GetSingletonEntity();
                var existing = entityManager.GetComponentObject<LocalPredictionReference>(entity);
                existing.Predictor = predictor;
                existing.World = worldState;
            }

            InstallSystems(world);
            return entity;
        }

        /// <summary>Creates the driving system under <see cref="PredictionSystemGroup"/>.</summary>
        public static void InstallSystems(Unity.Entities.World world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            var initialization = world.GetOrCreateSystemManaged<InitializationSystemGroup>();
            var netcode = world.GetOrCreateSystemManaged<NetcodeSystemGroup>();
            var prediction = world.GetOrCreateSystemManaged<PredictionSystemGroup>();
            initialization.AddSystemToUpdateList(netcode);
            netcode.AddSystemToUpdateList(prediction);
            prediction.AddSystemToUpdateList(world.GetOrCreateSystem<LocalPredictionSystem>());

            initialization.SortSystems();
        }

        /// <summary>
        /// Removes the singleton and releases every transform this driver had claimed.
        /// </summary>
        /// <remarks>
        /// The release is the part that matters: dropping the reference while
        /// <see cref="PredictedTransform"/> is still on the local entity would leave
        /// <c>LocalTransform</c> with no writer at all — the adapter has stopped and the driver is
        /// gone — and a frozen avatar is the result. Uninstall hands the transform back.
        /// </remarks>
        public static void Uninstall(Unity.Entities.World world)
        {
            if (world == null || !world.IsCreated) return;

            var entityManager = world.EntityManager;

            using var predicted = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PredictedTransform>());
            if (!predicted.IsEmpty) entityManager.RemoveComponent<PredictedTransform>(predicted);

            using var query = entityManager.CreateEntityQuery(ComponentType.ReadWrite<LocalPredictionReference>());
            if (query.IsEmpty) return;

            entityManager.DestroyEntity(query.GetSingletonEntity());
        }
    }
}
