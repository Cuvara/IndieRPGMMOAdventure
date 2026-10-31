namespace Tests.Editor
{
    using NUnit.Framework;
    using Scripts.DI;
    using UnityEngine;
    using VContainer;

    /// <summary>The router is the single holder of the gameplay input source: devices, override, clear.</summary>
    [TestFixture]
    public sealed class GameplayInputRouterTests
    {
        private sealed class FixedSource : IGameplayInputSource
        {
            public void ReadMove(out float x, out float y)
            {
                x = 0.5f;
                y = -0.25f;
            }

            public bool JumpPressed() => true;

            public bool CastPressed() => true;

            public bool PickupPressed() => true;

            public bool InventoryTogglePressed() => true;

            public bool TryPointerPosition(out Vector2 position)
            {
                position = new Vector2(3f, 4f);
                return true;
            }

            public bool TryAimPoint(Vector3 feet, out Vector3 aim)
            {
                aim = feet + Vector3.one;
                return true;
            }
        }

        [Test]
        public void ByDefault_ForwardsToTheDevices()
        {
            var router = new GameplayInputRouter();
            Assert.That(router.IsOverridden, Is.False);
            Assert.That(router.Active, Is.SameAs(DeviceGameplayInputSource.Instance));
            Assert.That(router.TryAimPoint(Vector3.zero, out _), Is.False);
        }

        [Test]
        public void Override_RoutesEveryRead_AndClearRestoresTheDevices()
        {
            var router = new GameplayInputRouter();
            router.Override(new FixedSource());

            Assert.That(router.IsOverridden, Is.True);
            router.ReadMove(out var x, out var y);
            Assert.That(x, Is.EqualTo(0.5f));
            Assert.That(y, Is.EqualTo(-0.25f));
            Assert.That(router.JumpPressed(), Is.True);
            Assert.That(router.CastPressed(), Is.True);
            Assert.That(router.PickupPressed(), Is.True);
            Assert.That(router.InventoryTogglePressed(), Is.True);
            Assert.That(router.TryPointerPosition(out var pointer), Is.True);
            Assert.That(pointer, Is.EqualTo(new Vector2(3f, 4f)));
            Assert.That(router.TryAimPoint(Vector3.zero, out var aim), Is.True);
            Assert.That(aim, Is.EqualTo(Vector3.one));

            router.ClearOverride();
            Assert.That(router.IsOverridden, Is.False);
            Assert.That(router.TryAimPoint(Vector3.zero, out _), Is.False);
        }

        [Test]
        public void Override_RejectsNull()
        {
            Assert.Throws<System.ArgumentNullException>(() => new GameplayInputRouter().Override(null));
        }

        [Test]
        public void Container_ResolvesOneRouter_ForBothServiceTypes()
        {
            var builder = new ContainerBuilder();
            builder.Register<GameplayInputRouter>(Lifetime.Singleton).As<IGameplayInputSource>().AsSelf();
            using var container = builder.Build();

            Assert.That(container.Resolve<IGameplayInputSource>(), Is.SameAs(container.Resolve<GameplayInputRouter>()));
        }

#pragma warning disable CS0618 // the obsolete forwarder is exactly what is under test
        [Test]
        public void ObsoleteStaticSource_ForwardsToTheLiveRouter()
        {
            var router = new GameplayInputRouter();
            var scripted = new FixedSource();

            GameplayInput.Source = scripted;
            Assert.That(router.Active, Is.SameAs(scripted));
            Assert.That(GameplayInput.Source, Is.SameAs(router));

            GameplayInput.Source = null;
            Assert.That(router.IsOverridden, Is.False);

            router.Dispose();
            Assert.That(GameplayInput.Source, Is.SameAs(DeviceGameplayInputSource.Instance));
        }
#pragma warning restore CS0618
    }
}
