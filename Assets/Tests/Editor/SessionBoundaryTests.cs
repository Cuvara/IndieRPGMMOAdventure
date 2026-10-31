using System.Collections.Generic;
using Cuvara.Netcode.Prediction;
using Cuvara.Netcode.Snapshot;
using Cuvara.Netcode.View;
using Cuvara.Netcode.World;
using NUnit.Framework;
using Scripts.DI.Dots;
using Shared.GameLogic.Components;

namespace Tests.Editor
{
    /// <summary>
    /// The routing of the session-start reset. <c>EcsSessionInstallation.Tick</c> and
    /// <c>OnReconnected</c> (moved out of <c>DotsWorldBridge</c>) call only <see cref="SessionResetGate{TSession}"/> for these paths, so
    /// these tests drive the production gate. A bridge-level test was not feasible in EditMode: the
    /// bridge needs an installed view library, a prewarmed asset provider and a live
    /// <c>NetworkClient</c>. What the reset itself touches (binder, predictor) is checked one level
    /// down with the real <see cref="WorldViewBinder"/> and <see cref="LocalMovePredictor"/>; the
    /// view generation is counted, not exercised. If the gate stops resetting before the feed,
    /// <see cref="NewSessionWhileInWorld_ResetsBeforeTheSameFramesFeed"/> fails; if
    /// <c>OnReconnected</c> stops sharing the once-per-session boundary, the Reconnect_* tests fail.
    /// </summary>
    public sealed class SessionBoundaryTests
    {
        private const string Local = "local-1";
        private const string Remote = "remote-1";

        // ---- ordering: the reset precedes the feed of the frame a session appears in --------

        [Test]
        public void NewSessionWhileInWorld_ResetsBeforeTheSameFramesFeed()
        {
            var h = new Harness();

            h.Frame(true, new FakeSession());

            Assert.That(h.Log, Is.EqualTo(new[] { "begin", "feed" }));
        }

        [Test]
        public void NotInWorld_NeverBegins_ButStillFeeds()
        {
            var h = new Harness();

            h.Frame(false, new FakeSession());

            Assert.That(h.Log, Is.EqualTo(new[] { "feed" }));
            Assert.That(h.Generations, Is.EqualTo(0));
        }

        // ---- (a) a non-reconnect new session -------------------------------------------------

        [Test]
        public void NewSessionObject_BeginsExactlyOnce_NoMatterHowOftenItIsObserved()
        {
            var h = new Harness();
            var session = new FakeSession();

            for (int frame = 0; frame < 5; frame++)
            {
                h.Frame(true, session);
            }

            Assert.That(h.Generations, Is.EqualTo(1));
            Assert.That(h.BinderResets, Is.EqualTo(1));
        }

        [Test]
        public void RetryOrDungeonJoin_NewObject_ChangesGenerationAndResetsBinderAndPredictor()
        {
            var h = new Harness();
            h.Frame(true, new FakeSession());
            h.RecordInput(1);
            Assert.That(h.Predictor.PendingCount, Is.EqualTo(1), "precondition");

            h.Frame(true, new FakeSession());

            Assert.That(h.Generations, Is.EqualTo(2));
            Assert.That(h.BinderResets, Is.EqualTo(2));
            Assert.That(h.Predictor.PendingCount, Is.EqualTo(0), "the old input backlog is gone");
        }

        [Test]
        public void NullSession_NeverBegins()
        {
            var h = new Harness();

            h.Frame(true, null);

            Assert.That(h.Generations, Is.EqualTo(0));
        }

        // ---- (b) reconnect: the event and the identity check both see the same session -------

        [Test]
        public void Reconnect_EventThenFrame_IsOneGeneration()
        {
            var h = new Harness();
            h.Frame(true, new FakeSession());
            var reconnected = new FakeSession();

            Assert.That(h.Reconnected(reconnected), Is.EqualTo(ReconnectOutcome.Began));
            h.Frame(true, reconnected);

            Assert.That(h.Generations, Is.EqualTo(2), "one for the first session, one for the reconnect");
        }

        [Test]
        public void Reconnect_FrameThenEvent_IsOneGeneration_AndTheEventSaysSo()
        {
            var h = new Harness();
            h.Frame(true, new FakeSession());
            var reconnected = new FakeSession();

            h.Frame(true, reconnected);
            var outcome = h.Reconnected(reconnected);

            Assert.That(outcome, Is.EqualTo(ReconnectOutcome.AlreadyBegun));
            Assert.That(h.Generations, Is.EqualTo(2));
        }

