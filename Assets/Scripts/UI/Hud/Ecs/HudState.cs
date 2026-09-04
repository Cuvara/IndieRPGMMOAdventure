namespace Scripts.UI.Hud.Ecs
{
    using System;
    using Unity.Entities;

    /// <summary>
    /// The one component the HUD bridge reads: everything the HUD shows, aggregated onto a
    /// single singleton entity by <see cref="HudStateSystem"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a singleton and not the mirror entities themselves.</b>
    /// <c>EcsViewModelBridge</c> converts and pushes every entity its query matches, with
    /// one <c>lastPushed</c> — it is shaped for "one component instance feeds one screen".
    /// Bridging <c>NetworkEntityState</c> directly would push every replicated entity's hp
    /// in arbitrary order and the HUD would display whichever came last. The aggregation
    /// (find the local player, count the rest) is simulation-side work, so it lives in an
    /// ECS system writing this component, and the bridge stays a pure converter.</para>
    ///
    /// <para><b>Written only on change, and that is load-bearing.</b> The bridge's
    /// change-version filter is chunk-granular: any write marks the chunk changed, equal
    /// value or not. <see cref="HudStateSystem"/> therefore compares before it writes —
    /// see the <see cref="IEquatable{T}"/> implementation this struct carries — so a frame
    /// in which nothing the HUD shows changed bumps no version and wakes no sink.</para>
    ///
    /// <para>Position is quantized to 0.1 units by the writer before it lands here: the
    /// HUD displays one decimal, so sub-decimal movement would be a value change the UI
    /// cannot even render.</para>
    /// </remarks>
    public struct HudState : IComponentData, IEquatable<HudState>
    {
        /// <summary>Local player hit points, as the server last reported them.</summary>
        public int Hp;

        /// <summary>Local player maximum hit points.</summary>
        public int MaxHp;

        /// <summary>Local player X on the server's plane (Unity world X), quantized to 0.1.</summary>
        public float PosX;

        /// <summary>Local player Y on the server's plane (Unity world Z under the XZPlane mapping), quantized to 0.1.</summary>
        public float PosZ;

        /// <summary>Replicated entities of kind "player" currently mirrored, the local player included.</summary>
        public int PlayersVisible;

        /// <summary>Every replicated entity currently mirrored, whatever its kind.</summary>
        public int EntitiesVisible;

        /// <summary>True while a mirror entity marked <c>IsLocal</c> exists.</summary>
        public bool HasLocalPlayer;

        public bool Equals(HudState other)
        {
            return this.Hp == other.Hp
                && this.MaxHp == other.MaxHp
                && this.PosX.Equals(other.PosX)
                && this.PosZ.Equals(other.PosZ)
                && this.PlayersVisible == other.PlayersVisible
                && this.EntitiesVisible == other.EntitiesVisible
                && this.HasLocalPlayer == other.HasLocalPlayer;
        }

        public override bool Equals(object obj) => obj is HudState other && this.Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(this.Hp);
            hash.Add(this.MaxHp);
            hash.Add(this.PosX);
            hash.Add(this.PosZ);
            hash.Add(this.PlayersVisible);
            hash.Add(this.EntitiesVisible);
            hash.Add(this.HasLocalPlayer);
            return hash.ToHashCode();
        }
    }
}
