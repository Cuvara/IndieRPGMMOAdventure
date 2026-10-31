#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER && CUVARA_NETCODE && CUVARA_SHARED_GAMELOGIC
namespace Scripts.DI.Dots
{
    using Cuvara.Netcode.Prediction;
    using Cuvara.Netcode.Protocol.Messages;
    using Scripts.Gameplay.Content;
    using Shared.GameLogic.Components;

    /// <summary>
    /// What <see cref="InputEgress"/> needs from the live session: the connection state, the
    /// sink an input leaves through, and the few reads the cast composition makes. One seam, so
    /// EditMode tests drive the real egress with a scripted port and a real predictor, without a
    /// <c>NetworkClient</c> or an ECS world.
    /// </summary>
    public interface IInputEgressPort
    {
        /// <summary>True while the client is in the world; input is sampled and sent only then.</summary>
        bool InWorld { get; }

        /// <summary>The binder's render tick, stamped on every input. Zero before the first snapshot.</summary>
        double RenderTick { get; }

        /// <summary>The local projectile predictor of the current session; null before a protocol 3 join.</summary>
        ProjectilePredictor ProjectilePredictor { get; }

        /// <summary>The protocol version the server speaks; named in the cast-ignored warning.</summary>
        uint ServerProtocolVersion { get; }

        /// <summary>The content's first projectile ability, or null when there is none (yet).</summary>
        GameContentCatalog.AbilityInfo FirstProjectileAbility();

        /// <summary>The local player's feet from the latest snapshot, used when the predictor has none.</summary>
        bool TryGetSnapshotFeet(out Vec3 feet);

        /// <summary>Hands <paramref name="input"/> to the session; a no-op when there is no session.</summary>
        void SendInput(InputMessage input);

        /// <summary>
        /// Tells the prediction system input <paramref name="tick"/> just left, and refreshes the
        /// round trip its clock steering aims its lead from.
        /// </summary>
        void NoteInputSent(long tick);
    }
}
#endif
