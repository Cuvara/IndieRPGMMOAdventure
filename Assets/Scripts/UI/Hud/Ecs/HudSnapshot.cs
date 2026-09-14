namespace Scripts.UI.Hud.Ecs
{
    using System;
    using System.Globalization;

    /// <summary>
    /// The boundary ViewModel the bridge pushes: a plain value, the only thing that
    /// crosses from ECS toward the UI.
    /// </summary>
    /// <remarks>
    /// No <c>VisualElement</c>, no <c>UIDocument</c>, no entity, no component — a readonly
    /// struct makes that visible at a glance, and <c>IViewModelSink.Push</c> takes it by
    /// <c>in</c> so it costs no copy. Formatting happens in <see cref="From"/> on the main
    /// thread, on change only, so the string allocations ride the change rate, not the
    /// frame rate. Culture is invariant: this is telemetry-style data, not localized copy.
    /// </remarks>
    public readonly struct HudSnapshot
    {
        /// <summary>Shown while a value has no source — no local player in the world yet.</summary>
        public const string NoValue = "—";

        public readonly string HealthCaption;
        public readonly float HealthFraction;
        public readonly string PositionCaption;
        public readonly int PlayersVisible;
        public readonly int EntitiesVisible;
        public readonly bool HasLocalPlayer;

        public HudSnapshot(
            string healthCaption,
            float healthFraction,
            string positionCaption,
            int playersVisible,
            int entitiesVisible,
            bool hasLocalPlayer)
        {
            this.HealthCaption = healthCaption;
            this.HealthFraction = healthFraction;
            this.PositionCaption = positionCaption;
            this.PlayersVisible = playersVisible;
            this.EntitiesVisible = entitiesVisible;
            this.HasLocalPlayer = hasLocalPlayer;
        }

        /// <summary>
        /// The bridge's conversion, extracted as a pure static so it is testable with
        /// NUnit alone — a <c>SystemBase</c> subclass cannot be constructed outside a
        /// world, but this can be called from anywhere.
        /// </summary>
        public static HudSnapshot From(in HudState state)
        {
            if (!state.HasLocalPlayer)
            {
                return new HudSnapshot(NoValue, 0f, NoValue, state.PlayersVisible, state.EntitiesVisible, false);
            }

            var fraction = state.MaxHp <= 0
                ? 0f
                : Math.Clamp((float)state.Hp / state.MaxHp, 0f, 1f);

            return new HudSnapshot(
                string.Format(CultureInfo.InvariantCulture, "{0}/{1}", state.Hp, state.MaxHp),
                fraction,
                string.Format(CultureInfo.InvariantCulture, "({0:0.0}, {1:0.0})", state.PosX, state.PosZ),
                state.PlayersVisible,
                state.EntitiesVisible,
                true);
        }
    }
}
