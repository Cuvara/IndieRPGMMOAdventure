namespace Scripts.Gameplay.Inventory
{
    using System;
    using System.Threading;
    using Cysharp.Threading.Tasks;

    /// <summary>The outcome of one gameplay command (wire <c>CommandResult</c>, ADR-30).</summary>
    public readonly struct GameplayCommandResult
    {
        public GameplayCommandResult(bool ok, string error, byte[] payload)
        {
            this.Ok = ok;
            this.Error = error ?? string.Empty;
            this.Payload = payload ?? Array.Empty<byte>();
        }

        public bool Ok { get; }

        /// <summary>A <c>GameplayErrors</c> code from the server, or a netcode <c>CommandChannelErrors</c> name.</summary>
        public string Error { get; }

        public byte[] Payload { get; }
    }

    /// <summary>
    /// The command channel as the gameplay services see it: send an opcode plus bytes and get
    /// one result back; receive server pushes. A seam so the inventory logic is tested without a
    /// connection; <see cref="NetworkClientCommandChannel"/> is the real one.
    /// </summary>
    public interface IGameplayCommandChannel
    {
        /// <summary>Sends one command. Never throws for a channel failure; the result says so.</summary>
        UniTask<GameplayCommandResult> SendAsync(uint opcode, byte[] payload, CancellationToken cancellationToken);

        /// <summary>A server push: opcode and payload. May be raised off the main thread.</summary>
        event Action<uint, byte[]> PushReceived;
    }
}
