namespace Scripts.Gameplay.Inventory
{
    using System;
    using System.Threading;
    using Cuvara.Netcode.Client;
    using Cuvara.Netcode.Protocol.Messages;
    using Cysharp.Threading.Tasks;

    /// <summary>
    /// <see cref="IGameplayCommandChannel"/> over <see cref="NetworkClient"/>'s command channel
    /// (<c>SendCommandAsync</c> / <c>ServerPushReceived</c>, netcode 0.46.0). The netcode client
    /// only moves bytes; what they mean is <c>Shared.GameLogic.Gameplay</c>'s codec.
    /// </summary>
    /// <remarks>
    /// Channel failures (not connected, a protocol 2 server, the session closing with the
    /// command in flight) arrive as results with <c>Ok == false</c> and a
    /// <c>CommandChannelErrors</c> name, never as exceptions, and are passed through unchanged.
    /// </remarks>
    public sealed class NetworkClientCommandChannel : IGameplayCommandChannel, IDisposable
    {
        private readonly NetworkClient client;

        public NetworkClientCommandChannel(NetworkClient client)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.client.ServerPushReceived += this.OnPush;
        }

        public event Action<uint, byte[]> PushReceived;

        public async UniTask<GameplayCommandResult> SendAsync(uint opcode, byte[] payload, CancellationToken cancellationToken)
        {
            CommandResult result = await this.client.SendCommandAsync(opcode, payload ?? Array.Empty<byte>(), cancellationToken);
            return result == null
                ? new GameplayCommandResult(false, "client_no_result", null)
                : new GameplayCommandResult(result.Ok, result.Error, result.Payload);
        }

        public void Dispose()
        {
            this.client.ServerPushReceived -= this.OnPush;
        }

        private void OnPush(ServerPush push)
        {
            if (push != null)
            {
                this.PushReceived?.Invoke(push.Opcode, push.Payload);
            }
        }
    }
}
