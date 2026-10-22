using System.Collections.Generic;
using Cuvara.Netcode.Snapshot;
using Cuvara.Netcode.View;
using Cuvara.Netcode.World;
using NUnit.Framework;

namespace Tests.Editor
{
    /// <summary>
    /// Why <c>DotsWorldBridge.OnReconnected</c> resets the <see cref="WorldViewBinder"/>: after a
    /// reconnect to a server whose ticks are lower than the old session's, a binder that was not
    /// reset never buffers a sample and its <see cref="WorldViewBinder.RenderTick"/> stays in the
    /// old tick space. The bridge itself needs an installed ECS world and a live
    /// <c>NetworkClient</c> to raise <c>Reconnected</c>, so this fixture drives the same sequence
    /// one level down: <c>WorldState.Reset</c> (what <c>NetworkClient</c> does on reconnect), then
    /// the new session's snapshots, with and without <c>binder.Reset()</c>.
    /// </summary>
    /// <remarks>
    /// Netcode after 0.47.0 also defends itself: <c>WorldViewBinder</c> treats a world tick below
    /// its last one as an unreported session boundary and restarts its tick line, counting it in
    /// <c>TickLineRestarts</c>. The control therefore asserts the behaviour of whichever netcode is
    /// compiled in (frozen clock on 0.47.0, a counted restart after), and the reset test asserts
    /// that the bridge's explicit reset leaves that fallback unused.
    /// </remarks>
    public sealed class ReconnectBinderResetTests
    {
        private const string Local = "local-1";
        private const string Remote = "remote-1";
        private const double FrameMs = 50.0;

        [Test]
        public void ReconnectToLowerTicks_WithoutReset_RenderTickStaysInTheOldSession()
        {
            var (binder, world, clock) = Session(oldStartTick: 10_000);

            Reconnect(world);
            Feed(binder, world, clock, firstTick: 100, count: 20);

            if (TickLineRestarts(binder) is int restarts)
            {
                // Netcode with the binder-side fallback: it caught the unreported boundary itself.
                Assert.That(restarts, Is.EqualTo(1), "the binder restarts its tick line once");
                Assert.That(binder.RenderTick, Is.GreaterThan(100.0).And.LessThan(120.0));
            }
            else
            {
                // Netcode 0.47.0, no fallback: the defect the bridge's Reset call removes.
                Assert.That(binder.RenderTick, Is.GreaterThan(9_000.0),
                    "a binder that is not reset keeps rendering in the old session's tick space");
            }
        }

        [Test]
        public void ReconnectToLowerTicks_WithReset_RenderTickFollowsTheNewSession()
        {
            var (binder, world, clock) = Session(oldStartTick: 10_000);

            Reconnect(world);
            binder.Reset();
            Feed(binder, world, clock, firstTick: 100, count: 20);

            Assert.That(binder.RenderTick, Is.GreaterThan(100.0).And.LessThan(120.0),
                "after Reset the render clock is rebuilt from the new session's ticks");
            if (TickLineRestarts(binder) is int restarts)
            {
                Assert.That(restarts, Is.EqualTo(0), "an explicit Reset leaves the binder's fallback unused");
            }
        }

        /// <summary><c>WorldViewBinder.TickLineRestarts</c>, or null on a netcode without it (0.47.0).</summary>
        private static int? TickLineRestarts(WorldViewBinder binder) =>
            typeof(WorldViewBinder).GetProperty("TickLineRestarts")?.GetValue(binder) as int?;

        private static (WorldViewBinder, WorldState, FakeClock) Session(long oldStartTick)
        {
            var clock = new FakeClock();
            var binder = new WorldViewBinder(new NullView(), null, clock);
            var world = new WorldState();
            Feed(binder, world, clock, oldStartTick, count: 40);
            Assert.That(binder.RenderTick, Is.GreaterThan(oldStartTick - 10.0), "precondition: the old session interpolates");
            return (binder, world, clock);
        }

        private static void Reconnect(WorldState world) => world.Reset();

        private static void Feed(WorldViewBinder binder, WorldState world, FakeClock clock, long firstTick, int count)
        {
            for (int i = 0; i < count; i++)
            {
                long tick = firstTick + i;
                var entities = new List<ResolvedEntity>
                {
                    new ResolvedEntity(Local, "player", 0f, 0f, 100, 100),
                    new ResolvedEntity(Remote, "player", i * 0.25f, 0f, 100, 100),
                };
                world.Apply(new ResolvedSnapshot(tick, 0, i == 0, entities, new List<string>()));
                clock.NowMs += FrameMs;
                binder.Tick(world, Local);
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
