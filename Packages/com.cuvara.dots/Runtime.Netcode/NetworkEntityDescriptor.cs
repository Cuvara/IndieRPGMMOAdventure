namespace Cuvara.DOTS.Netcode
{
    /// <summary>
    /// Everything the wire says about a replicated entity at the moment it appears.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A parameter object rather than three parameters, because this interface has already
    /// broken once over exactly one added field.</b> <c>IEntityView.Spawn</c> went from
    /// <c>(id, isLocal)</c> to <c>(id, isLocal, type)</c> in netcode 0.4.0, and that took every
    /// implementation and every call site with it. This package's resolver seam is younger and
    /// smaller, and it can decline to repeat that: a fourth field — a faction, a level, a team —
    /// becomes a field on this struct, and existing <see cref="INetworkArchetypeResolver"/>
    /// implementations keep compiling untouched.
    /// </para>
    /// <para>
    /// It is not a claim that netcode should have done the same. Its interface is deliberately
    /// three narrow methods and a struct there would be a heavier promise than the seam wants.
    /// This one is a resolver input, which is a different job.
    /// </para>
    /// </remarks>
    public readonly struct NetworkEntityDescriptor
    {
        /// <summary>The replicated id. For players this is the Nakama user id.</summary>
        public readonly string Id;

        /// <summary>
        /// The server's entity kind — <c>"player"</c>, <c>"mob"</c>, <c>"npc"</c>, <c>"item"</c>,
        /// <c>"projectile"</c>, or a kind a newer simulation sends that this build does not name.
        /// Never null; empty when the server sent no type at all.
        /// </summary>
        public readonly string Type;

        /// <summary>
        /// Whether this id is the local player's.
        /// </summary>
        /// <remarks>
        /// Derived by comparing the entity id with <c>NetworkClient.UserId</c>, so it is the one
        /// thing here that does not depend on the server's vocabulary being what this build
        /// expects.
        /// </remarks>
        public readonly bool IsLocal;

        public NetworkEntityDescriptor(string id, string type, bool isLocal)
        {
            Id = id ?? string.Empty;
            Type = type ?? string.Empty;
            IsLocal = isLocal;
        }

        /// <summary>True when the server sent no kind for this entity.</summary>
        public bool HasType => !string.IsNullOrEmpty(Type);
    }
}