        [Test]
        public void Reconnect_WithNoSessionAtCallbackTime_ResetsNothing_ThenTheFrameCheckBegins()
        {
            var h = new Harness();
            h.Frame(true, new FakeSession());

            Assert.That(h.Reconnected(null), Is.EqualTo(ReconnectOutcome.NoSession));
            Assert.That(h.Generations, Is.EqualTo(1), "nothing reset by the event");

            h.Frame(true, new FakeSession());

            Assert.That(h.Generations, Is.EqualTo(2), "the per-frame check picked the new session up");
        }

        // ---- (c) B's scenario: the new session's ticks start ABOVE the old ones --------------

        [Test]
        public void NewSessionStartingAboveTheOldTicks_CarriesNoBinderClockOrPredictorHistory()
        {
            var h = new Harness();
            var world = new WorldState();
            h.Frame(true, new FakeSession());
            for (int i = 0; i < 100; i++)
            {
                h.Feed(world, 100 + i, i);
                h.RecordInput(i + 1);
            }

            Assert.That(h.Binder.RenderTick, Is.GreaterThan(150.0), "precondition: session A interpolates");
            Assert.That(h.Predictor.PendingCount, Is.GreaterThan(0), "precondition: A left queued inputs");

            // Session B: NetworkClient resets the world; the gate sees the new object before the feed.
            world.Reset();
            h.Frame(true, new FakeSession());

            Assert.That(h.Predictor.PendingCount, Is.EqualTo(0), "stale queued commands dropped");
            for (int i = 0; i < 20; i++)
            {
                h.Feed(world, 500 + i, i);
            }

            Assert.That(h.Binder.RenderTick, Is.GreaterThan(500.0).And.LessThan(520.0),
                "the render clock is rebuilt in the new session's tick space, not A's");
        }

        /// <summary>The production gate wired as the bridge wires it; Begin performs the reset pieces the bridge does.</summary>
        private sealed class Harness
        {
            private readonly SessionResetGate<FakeSession> gate = new SessionResetGate<FakeSession>();
            private readonly FakeClock clock = new FakeClock();
            private bool seeded;

            public Harness()
            {
                this.Binder = new WorldViewBinder(new NullView(), null, this.clock);
                this.Predictor = new LocalMovePredictor(
                    new PredictionSettings(GameConstants.DefaultTickRate, 5f, MapBounds.Default));
            }

            public List<string> Log { get; } = new List<string>();

            public WorldViewBinder Binder { get; }

            public LocalMovePredictor Predictor { get; }

            public int Generations { get; private set; }

            public int BinderResets { get; private set; }

            public void Frame(bool inWorld, FakeSession session) =>
                this.gate.Tick(inWorld, session, this.Begin, () => this.Log.Add("feed"));

            public ReconnectOutcome Reconnected(FakeSession session) => this.gate.OnReconnected(session, this.Begin);

            private void Begin(FakeSession session)
            {
                this.Log.Add("begin");
                this.Generations++;
                this.Binder.Reset();
                this.BinderResets++;
                this.Predictor.Reset();
                this.seeded = false;
            }

            /// <summary>Records one input; the first Reconcile after a Reset seeds the predictor, as a snapshot does.</summary>
            public void RecordInput(long tick)
            {
                if (!this.seeded)
                {
                    this.Predictor.Reconcile(Vec2.Zero, 0);
                    this.seeded = true;
                }

                this.Predictor.RecordInput(tick, 1f, 0f);
            }

            public void Feed(WorldState world, long tick, int i)
            {
                var entities = new List<ResolvedEntity>
                {
                    new ResolvedEntity(Local, "player", 0f, 0f, 100, 100),
                    new ResolvedEntity(Remote, "player", i * 0.25f, 0f, 100, 100),
                };
                world.Apply(new ResolvedSnapshot(tick, 0, i == 0, entities, new List<string>()));
                this.clock.NowMs += 50.0;
                this.Binder.Tick(world, Local);
            }
        }

        private sealed class FakeSession
        {
        }

        private sealed class FakeClock : IViewClock
        {
            public double NowMs { get; set; }
        }

        private sealed class NullView : IEntityView
        {
            public void Spawn(string id, bool isLocal, string type)
            {
            }

            public void Despawn(string id)
            {
            }

            public void SetState(string id, float x, float y, int hp, int maxHp)
            {
            }
        }
    }
}
