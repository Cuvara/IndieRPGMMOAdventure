#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER && CUVARA_NETCODE && CUVARA_SHARED_GAMELOGIC
namespace Scripts.DI.Dots
{
    using Cuvara.DOTS.Netcode.Prediction;
    using Cuvara.Netcode.Client;
    using Cuvara.Netcode.Prediction;
    using Cuvara.Netcode.Protocol.Messages;
    using Scripts.Gameplay.Content;
    using Shared.GameLogic.Components;

    /// <summary>The production <see cref="IInputEgressPort"/>: the live client, content and session installation.</summary>
    public sealed class InstallationInputPort : IInputEgressPort
    {
        private readonly NetworkClient client;
        private readonly GameContentService content;
        private readonly EcsSessionInstallation installation;

        public InstallationInputPort(NetworkClient client, GameContentService content, EcsSessionInstallation installation)
        {
            this.client = client;
            this.content = content;
            this.installation = installation;
        }

        public bool InWorld => this.client.State == NetworkClientState.InWorld;

        public double RenderTick => this.installation.Binder.RenderTick;

        public ProjectilePredictor ProjectilePredictor => this.installation.ProjectilePredictor;

        public uint ServerProtocolVersion => this.client.ServerProtocolVersion;

        public GameContentCatalog.AbilityInfo FirstProjectileAbility() =>
            this.content != null ? this.content.Catalog.FirstProjectileAbility() : null;

        public bool TryGetSnapshotFeet(out Vec3 feet)
        {
            if (this.client.World.TryGet(this.client.UserId, out var self))
            {
                feet = new Vec3(self.X, self.Y, self.Z);
                return true;
            }

            feet = default;
            return false;
        }

        public void SendInput(InputMessage input) => this.client.Session?.SendInput(input);

        public void NoteInputSent(long tick)
        {
            // The prediction system steers the predictor's clock toward the server tick;
            // it needs to know when each input left to measure the acknowledgement lead.
            // The round trip sizes the lead the steering aims for; refreshed per send rather
            // than per frame because the lookup builds an entity query.
            DotsPredictionBootstrap.NoteInputSent(this.installation.World, tick);
            if (DotsPredictionBootstrap.TryGetClockSteering(this.installation.World, out var steering))
            {
                steering.RoundTripMs = this.client.Session?.RoundTripMs ?? 0L;
            }
        }
    }
}
#endif
