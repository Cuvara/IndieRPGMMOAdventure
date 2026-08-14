using Cuvara.DOTS.Groups;
using Cuvara.DOTS.Simulation;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Cuvara.DOTS.Tests
{
    /// <summary>
    /// Drives the simulation systems against an isolated <see cref="World"/>.
    /// </summary>
    /// <remarks>
    /// Each test ticks <see cref="GameplaySystemGroup"/> rather than the world, so the assertions
    /// exercise the real group ordering — movement before lifecycle, command buffer last — instead
    /// of a hand-picked call order that could pass while the shipped ordering is wrong.
    /// </remarks>
    public sealed class SimulationSystemsTests
    {
        private World _world;
        private EntityManager _entityManager;
        private GameplaySystemGroup _gameplay;

        [SetUp]
        public void SetUp()
        {
            _world = new World("SimulationSystemsTests");
            _entityManager = _world.EntityManager;
            DotsSimulationBootstrap.InstallSimulationSystems(_world);
            _gameplay = _world.GetExistingSystemManaged<GameplaySystemGroup>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
            _world = null;
        }

        /// <summary>Advances simulation time by a fixed step and runs one gameplay frame.</summary>
        private void Tick(float deltaTime = 0.25f)
        {
            _world.SetTime(new Unity.Core.TimeData(_world.Time.ElapsedTime + deltaTime, deltaTime));
            _gameplay.Update();
        }

        private Entity CreateAt(float3 position)
        {
            var entity = _entityManager.CreateEntity();
            _entityManager.AddComponentData(entity, LocalTransform.FromPosition(position));
            return entity;
        }

        [Test]
        public void MoveToward_StepsTowardTarget_AndStopsWithoutOvershooting()
        {
            var entity = CreateAt(new float3(0f, 0f, 0f));
            _entityManager.AddComponentData(entity, new MoveToward
            {
                Target = new float3(1f, 0f, 0f),
                Speed = 100f,          // far more than the distance in one step
                StopDistance = 0f,
            });

            Tick();
            var position = _entityManager.GetComponentData<LocalTransform>(entity).Position;
            Assert.That(position.x, Is.EqualTo(1f).Within(1e-4f), "step must clamp to the remaining distance");

            Tick();
            position = _entityManager.GetComponentData<LocalTransform>(entity).Position;
            Assert.That(position.x, Is.EqualTo(1f).Within(1e-4f), "an arrived entity must not drift");
        }

        [Test]
        public void MoveToward_AtTarget_ProducesNoNaN()
        {
            // Normalising a zero vector is the failure this guards: it poisons the transform for
            // the rest of the session rather than producing a visible one-frame glitch.
            var entity = CreateAt(new float3(2f, 3f, 4f));
            _entityManager.AddComponentData(entity, new MoveToward
            {
                Target = new float3(2f, 3f, 4f),
                Speed = 5f,
                StopDistance = 0f,
            });

            Tick();

            var position = _entityManager.GetComponentData<LocalTransform>(entity).Position;
            Assert.That(math.any(math.isnan(position)), Is.False, "position must not become NaN");
        }

        [Test]
        public void MoveBounce_ReflectsVelocityAndClampsInsideBounds()
        {
            var entity = CreateAt(new float3(0.9f, 0f, 0f));
            _entityManager.AddComponentData(entity, new MoveData
            {
                Velocity = new float3(1f, 0f, 0f),
                BoundsMin = new float3(-1f, -1f, -1f),
                BoundsMax = new float3(1f, 1f, 1f),
            });

            Tick(1f); // would land at 1.9, outside the box

            var position = _entityManager.GetComponentData<LocalTransform>(entity).Position;
            var velocity = _entityManager.GetComponentData<MoveData>(entity).Velocity;

            Assert.That(position.x, Is.LessThanOrEqualTo(1f), "must be clamped back inside the bounds");
            Assert.That(velocity.x, Is.LessThan(0f), "velocity must have reflected");
        }

        [Test]
        public void Spin_RotatesWithoutMoving()
        {
            var entity = CreateAt(new float3(5f, 0f, 0f));
            _entityManager.AddComponentData(entity, new SpinSpeed { RadiansPerSecond = math.PI });

            Tick(1f);

            var transform = _entityManager.GetComponentData<LocalTransform>(entity);
            Assert.That(math.distance(transform.Position, new float3(5f, 0f, 0f)),
                Is.LessThan(1e-4f), "spin must not write position");
            Assert.That(math.abs(transform.Rotation.value.w), Is.LessThan(0.999f), "rotation must have changed");
        }

        [Test]
        public void TimeToLive_DestroysWhenExpired_ThroughThePackagesOwnCommandBuffer()
        {
            var entity = CreateAt(float3.zero);
            _entityManager.AddComponentData(entity, new TimeToLive { Remaining = 0.4f });

            Tick(); // 0.4 -> 0.15, still alive
            Assert.That(_entityManager.Exists(entity), Is.True);

            Tick(); // -> -0.1, destroyed at playback inside the same group
            Assert.That(_entityManager.Exists(entity), Is.False,
                "the buffer must play back before the gameplay group ends");
        }

        [Test]
        public void Health_DestroysAtZero_AndLeavesPositiveHealthAlone()
        {
            var dead = _entityManager.CreateEntity();
            _entityManager.AddComponentData(dead, new Health { Current = 0, Max = 10 });

            var alive = _entityManager.CreateEntity();
            _entityManager.AddComponentData(alive, new Health { Current = 1, Max = 10 });

            Tick();

            Assert.That(_entityManager.Exists(dead), Is.False);
            Assert.That(_entityManager.Exists(alive), Is.True);
        }

        [Test]
        public void Health_NeedsNoTagAndNoStatsSingleton()
        {
            // The lifted implementation filtered on a game-specific tag and wrote a stats singleton.
            // Neither exists here, and an entity carrying nothing but Health must still die.
            var entity = _entityManager.CreateEntity();
            _entityManager.AddComponentData(entity, new Health { Current = -5, Max = 10 });

            Tick();

            Assert.That(_entityManager.Exists(entity), Is.False);
        }

        [Test]
        public void InstallSimulationSystems_IsIdempotent()
        {
            DotsSimulationBootstrap.InstallSimulationSystems(_world);
            DotsSimulationBootstrap.InstallSimulationSystems(_world);

            var entity = CreateAt(float3.zero);
            _entityManager.AddComponentData(entity, new MoveToward
            {
                Target = new float3(10f, 0f, 0f),
                Speed = 1f,
                StopDistance = 0f,
            });

            Tick(1f);

            // One step, not three: a duplicated system in the group would move it further.
            var position = _entityManager.GetComponentData<LocalTransform>(entity).Position;
            Assert.That(position.x, Is.EqualTo(1f).Within(1e-4f));
        }
    }
}
