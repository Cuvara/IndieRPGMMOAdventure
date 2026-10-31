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
    /// The session-start reset of <c>DotsWorldBridge.OnSessionBegan</c>. The bridge needs an
    /// installed ECS world and a live <c>NetworkClient</c>, so these tests drive the decision
    /// (<see cref="SessionBoundary"/>) and the pieces the reset calls (<see cref="WorldViewBinder"/>,
    /// <see cref="LocalMovePredictor"/>) one level down, with a stand-in for the bridge that runs
    /// the same sequence: generation, binder, predictor. The view generation itself
    /// (<c>DotsEntityView.BeginGeneration</c>) is counted by the stand-in, not exercised.
    /// </summary>
    public sealed class SessionBoundaryTests
    {
        private const string Local = "local-1";
        private const string Remote = "remote-1";

        // ---- (a) a non-reconnect new session -------------------------------------------------

        [Test]
        public void NewSessionObject_BeginsExactlyOnce_NoMatterHowOftenItIsObserved()
        {
            var bridge = new BridgeStandIn();
            var session = new object();

            for (int frame = 0; frame < 5; frame++)
            {
                bridge.Observe(session);
            }

            Assert.That(bridge.Generations, Is.EqualTo(1));
            Assert.That(bridge.BinderResets, Is.EqualTo(1));
        }

        [Test]
        public void RetryOrDungeonJoin_NewObject_ChangesGenerationAndResetsBinderAndPredictor()
        {
            var bridge = new BridgeStandIn();
            bridge.Observe(new object());
            bridge.Predictor.RecordInput(1, 1f, 0f);
            Assert.That(bridge.Predictor.PendingCount, Is.EqualTo(1), "precondition");

            bridge.Observe(new object());

            Assert.That(bridge.Generations, Is.EqualTo(2));
            Assert.That(bridge.BinderResets, Is.EqualTo(2));
            Assert.That(bridge.Predictor.PendingCount, Is.EqualTo(0), "the old input backlog is gone");
        }

        [Test]
        public void NullSession_NeverBegins()
        {
            var bridge = new BridgeStandIn();

            bridge.Observe(null);

            Assert.That(bridge.Generations, Is.EqualTo(0));
        }

        // ---- (b) reconnect: the event and the identity check both see the same session -------

        [Test]
        public void Reconnect_SeenByEventThenByFrameCheck_IsOneGeneration()
        {
            var bridge = new BridgeStandIn();
            bridge.Observe(new object());
            var reconnected = new object();

            bridge.Observe(reconnected); // the Reconnected event
            bridge.Observe(reconnected); // the next frame's identity check

            Assert.That(bridge.Generations, Is.EqualTo(2), "one for the first session, one for the reconnect");
        }

        [Test]
        public void Reconnect_SeenByFrameCheckThenByEvent_IsOneGeneration()
        {
            var bridge = new BridgeStandIn();
            bridge.Observe(new object());
            var reconnected = new object();

            bridge.Observe(reconnected); // the frame's identity check
            bridge.Observe(reconnected); // the Reconnected event

            Assert.That(bridge.Generations, Is.EqualTo(2));
        }

        // ---- (c) B's scenario: the new session's ticks start ABOVE the old ones --------------

        [Test]
        public void NewSessionStartingAboveTheOldTicks_CarriesNoBinderClockOrPredictorHistory()
        {
            var bridge = new BridgeStandIn();
            var world = new WorldState();
            bridge.Observe(new object());
            for (int i = 0; i < 100; i++)
            {
                bridge.Feed(world, 100 + i, i);
                bridge.Predictor.RecordInput(i + 1, 1f, 0f);
            }

            Assert.That(bridge.Binder.RenderTick, Is.GreaterThan(150.0), "precondition: session A interpolates");
            Assert.That(bridge.Predictor.PendingCount, Is.GreaterThan(0), "precondition: A left queued inputs");

            // Session B: NetworkClient resets the world; the bridge sees the new object before it feeds.
            world.Reset();
            bridge.Observe(new object());

            Assert.That(bridge.Predictor.PendingCount, Is.EqualTo(0), "stale queued commands dropped");
            for (int i = 0; i < 20; i++)
            {
                bridge.Feed(world, 500 + i, i);
            }

            Assert.That(bridge.Binder.RenderTick, Is.GreaterThan(500.0).And.LessThan(520.0),
                "the render clock is rebuilt in the new session's tick space, not A's");
        }

        /// <summary>The decision and the reset sequence of <c>DotsWorldBridge</c>, minus the ECS world.</summary>
        private sealed class BridgeStandIn
        {
            private readonly SessionBoundary boundary = new SessionBoundary();
            private readonly FakeClock clock = new FakeClock();

            public BridgeStandIn()
            {
                this.Binder = new WorldViewBinder(new NullView(), null, this.clock);
                this.Predictor = new LocalMovePredictor(
                    new PredictionSettings(GameConstants.DefaultTickRate, 5f, MapBounds.Default));
            }

            public WorldViewBinder Binder { get; }

            public LocalMovePredictor Predictor { get; }

            public int Generations { get; private set; }

            public int BinderResets { get; private set; }

            public void Observe(object session)
            {
                if (!this.boundary.TryBegin(session))
                {
                    return;
                }

                this.Generations++;
                this.Binder.Reset();
                this.BinderResets++;
                this.Predictor.Reset();
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
