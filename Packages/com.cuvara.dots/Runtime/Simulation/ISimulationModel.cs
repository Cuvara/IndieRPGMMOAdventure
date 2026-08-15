using Unity.Mathematics;

namespace Cuvara.DOTS.Simulation
{
    /// <summary>
    /// The rules of the game, as the client is allowed to see them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The package owns this interface; <c>Shared.GameLogic</c> is one implementation of it and
    /// never the interface itself.</b> That is what lets consumer code be byte-identical whether or
    /// not the git dependency is installed — exactly one registration line inside this package
    /// differs, and it lives in <c>SimulationModelVContainer</c>.
    /// </para>
    /// <para>
    /// <see cref="IsAuthoritative"/> is the load-bearing member. Prediction code must check it and
    /// <b>refuse to run</b> when it is false, rather than fall back to an approximation. The
    /// server's integration step splits its multiply into separate float locals specifically to deny
    /// the JIT an FMA contraction — a re-implementation would not reproduce that by accident, and a
    /// prediction that is wrong in the last place is worse than no prediction: it drifts silently
    /// instead of being visibly absent.
    /// </para>
    /// </remarks>
    public interface ISimulationModel
    {
        /// <summary>
        /// True when this model runs the same code the server runs, so its results may be trusted
        /// for prediction. False when no shared logic is installed — see
        /// <see cref="PassiveSimulationModel"/>.
        /// </summary>
        bool IsAuthoritative { get; }

        /// <summary>
        /// Tuning values behind the rules. Meaningless unless
        /// <see cref="SimConstants.IsPopulated"/> is true.
        /// </summary>
        SimConstants Constants { get; }

        /// <summary>Fixed timestep in seconds for a tick rate; 0 for a non-positive rate.</summary>
        float DeltaTimeForTickRate(int tickRate);

        /// <summary>
        /// Validates an input vector, integrates one step and clamps to bounds.
        /// </summary>
        /// <param name="input">Raw direction from the input layer; magnitude is validated, not assumed.</param>
        /// <param name="newPosition">
        /// Resulting position, equal to <c>entity.Position</c> for every result other than
        /// <see cref="SimMoveResult.Accepted"/> and <see cref="SimMoveResult.Clamped"/>.
        /// </param>
        SimMoveResult TryMove(in SimEntity entity, float2 input, float dt, in SimBounds bounds, out float2 newPosition);

        /// <summary>Damage one entity deals another, floored at the shared minimum.</summary>
        int CalculateDamage(in SimEntity attacker, in SimEntity defender);

        /// <summary>Whether two positions are within <paramref name="range"/> of each other.</summary>
        bool InRange(float2 a, float2 b, float range);
    }
}
