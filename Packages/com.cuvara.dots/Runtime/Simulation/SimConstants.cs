namespace Cuvara.DOTS.Simulation
{
    /// <summary>
    /// The tuning values the shared simulation runs on, carried as data rather than as constants.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every field is read from <c>Shared.GameLogic.Components.GameConstants</c> at construction
    /// time.</b> Nothing in this package restates one of those numbers as a literal. A literal copy
    /// compiles, passes every test that also holds the copy, and then silently disagrees with the
    /// server the moment the shared package is bumped — which is exactly the class of bug the
    /// shared library exists to prevent. <c>SimConstantsParityTests</c> asserts field-by-field
    /// equality against the source for that reason.
    /// </para>
    /// <para>
    /// A struct of fields rather than <c>const</c>s: a <c>const</c> is baked into every calling
    /// assembly at compile time, so a consumer built against an older version would keep the old
    /// value even after the dependency moved.
    /// </para>
    /// </remarks>
    public readonly struct SimConstants
    {
        public readonly float MaxInputMagnitude;
        public readonly float InputDeadzoneSq;
        public readonly float MaxDeltaTime;
        public readonly float DisplacementTolerance;
        public readonly float DefaultMapWidth;
        public readonly float DefaultMapHeight;
        public readonly float AttackRange;
        public readonly int AttackCooldownMs;
        public readonly int MinDamage;
        public readonly float DefaultAoiRadius;
        public readonly int DefaultTickRate;
        public readonly int DefaultKeyframeInterval;

        /// <summary>True when these values came from a real source rather than from <see cref="Unavailable"/>.</summary>
        public readonly bool IsPopulated;

        public SimConstants(
            float maxInputMagnitude,
            float inputDeadzoneSq,
            float maxDeltaTime,
            float displacementTolerance,
            float defaultMapWidth,
            float defaultMapHeight,
            float attackRange,
            int attackCooldownMs,
            int minDamage,
            float defaultAoiRadius,
            int defaultTickRate,
            int defaultKeyframeInterval)
        {
            MaxInputMagnitude = maxInputMagnitude;
            InputDeadzoneSq = inputDeadzoneSq;
            MaxDeltaTime = maxDeltaTime;
            DisplacementTolerance = displacementTolerance;
            DefaultMapWidth = defaultMapWidth;
            DefaultMapHeight = defaultMapHeight;
            AttackRange = attackRange;
            AttackCooldownMs = attackCooldownMs;
            MinDamage = minDamage;
            DefaultAoiRadius = defaultAoiRadius;
            DefaultTickRate = defaultTickRate;
            DefaultKeyframeInterval = defaultKeyframeInterval;
            IsPopulated = true;
        }

        /// <summary>
        /// All-zero constants, used by <see cref="PassiveSimulationModel"/>.
        /// </summary>
        /// <remarks>
        /// Zeros rather than plausible defaults, on purpose. With the shared package absent there is
        /// no source of truth for these numbers, and inventing one would be the literal-copy trap
        /// wearing a different hat: plausible-looking values would let prediction code run and
        /// quietly disagree with the server. Zeros make misuse fail loudly, and
        /// <see cref="IsPopulated"/> lets a caller check instead of guessing.
        /// </remarks>
        public static SimConstants Unavailable => default;
    }
}
