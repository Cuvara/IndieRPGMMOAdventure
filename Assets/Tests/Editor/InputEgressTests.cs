#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER && CUVARA_NETCODE && CUVARA_SHARED_GAMELOGIC
namespace Tests.Editor
{
    using System.Collections.Generic;
    using Cuvara.Netcode.Prediction;
    using Cuvara.Netcode.Protocol.Messages;
    using NUnit.Framework;
    using Scripts.DI;
    using Scripts.DI.Dots;
    using Scripts.Gameplay.Content;
    using Shared.GameLogic.Components;
    using UnityEngine;

    /// <summary>
    /// The real <see cref="InputEgress"/> over a scripted input source and port and a real
    /// <see cref="LocalMovePredictor"/>. The predictor's own trace sink is the record of what it
    /// was asked to record, so "sent == recorded" compares two independent logs. Each test names
    /// the production line whose removal it catches.
    /// </summary>
    public sealed class InputEgressTests
    {
        private const double Start = 100.0;
        private const double FrameSeconds = 1.0 / 240.0;

        private static double Period => 1.0 / InputCadence.RecommendedSendHz(GameConstants.DefaultTickRate);

        // ---- the invariant: recorded == sent ---------------------------------------------------

        /// <summary>
        /// Fails if <c>predictor.RecordInput</c> is removed (recorded stays empty), if it is given
        /// another tick or value than the message (element comparison), or if a jump latched
        /// between sends is dropped (no sent jump).
        /// </summary>
        [Test]
        public void EverySentInput_IsExactlyWhatThePredictorRecorded_AcrossSeveralPeriods()
        {
            var h = new Harness();
            h.Source.MoveAt = t => t < Period * 2 ? (0.25f, 1f) : t < Period * 4 ? (-1f, 0.5f) : (0f, 0f);

            // A jump pressed on a frame that is not a send frame, and then the stick released.
            h.Run(Period * 6.5, onFrame: (frame, t) => h.Source.JumpEdge = frame == 3);

            Assert.That(h.Sent.Count, Is.GreaterThanOrEqualTo(6), "several cadence periods were covered");
            Assert.That(h.Recorded.Count, Is.EqualTo(h.Sent.Count));
            for (var i = 0; i < h.Sent.Count; i++)
            {
                Assert.That(h.Recorded[i].Tick, Is.EqualTo(h.Sent[i].Tick), $"tick #{i}");
                Assert.That(h.Recorded[i].MoveX, Is.EqualTo(h.Sent[i].MoveX), $"moveX #{i}");
                Assert.That(h.Recorded[i].MoveY, Is.EqualTo(h.Sent[i].MoveY), $"moveY #{i}");
                Assert.That(h.Recorded[i].Jump, Is.EqualTo(h.Sent[i].Jump), $"jump #{i}");
            }

            Assert.That(h.Sent.Exists(m => m.Jump), Is.True, "the edge-latched jump reached a send");
            Assert.That(h.Sent[h.Sent.Count - 1].MoveX, Is.EqualTo(0f));
            Assert.That(h.Sent[h.Sent.Count - 1].MoveY, Is.EqualTo(0f), "the zero-move release is sent, not skipped");
        }

        /// <summary>Fails if the tick handed to <c>NoteInputSent</c> differs from the sent tick, or the call is removed.</summary>
        [Test]
        public void TheClockSteeringIsToldTheSameTickThatWasSent()
        {
            var h = new Harness();

            h.Run(Period * 3.5, onFrame: null);

            Assert.That(h.Sent.Count, Is.GreaterThan(0));
            Assert.That(h.Port.NotedTicks, Is.EqualTo(h.Sent.ConvertAll(m => m.Tick)));
        }

        /// <summary>Fails if <c>SetRenderTime</c> is removed or reads another source than the port's render tick.</summary>
        [Test]
        public void EveryInputCarriesTheBindersRenderTick()
        {
            var h = new Harness();
            h.Port.RenderTick = 1234.0;

            h.Run(Period * 2.5, onFrame: null);

            Assert.That(h.Sent.Count, Is.GreaterThan(0));
            Assert.That(h.Sent.TrueForAll(m => m.RenderTick == 1234UL), Is.True);
        }

        // ---- cadence ---------------------------------------------------------------------------

        /// <summary>
        /// Fails if the <c>SecondsUntilDue</c> gate is removed (a send every 1/240 s frame instead
        /// of every period) or the schedule is started at another rate.
        /// </summary>
        [Test]
        public void SendsHappenAtTheRecommendedRate_NotOncePerFrame()
        {
            var h = new Harness();
            var periods = 10;

            h.Run(Period * periods, onFrame: null);

            Assert.That(h.Sent.Count, Is.InRange(periods, periods + 1));
            var framesRun = (int)(Period * periods / FrameSeconds);
            Assert.That(h.Sent.Count, Is.LessThan(framesRun / 2), "far fewer sends than frames");
            for (var i = 1; i < h.SentAt.Count; i++)
            {
                Assert.That(h.SentAt[i] - h.SentAt[i - 1], Is.EqualTo(Period).Within(FrameSeconds + 1e-9));
            }
        }

