using Cuvara.DOTS.Simulation;
using Shared.GameLogic.Components;
using Shared.GameLogic.Systems;
using Unity.Mathematics;

namespace Cuvara.DOTS.GameLogic
{
    /// <summary>
    /// <see cref="ISimulationModel"/> backed by the same <c>Shared.GameLogic</c> the authoritative
    /// server runs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every method is a delegation. Nothing is reimplemented, reordered or "optimised" on the way
    /// through — the value of this seam is entirely that the arithmetic happens inside the shared
    /// library, where the golden vectors pin it bit-for-bit against the server.
    /// </para>
    /// <para>
    /// No <c>#if</c> appears in this file. The whole assembly is gated by its asmdef's
    /// <c>defineConstraints</c>, so when the shared package is absent this code is not compiled at
    /// all rather than compiled-around.
    /// </para>
    /// </remarks>
    public sealed class SharedGameLogicSimulation : ISimulationModel
    {
        private readonly SimConstants _constants;

        /// <summary>
        /// Snapshots <see cref="GameConstants"/> into <see cref="SimConstants"/>.
        /// </summary>
        /// <remarks>
        /// Read from the source, never restated as literals. A version bump of
        /// <c>com.rpgmmo.shared-gamelogic</c> then propagates through this package without anyone
        /// editing it, and <c>SimConstantsParityTests</c> fails loudly if that ever stops being
        /// true.
        /// </remarks>
        public SharedGameLogicSimulation()
        {
            _constants = new SimConstants(
                GameConstants.MaxInputMagnitude,
                GameConstants.InputDeadzoneSq,
                GameConstants.MaxDeltaTime,
                GameConstants.DisplacementTolerance,
                GameConstants.DefaultMapWidth,
                GameConstants.DefaultMapHeight,
                GameConstants.AttackRange,
                GameConstants.AttackCooldownMs,
                GameConstants.MinDamage,
                GameConstants.DefaultAoiRadius,
                GameConstants.DefaultTickRate,
                GameConstants.DefaultKeyframeInterval);
        }

        public bool IsAuthoritative => true;

        public SimConstants Constants => _constants;

        /// <summary>Attack cooldown in simulation ticks; delegates so the ceiling rounding matches.</summary>
        public int AttackCooldownTicks(int tickRate) => GameConstants.AttackCooldownTicks(tickRate);

        public float DeltaTimeForTickRate(int tickRate) => MovementSystem.DeltaTimeForTickRate(tickRate);

        public SimMoveResult TryMove(in SimEntity entity, float2 input, float dt, in SimBounds bounds, out float2 newPosition)
        {
            var state = entity.ToEntityState();
            var mapBounds = bounds.ToMapBounds();

            var result = MovementSystem.TryMove(in state, input.x, input.y, dt, in mapBounds, out var moved);

            newPosition = moved.ToFloat2();
            return result.ToSimMoveResult();
        }

        public int CalculateDamage(in SimEntity attacker, in SimEntity defender)
        {
            var attackerState = attacker.ToEntityState();
            var defenderState = defender.ToEntityState();
            return CombatLogic.CalculateDamage(in attackerState, in defenderState);
        }

        public bool InRange(float2 a, float2 b, float range) =>
            CombatLogic.InRange(a.ToVec2(), b.ToVec2(), range);
    }
}
