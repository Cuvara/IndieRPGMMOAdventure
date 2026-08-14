using Unity.Mathematics;

namespace Cuvara.DOTS.Simulation
{
    /// <summary>
    /// The model used when <c>com.rpgmmo.shared-gamelogic</c> is not installed: it applies whatever
    /// the server said and predicts nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It deliberately does not re-implement the server's movement rule.</b> That rule is not
    /// "position += direction * speed * dt": <c>MovementSystem.Integrate</c> splits the multiply
    /// into separate float locals to deny the JIT an FMA contraction, and <c>Vec2.SqrMagnitude</c>
    /// casts every intermediate to <c>float</c> because C# permits higher-precision evaluation and
    /// .NET's RyuJIT and Unity's Mono JIT choose differently. Code written from the formula would
    /// look right and be one ULP wrong, and a prediction that is one ULP wrong accumulates into
    /// visible desync over a few hundred ticks while looking, at every individual step, correct.
    /// No prediction at all is the honest failure mode; the entity simply sits at its last
    /// authoritative position until the next snapshot moves it.
    /// </para>
    /// <para>
    /// Everything here returns a refusal, and <see cref="IsAuthoritative"/> is false so callers can
    /// see the refusal coming instead of inferring it from suspicious values.
    /// </para>
    /// </remarks>
    public sealed class PassiveSimulationModel : ISimulationModel
    {
        public bool IsAuthoritative => false;

        public SimConstants Constants => SimConstants.Unavailable;

        /// <summary>Always 0 — a timestep is only meaningful to a model that integrates.</summary>
        public float DeltaTimeForTickRate(int tickRate) => 0f;

        /// <summary>
        /// Leaves the entity where the last authoritative snapshot put it and reports
        /// <see cref="SimMoveResult.Unavailable"/>.
        /// </summary>
        public SimMoveResult TryMove(in SimEntity entity, float2 input, float dt, in SimBounds bounds, out float2 newPosition)
        {
            newPosition = entity.Position;
            return SimMoveResult.Unavailable;
        }

        /// <summary>
        /// Always 0. The floor is <c>GameConstants.MinDamage</c>, which is not available here, and
        /// guessing it would be the literal-copy trap.
        /// </summary>
        public int CalculateDamage(in SimEntity attacker, in SimEntity defender) => 0;

        /// <summary>
        /// Always false. The comparison itself is trivial, but the server's version routes through
        /// <c>Vec2.DistanceSq</c> with its explicit per-operation casts, and a locally written
        /// version would be a different function that happens to agree most of the time.
        /// </summary>
        public bool InRange(float2 a, float2 b, float range) => false;
    }
}