        /// <summary>Fails if the pending flags are not cleared at a send: the jump would repeat on every later send.</summary>
        [Test]
        public void AJumpPressedBetweenSends_IsSentOnce()
        {
            var h = new Harness();
            h.Run(Period * 0.5, onFrame: null); // the first send is due immediately and has gone
            var before = h.Sent.Count;

            h.Source.JumpEdge = true;
            h.Step();
            h.Source.JumpEdge = false;
            h.Run(Period * 4, onFrame: null);

            var afterEdge = h.Sent.GetRange(before, h.Sent.Count - before);
            Assert.That(afterEdge.FindAll(m => m.Jump).Count, Is.EqualTo(1));
            Assert.That(afterEdge[0].Jump, Is.True, "the very next send carries it");
        }

        /// <summary>Fails if <c>pendingCast</c> is cleared by the first send but the cast is not attached to it.</summary>
        [Test]
        public void ACastPressedBetweenSends_RidesTheNextSendOnly()
        {
            var h = new Harness();
            h.Port.Ability = new GameContentCatalog.AbilityInfo
            {
                Id = 5u, Delivery = "projectile", ProjectileSpeed = 20f, ProjectileRadius = 0.3f, ProjectileRange = 25f,
            };
            h.Port.Projectiles = new ProjectilePredictor(GameConstants.DefaultTickRate);
            h.Port.SnapshotFeet = new Vec3(2f, 3f, 0f);
            h.Run(Period * 0.5, onFrame: null);
            var before = h.Sent.Count;

            h.Source.CastEdge = true;
            h.Step();
            h.Source.CastEdge = false;
            h.Run(Period * 3, onFrame: null);

            var afterEdge = h.Sent.GetRange(before, h.Sent.Count - before);
            Assert.That(afterEdge[0].AbilityId, Is.EqualTo(5u));
            Assert.That(afterEdge[0].SpawnSeq, Is.Not.EqualTo(0u));
            Assert.That(afterEdge.FindAll(m => m.AbilityId != 0u).Count, Is.EqualTo(1));
        }

        // ---- cast composition ------------------------------------------------------------------

        /// <summary>Fails if the no-pointer fallback aims anywhere but <see cref="InputEgress.NoPointerAimDistance"/> along +y of the feet.</summary>
        [Test]
        public void ACastWithNoPointer_AimsTheNamedDistanceAlongWireY_FromTheSnapshotFeet()
        {
            var h = new Harness();
            h.Port.Ability = new GameContentCatalog.AbilityInfo
            {
                Id = 7u, Delivery = "projectile", ProjectileSpeed = 20f, ProjectileRadius = 0.3f, ProjectileRange = 25f,
            };
            h.Port.Projectiles = new ProjectilePredictor(GameConstants.DefaultTickRate);
            h.Port.SnapshotFeet = new Vec3(2f, 3f, 1f);
            h.Source.CastEdge = true;

            h.Step();

            Assert.That(h.Sent.Count, Is.EqualTo(1));
            Assert.That(InputEgress.NoPointerAimDistance, Is.EqualTo(10f));
            Assert.That(h.Sent[0].AimX, Is.EqualTo(2f));
            Assert.That(h.Sent[0].AimY, Is.EqualTo(3f + InputEgress.NoPointerAimDistance));
            Assert.That(h.Sent[0].AimZ, Is.EqualTo(1f));
            Assert.That(h.Port.Projectiles.Count, Is.EqualTo(1), "the cast is predicted as well as sent");
        }

        /// <summary>
        /// Fails if a cast with nothing valid to predict is sent anyway, or if it stops the move
        /// from being sent and recorded (the input still goes out without the cast).
        /// </summary>
        [Test]
        public void ACastWithNoProjectilePredictor_IsDroppedButTheMoveIsStillSentAndRecorded()
        {
            var h = new Harness();
            h.Port.Ability = new GameContentCatalog.AbilityInfo { Id = 7u, Delivery = "projectile" };
            h.Port.Projectiles = null;
            h.Source.CastEdge = true;
            h.Source.MoveAt = t => (1f, 0f);
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("cast key ignored"));

            h.Step();

            Assert.That(h.Sent.Count, Is.EqualTo(1));
            Assert.That(h.Sent[0].AbilityId, Is.EqualTo(0u));
            Assert.That(h.Sent[0].SpawnSeq, Is.EqualTo(0u));
            Assert.That(h.Recorded.Count, Is.EqualTo(1));
            Assert.That(h.Recorded[0].MoveX, Is.EqualTo(1f));
        }

        // ---- gating ----------------------------------------------------------------------------

        /// <summary>Fails if the in-world or <c>DriveInput</c> condition is removed from <c>Step</c>.</summary>
        [Test]
        public void NothingIsSentOutOfWorld_OrWithDriveInputOff()
        {
            var h = new Harness();

            h.Port.InWorld = false;
            h.Run(Period * 2, onFrame: null);
            h.Port.InWorld = true;
            h.Egress.DriveInput = false;
            h.Run(Period * 2, onFrame: null);

            Assert.That(h.Sent, Is.Empty);
            Assert.That(h.Recorded, Is.Empty);
        }

