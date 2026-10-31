#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER && CUVARA_NETCODE && CUVARA_SHARED_GAMELOGIC
namespace Scripts.DI.Dots
{
    using System;
    using Cuvara.Netcode.Prediction;
    using Cuvara.Netcode.Protocol.Messages;
    using Scripts.Gameplay.Maps;
    using Scripts.Gameplay.Presentation;
    using Shared.GameLogic.Components;
    using UnityEngine;

    /// <summary>
    /// The input half of a frame, extracted from <c>DotsWorldBridge</c>: samples the injected
    /// <see cref="IGameplayInputSource"/>, latches edge presses between sends, paces sends with
    /// <see cref="InputSendSchedule"/>, builds the <see cref="InputMessage"/> (including the cast
    /// and its aim), sends it and records it on the predictor. Plain C#, so EditMode tests run the
    /// real egress against a scripted <see cref="IInputEgressPort"/> and a real predictor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The invariant: the input the predictor records is exactly the input sent</b> - same
    /// tick, same move, same jump. Input is sampled and sent here, not inside the prediction
    /// driver, so both calls take the same locals in <see cref="Step"/>; replay diverges by
    /// construction if they ever drift apart.
    /// </para>
    /// <para>
    /// <b>Cadence.</b> A pinned schedule at <see cref="InputCadence.RecommendedSendHz"/>, the
    /// rate the netcode DOTS Sample sends at. Not once per frame: that was ~1000 inputs a second on
    /// an uncapped desktop loop, nearly all coalesced by the server, and the client's
    /// one-recorded-step-per-input history stopped describing what the server did. Idle inputs are
    /// sent too (a zero vector), because a release that never reached the server kept the player
    /// moving for its 250 ms silence timeout and then snapped it back; <c>docs/API.md</c> requires
    /// the vector on every input tick.
    /// </para>
    /// <para>
    /// The cast and aim composition (<c>TryAddProjectileCast</c>, <c>AimFrom</c>,
    /// <c>TryGetLocalFeet</c>) stays here for now.
    /// </para>
    /// </remarks>
    public sealed class InputEgress
    {
        /// <summary>How far along wire +y a cast with no pointer and no explicit aim point is aimed.</summary>
        public const float NoPointerAimDistance = 10f;

        private readonly IInputEgressPort port;
        private readonly LocalMovePredictor predictor;
        private readonly IGameplayInputSource input;
        private readonly Camera followCamera;
        private readonly Func<double> clock;
        private readonly Action<string, long> trace;

        private InputSendSchedule sendSchedule;
        private bool sendScheduleStarted;
        private long inputTick;

        /// <summary>Edge-triggered presses latched until the next scheduled send, so none is lost between sends.</summary>
        private bool pendingJump;

        private bool pendingCast;
        private bool castWarningLogged;

        /// <param name="clock">Seconds, monotonic; null reads <c>Time.realtimeSinceStartupAsDouble</c>.</param>
        public InputEgress(
            IInputEgressPort port,
            LocalMovePredictor predictor,
            IGameplayInputSource input,
            Camera followCamera,
            Func<double> clock = null,
            Action<string, long> trace = null)
        {
            this.port = port;
            this.predictor = predictor;
            this.input = input;
            this.followCamera = followCamera;
            this.clock = clock ?? (() => Time.realtimeSinceStartupAsDouble);
            this.trace = trace;
        }

        /// <summary>
        /// Whether this egress owns input. Off, nothing is sampled or sent and the schedule
        /// restarts when it is on again. The bridge refreshes it from its serialized field each frame.
        /// </summary>
        public bool DriveInput { get; set; } = true;

        /// <summary>The tick of the last input sent; the next one is this plus one.</summary>
        public long InputTick => this.inputTick;

