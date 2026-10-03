#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER && CUVARA_NETCODE && CUVARA_SHARED_GAMELOGIC
namespace Scripts.DI.Dots
{
    using System;
    using System.Collections.Generic;
    using Cuvara.Netcode.Prediction;
    using Cuvara.Netcode.World;
    using Scripts.Gameplay.Presentation;
    using Shared.GameLogic.Components;

    /// <summary>
    /// Fills the <see cref="EntityElevationTable"/> once per frame from the netcode world: the
    /// height of every mirror, and the whole extrapolated position of every projectile. Also the
    /// one place that walks the world for the owner's projectile handover (<c>spawn_seq</c>).
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description><b>Local player:</b> the predictor's height
    /// (<c>LocalMovePredictor.Position3.Z</c>) when it runs the 3D motor — the same body whose
    /// ground position the prediction system drew — else the snapshot's.</description></item>
    /// <item><description><b>Projectiles:</b> last reported position plus velocity times the time
    /// since that report, capped (<see cref="ProjectileExtrapolation"/>).</description></item>
    /// <item><description><b>Everything else:</b> snapshot heights per tick in a
    /// <see cref="HeightTrack"/>, evaluated at the binder's render tick, so height moves in step
    /// with the interpolated ground position.</description></item>
    /// </list>
    /// Against a protocol 2 server every <c>z</c> is 0 and this writes Unity <c>y = 0</c>, which
    /// is what the adapter wrote anyway.
    /// </remarks>
    public sealed class EntityElevationFeeder
    {
        private struct ProjectileTrack
        {
            public Vec3 Position;
            public Vec3 Velocity;
            public double ReceivedAt;
            public int Seen;
        }

        private struct RemoteTrack
        {
            public HeightTrack Heights;
            public int Seen;
        }

        private readonly EntityElevationTable table;
        private readonly Dictionary<string, RemoteTrack> remotes = new Dictionary<string, RemoteTrack>(StringComparer.Ordinal);
        private readonly Dictionary<string, ProjectileTrack> projectiles = new Dictionary<string, ProjectileTrack>(StringComparer.Ordinal);
        private readonly List<string> stale = new List<string>();
        private int frame;

        public EntityElevationFeeder(EntityElevationTable table)
        {
            this.table = table ?? throw new ArgumentNullException(nameof(table));
        }

        /// <summary>Projectile predictions handed over by the last <see cref="Feed"/>. Diagnostics.</summary>
        public int HandOversLastFeed { get; private set; }

        /// <summary>
        /// Rebuilds the table for this frame.
        /// </summary>
        /// <param name="world">The netcode client's merged world.</param>
        /// <param name="localId">The local player's entity id.</param>
        /// <param name="renderTick">The binder's interpolation render tick.</param>
        /// <param name="now">Seconds on a monotonic clock (realtime since startup).</param>
        /// <param name="predictor">The movement predictor, or null.</param>
        /// <param name="projectilePredictor">The local projectile predictor, or null.</param>
        public void Feed(
            WorldState world,
            string localId,
            double renderTick,
            double now,
            LocalMovePredictor predictor,
            ProjectilePredictor projectilePredictor)
        {
            this.table.Clear();
            this.HandOversLastFeed = 0;
            if (world == null) return;

            this.frame++;
            var worldTick = world.Tick;

            foreach (var pair in world.EntityMap)
            {
                var id = pair.Key;
                var e = pair.Value;

                // The server sends spawn_seq only to the owner, so any entity carrying one is a
                // projectile this client fired; the prediction gives way to it.
                if (e.SpawnSeq != 0u && projectilePredictor != null && projectilePredictor.TryHandOver(e.SpawnSeq, id))
                {
                    this.HandOversLastFeed++;
                }

                if (string.Equals(id, localId, StringComparison.Ordinal))
                {
                    var z = predictor != null && predictor.UsesCharacterMotor ? predictor.Position3.Z : e.Z;
                    this.table.Set(id, new EntityElevation { Height = z });
                    continue;
                }

                if (string.Equals(e.Type, NearestEntity.ProjectileType, StringComparison.Ordinal))
                {
                    var position = new Vec3(e.X, e.Y, e.Z);
                    var velocity = new Vec3(e.VelX, e.VelY, e.VelZ);
                    if (!this.projectiles.TryGetValue(id, out var track) || !track.Position.Equals(position) || !track.Velocity.Equals(velocity))
                    {
                        track.Position = position;
                        track.Velocity = velocity;
                        track.ReceivedAt = now;
                    }

                    track.Seen = this.frame;
                    this.projectiles[id] = track;

                    var drawn = ProjectileExtrapolation.Evaluate(track.Position, track.Velocity, (float)(now - track.ReceivedAt));
                    this.table.Set(id, new EntityElevation { Height = drawn.Z, OverridesPlane = true, X = drawn.X, Y = drawn.Y });
                    continue;
                }

                this.remotes.TryGetValue(id, out var remote);
                remote.Heights.Push(worldTick, e.Z);
                remote.Seen = this.frame;
                this.remotes[id] = remote;
                this.table.Set(id, new EntityElevation { Height = remote.Heights.Evaluate(renderTick) });
            }

            this.Sweep();
        }

        /// <summary>Forgets every track (a reconnect: the new session's ticks are a new timeline).</summary>
        public void Reset()
        {
            this.remotes.Clear();
            this.projectiles.Clear();
            this.table.Clear();
        }

        private void Sweep()
        {
            this.stale.Clear();
            foreach (var pair in this.remotes)
            {
                if (pair.Value.Seen != this.frame) this.stale.Add(pair.Key);
            }

            foreach (var id in this.stale) this.remotes.Remove(id);

            this.stale.Clear();
            foreach (var pair in this.projectiles)
            {
                if (pair.Value.Seen != this.frame) this.stale.Add(pair.Key);
            }

            foreach (var id in this.stale) this.projectiles.Remove(id);
        }
    }
}
#endif