        // ---- order against the session reset ---------------------------------------------------

        /// <summary>
        /// Drives <see cref="InputEgress.Step"/> as the gate's feed, the way
        /// <c>EcsSessionInstallation.Tick</c> does. Fails if the gate runs the feed before the
        /// reset, or if <c>Step</c> sends before the frame's reset has run.
        /// </summary>
        [Test]
        public void InTheFrameANewSessionAppears_TheResetRunsBeforeTheInputIsSent()
        {
            var h = new Harness();
            var gate = new SessionResetGate<object>();
            var session = new object();
            h.Port.Log = h.Log;

            gate.Tick(true, session, s => h.Log.Add("reset"), h.Egress.Step);

            Assert.That(h.Log, Is.EqualTo(new[] { "reset", "send" }));
        }

        // ---- harness ---------------------------------------------------------------------------

        private sealed class Harness
        {
            private double now = Start;
            private int frame;

            public Harness()
            {
                this.Predictor = new LocalMovePredictor(new PredictionSettings(
                    GameConstants.DefaultTickRate, 6f, MapBounds.Default));
                this.Predictor.Trace = (in PredictorTraceEvent e) =>
                {
                    if (e.Call == PredictorCall.RecordInput)
                    {
                        this.Recorded.Add(new RecordedInput { Tick = e.A, MoveX = e.X, MoveY = e.Y, Jump = e.Flag });
                    }
                };
                this.Port = new ScriptedPort(this);
                this.Egress = new InputEgress(this.Port, this.Predictor, this.Source, null, () => this.now);
            }

            public ScriptedSource Source { get; } = new ScriptedSource();

            public ScriptedPort Port { get; }

            public LocalMovePredictor Predictor { get; }

            public InputEgress Egress { get; }

            public List<InputMessage> Sent { get; } = new List<InputMessage>();

            public List<double> SentAt { get; } = new List<double>();

            public List<RecordedInput> Recorded { get; } = new List<RecordedInput>();

            public List<string> Log { get; } = new List<string>();

            public double Elapsed => this.now - Start;

            public void Step()
            {
                this.Source.Now = this.Elapsed;
                this.Egress.Step();
            }

            public void Run(double seconds, System.Action<int, double> onFrame)
            {
                var until = this.now + seconds;
                while (this.now < until)
                {
                    this.frame++;
                    onFrame?.Invoke(this.frame, this.Elapsed);
                    this.Step();
                    this.now += FrameSeconds;
                }
            }

            public void NoteSent(InputMessage message)
            {
                this.Sent.Add(message);
                this.SentAt.Add(this.now);
            }
        }

        private struct RecordedInput
        {
            public long Tick;
            public float MoveX;
            public float MoveY;
            public bool Jump;
        }

        private sealed class ScriptedSource : IGameplayInputSource
        {
            private bool jumpEdge;
            private bool castEdge;

            public double Now;

            public System.Func<double, (float, float)> MoveAt = t => (0f, 0f);

            /// <summary>Reports a press for exactly one read, like the real edge-triggered source.</summary>
            public bool JumpEdge { set => this.jumpEdge = value; }

            public bool CastEdge { set => this.castEdge = value; }

            public void ReadMove(out float x, out float y) => (x, y) = this.MoveAt(this.Now);

            public bool JumpPressed()
            {
                var pressed = this.jumpEdge;
                this.jumpEdge = false;
                return pressed;
            }

            public bool CastPressed()
            {
                var pressed = this.castEdge;
                this.castEdge = false;
                return pressed;
            }

            public bool PickupPressed() => false;

            public bool InventoryTogglePressed() => false;

            public bool TryPointerPosition(out Vector2 position)
            {
                position = default;
                return false;
            }

            public bool TryAimPoint(Vector3 feet, out Vector3 aim)
            {
                aim = default;
                return false;
            }
        }

        private sealed class ScriptedPort : IInputEgressPort
        {
            private readonly Harness harness;

            public ScriptedPort(Harness harness) => this.harness = harness;

            public bool InWorld { get; set; } = true;

            public double RenderTick { get; set; }

            public ProjectilePredictor Projectiles { get; set; }

            public GameContentCatalog.AbilityInfo Ability { get; set; }

            public Vec3 SnapshotFeet { get; set; }

            public List<string> Log { get; set; }

            public List<long> NotedTicks { get; } = new List<long>();

            ProjectilePredictor IInputEgressPort.ProjectilePredictor => this.Projectiles;

            public uint ServerProtocolVersion => 2u;

            public GameContentCatalog.AbilityInfo FirstProjectileAbility() => this.Ability;

            public bool TryGetSnapshotFeet(out Vec3 feet)
            {
                feet = this.SnapshotFeet;
                return true;
            }

            public void SendInput(InputMessage input)
            {
                this.Log?.Add("send");
                this.harness.NoteSent(input);
            }

            public void NoteInputSent(long tick) => this.NotedTicks.Add(tick);
        }
    }
}
#endif
