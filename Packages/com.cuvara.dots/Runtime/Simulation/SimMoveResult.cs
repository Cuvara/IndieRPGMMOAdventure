namespace Cuvara.DOTS.Simulation
{
    /// <summary>
    /// Outcome of a movement step through the seam.
    /// </summary>
    /// <remarks>
    /// Values 0-4 mirror <c>Shared.GameLogic.Systems.MoveResult</c>, but the conversion in
    /// <c>Cuvara.DOTS.GameLogic</c> is an explicit switch rather than a cast: matching numbers today
    /// are a coincidence to be checked, not a contract to lean on, and a reordered enum on the
    /// server side would otherwise turn "rejected" into "clamped" with no compiler complaint.
    /// </remarks>
    public enum SimMoveResult
    {
        /// <summary>Input inside the deadzone; no movement requested.</summary>
        None = 0,

        /// <summary>Input accepted as-is.</summary>
        Accepted = 1,

        /// <summary>Input magnitude exceeded 1 and was normalized before integration.</summary>
        Clamped = 2,

        /// <summary>Input was grossly invalid (NaN, infinity, or implausibly large).</summary>
        Rejected = 3,

        /// <summary>The entity cannot move: dead, non-positive speed, or non-positive dt.</summary>
        Blocked = 4,

        /// <summary>
        /// No simulation model is installed, so nothing was computed and the position is unchanged.
        /// Only <see cref="PassiveSimulationModel"/> returns this; it has no counterpart on the
        /// server, which always has the rules available.
        /// </summary>
        Unavailable = 5,
    }
}
