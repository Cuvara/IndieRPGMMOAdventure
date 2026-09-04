#if CUVARA_DOTS
namespace Scripts.DI.Dots
{
    /// <summary>
    /// The archetype names the client's view layer knows, in one place: the
    /// <c>TypeArchetypeResolver</c> maps server entity kinds to these, the
    /// <c>ViewConfigCatalog</c> is keyed by them, and <see cref="PrimitiveViewAssetProvider"/>
    /// shapes a primitive per name. Three call sites spelling the same strings independently is
    /// how a renamed archetype becomes an invisible entity.
    /// </summary>
    public static class DotsViewArchetypes
    {
        public const string PlayerLocal = "player-local";
        public const string PlayerRemote = "player-remote";
        public const string Mob = "mob";

        /// <summary>Server entity kinds, as the wire spells them (see the netcode message set).</summary>
        public const string ServerKindPlayer = "player";
        public const string ServerKindMob = "mob";
    }
}
#endif
