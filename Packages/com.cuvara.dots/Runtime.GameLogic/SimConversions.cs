using Cuvara.DOTS.Simulation;
using Shared.GameLogic.Components;
using Shared.GameLogic.Systems;
using Unity.Mathematics;

namespace Cuvara.DOTS.GameLogic
{
    /// <summary>
    /// The one place <c>float2</c> and <c>Vec2</c> meet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All conversion lives on this side of the seam because it can only live here:
    /// <c>Shared.GameLogic.asmdef</c> declares <c>noEngineReferences: true</c>, so that assembly
    /// cannot reference <c>Unity.Mathematics</c> and can never learn what a <c>float2</c> is. The
    /// core package assembly does not reference <c>Shared.GameLogic</c> either, which leaves exactly
    /// this optional assembly.
    /// </para>
    /// <para>
    /// The conversions are field copies and nothing else — no arithmetic, no reordering. Any math
    /// performed here would be math the server did not perform, which is the whole failure mode the
    /// shared library exists to prevent.
    /// </para>
    /// </remarks>
    internal static class SimConversions
    {
        public static Vec2 ToVec2(this float2 value) => new Vec2(value.x, value.y);

        public static float2 ToFloat2(this Vec2 value) => new float2(value.X, value.Y);

        public static MapBounds ToMapBounds(this in SimBounds bounds) =>
            new MapBounds(bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY);

        /// <summary>
        /// Projects a <see cref="SimEntity"/> onto the shared <see cref="EntityState"/>.
        /// </summary>
        /// <remarks>
        /// <c>Id</c> and <c>Type</c> are left null on purpose. They are <c>string</c>, which is why
        /// <see cref="SimEntity"/> does not carry them, and no rule reached through this seam
        /// (<c>MovementSystem.TryMove</c>, <c>CombatLogic.CalculateDamage</c>,
        /// <c>CombatLogic.InRange</c>) reads either field. Identity stays on the ECS side as a
        /// <c>FixedString64Bytes</c>.
        /// </remarks>
        public static EntityState ToEntityState(this in SimEntity entity) => new EntityState
        {
            Position = entity.Position.ToVec2(),
            Speed = entity.Speed,
            Hp = entity.Hp,
            MaxHp = entity.MaxHp,
            Attack = entity.Attack,
            Defense = entity.Defense,
            Dead = entity.Dead,
            CooldownUntilTick = entity.CooldownUntilTick,
        };

        /// <summary>
        /// Maps the shared result enum onto the package's own.
        /// </summary>
        /// <remarks>
        /// An explicit switch, not a cast. The numeric values happen to line up today; a cast would
        /// turn a future reordering on the server side into a silently wrong classification, while
        /// this throws the unmapped value back at the caller.
        /// </remarks>
        public static SimMoveResult ToSimMoveResult(this MoveResult result) => result switch
        {
            MoveResult.None => SimMoveResult.None,
            MoveResult.Accepted => SimMoveResult.Accepted,
            MoveResult.Clamped => SimMoveResult.Clamped,
            MoveResult.Rejected => SimMoveResult.Rejected,
            MoveResult.Blocked => SimMoveResult.Blocked,
            _ => throw new System.ArgumentOutOfRangeException(
                nameof(result), result, "unmapped MoveResult — com.rpgmmo.shared-gamelogic added a case"),
        };
    }
}
