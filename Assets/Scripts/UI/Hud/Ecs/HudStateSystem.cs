namespace Scripts.UI.Hud.Ecs
{
    using Cuvara.DOTS.Netcode;
    using Unity.Collections;
    using Unity.Entities;
    using Unity.Mathematics;
    using Unity.Transforms;

    /// <summary>
    /// Aggregates what the HUD shows out of the netcode mirror entities into the
    /// <see cref="HudState"/> singleton — writing it only when a shown value changed.
    /// </summary>
    /// <remarks>
    /// <para>Reads the components <c>NetworkViewCommandSystem</c> puts on every mirror:
    /// <c>NetworkEntity</c> (id, kind, IsLocal), <c>NetworkEntityState</c> (authoritative
    /// hp — deliberately not <c>Cuvara.DOTS.Simulation.Health</c>, which means
    /// "destroy at zero" and is opt-in), and <c>LocalTransform</c> (position; for the
    /// local player under prediction this is what <c>LocalPredictionSystem</c> wrote,
    /// which is exactly the position the player sees).</para>
    ///
    /// <para><b>Placement.</b> <see cref="SimulationSystemGroup"/>: after the netcode
    /// drain and prediction (both under <c>InitializationSystemGroup</c>), before the
    /// bridge in <see cref="PresentationSystemGroup"/> — the HUD never renders a frame
    /// behind the world. <see cref="DisableAutoCreationAttribute"/> because
    /// <see cref="HudEcsBootstrap"/> installs it explicitly, into the same world
    /// <c>DotsWorldBridge</c> uses, and tests install it into a throwaway one.</para>
    ///
    /// <para><b>The compare-before-write is the change contract.</b> The bridge's chunk
    /// change filter reports any write, equal or not; reading via <c>GetSingleton</c>
    /// bumps nothing, so a quiet frame costs one query walk here and zero work in the
    /// bridge and everything above it.</para>
    /// </remarks>
    [DisableAutoCreation]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct HudStateSystem : ISystem
    {
        /// <summary>
        /// The server's kind string for players, as the wire spells it — the same value
        /// <c>DotsViewArchetypes.ServerKindPlayer</c> holds (that constant lives in
        /// <c>NDC.Scripts.DI</c>, which this assembly deliberately does not reference:
        /// the HUD bridge must not depend on the DI wiring layer).
        /// </summary>
        internal const string ServerKindPlayer = "player";

        private FixedString32Bytes playerKind;

        public void OnCreate(ref SystemState state)
        {
            this.playerKind = new FixedString32Bytes(ServerKindPlayer);

            // The singleton the bridge reads. Created here so install order cannot race:
            // this system is created before the bridge's first update, and the bridge's
            // query simply matches nothing until this exists.
            using var existing = state.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<HudState>());
            if (existing.IsEmpty)
            {
                var entity = state.EntityManager.CreateEntity(ComponentType.ReadWrite<HudState>());
#if UNITY_EDITOR
                state.EntityManager.SetName(entity, "HudState");
#endif
            }

            state.RequireForUpdate<HudState>();
        }

        public void OnDestroy(ref SystemState state)
        {
            // The singleton is this system's; leaving it behind would hand the next
            // install a stale value to catch-up-push from.
            using var query = state.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<HudState>());
            if (!query.IsEmpty)
            {
                state.EntityManager.DestroyEntity(query);
            }
        }

        public void OnUpdate(ref SystemState state)
        {
            var next = new HudState();

            foreach (var (network, hp, transform) in SystemAPI
                         .Query<RefRO<NetworkEntity>, RefRO<NetworkEntityState>, RefRO<LocalTransform>>())
            {
                next.EntitiesVisible++;

                if (network.ValueRO.Type == this.playerKind || network.ValueRO.IsLocal)
                {
                    next.PlayersVisible++;
                }

                if (network.ValueRO.IsLocal)
                {
                    next.HasLocalPlayer = true;
                    next.Hp = hp.ValueRO.Hp;
                    next.MaxHp = hp.ValueRO.MaxHp;

                    // Quantized to what the HUD can display (one decimal): sub-decimal
                    // movement must not count as a change, or a moving player would push
                    // the UI every frame for digits that never differ.
                    var position = transform.ValueRO.Position;
                    next.PosX = math.round(position.x * 10f) / 10f;
                    next.PosZ = math.round(position.z * 10f) / 10f;
                }
            }

            // Compare before write — the write IS the bridge's wake-up signal.
            if (!SystemAPI.GetSingleton<HudState>().Equals(next))
            {
                SystemAPI.SetSingleton(next);
            }
        }
    }
}
