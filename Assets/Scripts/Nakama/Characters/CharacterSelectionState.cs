namespace Scripts.Nakama.Characters
{
    /// <summary>
    /// The roster character the session plays, shared between the session flow that chooses it
    /// and <see cref="Auth.NakamaAuthProvider"/>, which asks <c>gateway_token</c> for a token
    /// carrying it (<c>character_id</c> -> <c>cid</c> claim, ADR-31).
    /// </summary>
    /// <remarks>
    /// The gateway refuses an enter-world whose <c>character_id</c> differs from the token's
    /// <c>cid</c> (<c>character_mismatch</c>), so the token and <c>NetworkClient.CharacterId</c>
    /// must name the same character. One root-scoped instance is how both read one value:
    /// the provider mints every token — including a reconnect's — for whatever is set here.
    /// Empty means the account's default character, the protocol 2 behaviour.
    /// </remarks>
    public sealed class CharacterSelectionState
    {
        /// <summary>The selected roster id; null or empty for the account's default character.</summary>
        public string CharacterId { get; set; }
    }
}
