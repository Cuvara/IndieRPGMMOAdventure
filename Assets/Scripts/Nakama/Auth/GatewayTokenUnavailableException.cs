namespace Scripts.Nakama.Auth
{
    using System;

    /// <summary>
    /// The gateway token could not be minted <b>this time</b>: the Nakama RPC timed out, the
    /// socket broke, or Nakama answered 5xx / 429 / any status that is not a refusal.
    /// </summary>
    /// <remarks>
    /// Deliberately not an <see cref="InvalidOperationException"/>. Netcode's reconnect loop
    /// treats that type as "the provider can never produce a credential" and stops after one
    /// attempt (<c>ReconnectPolicy.IsPermanentFailure</c>); any other type is retried within the
    /// budget. A network that has not come back yet is the usual cause, and the player's body
    /// is still held on the server while the loop runs.
    /// </remarks>
    public sealed class GatewayTokenUnavailableException : Exception
    {
        public GatewayTokenUnavailableException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