        /// <summary>One frame of input: latch presses, and send when the schedule is due. Runs after the session reset.</summary>
        public void Step()
        {
            if (this.DriveInput && this.port.InWorld)
            {
                // Input is sampled and SENT here, not inside the prediction driver: the tick
                // recorded must be the tick that went to the server.
                var inputSource = this.input;
                this.pendingJump |= inputSource.JumpPressed();
                this.pendingCast |= inputSource.CastPressed();

                var now = this.clock();
                if (!this.sendScheduleStarted)
                {
                    this.sendSchedule.Start(InputCadence.RecommendedSendHz(GameConstants.DefaultTickRate), now);
                    this.sendScheduleStarted = true;
                }

                if (this.sendSchedule.SecondsUntilDue(now) <= 0.0)
                {
                    // Sampled at the send, so the vector sent is the stick right now. Sent even
                    // when it is zero: that is how a release reaches the server.
                    inputSource.ReadMove(out var moveX, out var moveY);
                    var jump = this.pendingJump;
                    var cast = this.pendingCast;
                    this.pendingJump = false;
                    this.pendingCast = false;

                    this.inputTick++;
                    var message = new InputMessage { Tick = this.inputTick, MoveX = moveX, MoveY = moveY, Jump = jump };
                    if (cast)
                    {
                        this.TryAddProjectileCast(message);
                    }

                    // Every input says which instant the player was looking at, so the server can
                    // rewind hit targets to it (ADR-29 decision 4). Zero before the first snapshot.
                    message.SetRenderTime(this.port.RenderTick);
                    this.port.SendInput(message);
                    this.predictor?.RecordInput(this.inputTick, moveX, moveY, jump);

                    this.port.NoteInputSent(this.inputTick);

                    this.sendSchedule.NoteSent(now);
                    this.trace?.Invoke("NoteSent", this.inputTick);
                }
            }
            else
            {
                this.sendScheduleStarted = false;
            }
        }

        /// <summary>
        /// Adds the content's projectile ability to <paramref name="message"/>, aimed where the
        /// pointer meets the ground at the player's height, and starts predicting it. Sends no
        /// cast at all when there is nothing valid to send (no content, a protocol 2 server, a
        /// refused spawn): a cast the client cannot predict is not one it should request.
        /// </summary>
        private void TryAddProjectileCast(InputMessage message)
        {
            var ability = this.port.FirstProjectileAbility();
            var projectiles = this.port.ProjectilePredictor;
            if (ability == null || projectiles == null)
            {
                if (!this.castWarningLogged)
                {
                    this.castWarningLogged = true;
                    Debug.LogWarning(
                        "[DotsWorldBridge] cast key ignored: " +
                        (projectiles == null
                            ? $"the server speaks protocol {this.port.ServerProtocolVersion} (projectiles need 3)."
                            : "the content has no projectile ability (content not loaded yet?)."));
                }

                return;
            }

            if (!this.TryGetLocalFeet(out var feet))
            {
                return;
            }

            var aim = this.AimFrom(feet);
            var spawnSeq = projectiles.Fire(
                AimPoint.LaunchOrigin(feet),
                AimPoint.LaunchTarget(aim),
                ability.ProjectileSpeed,
                ability.ProjectileRadius,
                ability.ProjectileRange);
            if (spawnSeq == 0u)
            {
                // ProjectileLogic refused (aim on the origin): send nothing, predict nothing.
                return;
            }

            message.AbilityId = ability.Id;
            message.AimX = aim.X;
            message.AimY = aim.Y;
            message.AimZ = aim.Z;
            message.SpawnSeq = spawnSeq;
        }

        /// <summary>The local player's feet in wire space: the predicted body, else the snapshot.</summary>
        private bool TryGetLocalFeet(out Vec3 feet)
        {
            if (this.predictor != null && this.predictor.IsEnabled && this.predictor.Reconciles > 0)
            {
                feet = this.predictor.Position3;
                return true;
            }

            return this.port.TryGetSnapshotFeet(out feet);
        }

        /// <summary>
        /// The ground point under the pointer at the player's own height, in wire space; with no
        /// pointer (a headless or automated run) a point <see cref="NoPointerAimDistance"/> units
        /// along wire +y.
        /// </summary>
        private Vec3 AimFrom(in Vec3 feet)
        {
            if (this.input.TryAimPoint(new Vector3(feet.X, feet.Y, feet.Z), out var explicitAim))
            {
                return new Vec3(explicitAim.x, explicitAim.y, explicitAim.z);
            }

            var camera = this.followCamera != null ? this.followCamera : Camera.main;
            if (camera != null &&
                this.input.TryPointerPosition(out var screen) &&
                AimPoint.TryGroundAim(camera.ScreenPointToRay(screen), feet.Z, out var aim))
            {
                return aim;
            }

            return new Vec3(feet.X, feet.Y + NoPointerAimDistance, feet.Z);
        }
    }
}
#endif
