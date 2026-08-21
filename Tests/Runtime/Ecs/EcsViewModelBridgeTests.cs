namespace Cuvara.UIToolkit.Ecs.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Cuvara.UIToolkit.Ecs;
    using NUnit.Framework;
    using Unity.Entities;

    /// <summary>The component the simulation writes. Unmanaged, as an IComponentData must be.</summary>
    public struct TestHudData : IComponentData
    {
        public int   Health;
        public int   MaxHealth;
        public float Mana;
    }

    /// <summary>The plain value that crosses the boundary. No VisualElement anywhere in it.</summary>
    public readonly struct TestHudViewModel : IEquatable<TestHudViewModel>
    {
        public readonly int    Health;
        public readonly int    MaxHealth;
        public readonly string Caption;

        public TestHudViewModel(int health, int maxHealth, string caption)
        {
            this.Health    = health;
            this.MaxHealth = maxHealth;
            this.Caption   = caption;
        }

        public bool Equals(TestHudViewModel other) =>
            this.Health == other.Health && this.MaxHealth == other.MaxHealth && this.Caption == other.Caption;

        public override bool Equals(object obj) => obj is TestHudViewModel other && this.Equals(other);

        public override int GetHashCode() => (this.Health, this.MaxHealth, this.Caption).GetHashCode();
    }

    /// <summary>A concrete bridge, which is what a host writes.</summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class TestHudBridge : EcsViewModelBridge<TestHudData, TestHudViewModel>
    {
        protected override TestHudViewModel Convert(in TestHudData component)
        {
            return new(component.Health, component.MaxHealth, $"{component.Health}/{component.MaxHealth}");
        }
    }

    /// <summary>A bridge that also deduplicates by value, to exercise the optional guard.</summary>
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class ValueDedupedHudBridge : EcsViewModelBridge<TestHudData, TestHudViewModel>
    {
        protected override TestHudViewModel Convert(in TestHudData component)
        {
            return new(component.Health, component.MaxHealth, $"{component.Health}/{component.MaxHealth}");
        }

        protected override bool HasChanged(in TestHudViewModel previous, in TestHudViewModel current)
        {
            return !previous.Equals(current);
        }
    }

    /// <summary>
    /// A bridge carrying NO <c>[UpdateInGroup]</c> of its own.
    /// </summary>
    /// <remarks>
    /// This is the type the inheritance test needs and the reason it is separate from
    /// <see cref="TestHudBridge"/>, which declares the attribute explicitly: a test using an
    /// attributed subclass would pass whether or not inheritance works, and would therefore
    /// prove nothing about the case that matters — a host that subclasses the base and
    /// reasonably assumes placement comes with it.
    /// </remarks>
    public partial class UnattributedHudBridge : EcsViewModelBridge<TestHudData, TestHudViewModel>
    {
        protected override TestHudViewModel Convert(in TestHudData component)
        {
            return new(component.Health, component.MaxHealth, $"{component.Health}/{component.MaxHealth}");
        }
    }

    /// <summary>A sink that records what it was handed. What a Presenter is, minus the Presenter.</summary>
    public sealed class RecordingSink : IViewModelSink<TestHudViewModel>
    {
        public readonly List<TestHudViewModel> Received = new();

        public void Push(in TestHudViewModel viewModel) { this.Received.Add(viewModel); }
    }

    /// <summary>
    /// The ECS-to-ViewModel adapter, tested with a real <see cref="World"/> and no device,
    /// no scene, no panel and no <c>VisualElement</c> in sight.
    /// </summary>
    /// <remarks>
    /// <para>That last part is the point rather than a convenience. The project's UI
    /// architecture contract routes ECS through
    /// <c>adapter -&gt; Presenter -&gt; View -&gt; UI Toolkit</c>, and if this layer were
    /// testable only with a live panel it would be evidence that the adapter had reached
    /// past the ViewModel. It cannot, so it is not.</para>
    ///
    /// <para>The tests that matter most here are the negative ones: an adapter that pushes
    /// every frame passes every "did the sink get the data" test and still violates the
    /// contract's performance rule.</para>
    /// </remarks>
    public class EcsViewModelBridgeTests
    {
        private World world;

        [SetUp]
        public void SetUp()
        {
            this.world = new("EcsViewModelBridgeTests");
        }

        [TearDown]
        public void TearDown()
        {
            if (this.world is { IsCreated: true }) this.world.Dispose();
            this.world = null;
        }

        private TestHudBridge CreateBridge() => this.world.CreateSystemManaged<TestHudBridge>();

        private Entity CreateHud(int health = 50, int maxHealth = 100, float mana = 10f)
        {
            var entity = this.world.EntityManager.CreateEntity(typeof(TestHudData));
            this.world.EntityManager.SetComponentData(entity, new TestHudData { Health = health, MaxHealth = maxHealth, Mana = mana });
            return entity;
        }

        #region It delivers

        [Test]
        public void TickingWithASink_PushesTheConvertedViewModel()
        {
            var bridge = this.CreateBridge();
            var sink   = new RecordingSink();

            this.CreateHud(30, 120);
            bridge.AddSink(sink);

            bridge.Update();

            Assert.That(sink.Received, Has.Count.EqualTo(1));
            Assert.That(sink.Received[0].Health, Is.EqualTo(30));
            Assert.That(sink.Received[0].MaxHealth, Is.EqualTo(120));
            Assert.That(sink.Received[0].Caption, Is.EqualTo("30/120"), "Convert must be what produces the ViewModel");
        }

        [Test]
        public void ChangingTheComponent_PushesAgain()
        {
            var bridge = this.CreateBridge();
            var sink   = new RecordingSink();
            var entity = this.CreateHud(30, 120);

            bridge.AddSink(sink);
            bridge.Update();
            Assert.That(sink.Received, Has.Count.EqualTo(1), "precondition");

            this.world.EntityManager.SetComponentData(entity, new TestHudData { Health = 25, MaxHealth = 120 });
            bridge.Update();

            Assert.That(sink.Received, Has.Count.EqualTo(2));
            Assert.That(sink.Received[1].Health, Is.EqualTo(25));
        }

        [Test]
        public void EverySinkReceivesEveryPush()
        {
            var bridge = this.CreateBridge();
            var first  = new RecordingSink();
            var second = new RecordingSink();

            this.CreateHud();
            bridge.AddSink(first);
            bridge.AddSink(second);

            bridge.Update();

            Assert.That(first.Received, Has.Count.EqualTo(1));
            Assert.That(second.Received, Has.Count.EqualTo(1));
        }

        #endregion

        #region It stays quiet — the half that protects the design

        [Test]
        public void TickingTwiceWithoutTouchingTheData_PushesOnlyOnce()
        {
            // The rule the architecture contract states as "update on data change, not per
            // frame". A bridge that re-pushed here would pass every delivery test above and
            // still be the thing that makes UI Toolkit look slow.
            var bridge = this.CreateBridge();
            var sink   = new RecordingSink();

            this.CreateHud();
            bridge.AddSink(sink);

            bridge.Update();
            bridge.Update();
            bridge.Update();

            Assert.That(sink.Received, Has.Count.EqualTo(1), "the change filter must skip chunks nothing has written");
            Assert.That(bridge.PushCount, Is.EqualTo(1));
        }

        [Test]
        public void WithNoSinks_TheSystemIsDisabled()
        {
            // The cheapest idle cost available: a disabled system's OnUpdate is not called,
            // so a world with no screen open does not even evaluate the query.
            var bridge = this.CreateBridge();

            this.CreateHud();

            Assert.That(bridge.Enabled, Is.False);
        }

        [Test]
        public void AddingTheFirstSinkEnablesIt_AndRemovingTheLastDisablesIt()
        {
            var bridge = this.CreateBridge();
            var sink   = new RecordingSink();

            bridge.AddSink(sink);
            Assert.That(bridge.Enabled, Is.True);

            bridge.RemoveSink(sink);
            Assert.That(bridge.Enabled, Is.False);
        }

        [Test]
        public void ARemovedSinkReceivesNothingFurther()
        {
            var bridge = this.CreateBridge();
            var sink   = new RecordingSink();
            var entity = this.CreateHud();

            bridge.AddSink(sink);
            bridge.Update();
            var countWhileRegistered = sink.Received.Count;

            bridge.RemoveSink(sink);
            this.world.EntityManager.SetComponentData(entity, new TestHudData { Health = 1, MaxHealth = 100 });
            bridge.Update();

            Assert.That(sink.Received, Has.Count.EqualTo(countWhileRegistered));
        }

        [Test]
        public void TickingWithNoEntities_PushesNothingAndDoesNotThrow()
        {
            var bridge = this.CreateBridge();
            var sink   = new RecordingSink();

            bridge.AddSink(sink);

            Assert.DoesNotThrow(() => bridge.Update());
            Assert.That(sink.Received, Is.Empty);
        }

        [Test]
        public void AddingTheSameSinkTwice_DoesNotDoublePush()
        {
            var bridge = this.CreateBridge();
            var sink   = new RecordingSink();

            this.CreateHud();
            bridge.AddSink(sink);
            bridge.AddSink(sink);

            bridge.Update();

            Assert.That(sink.Received, Has.Count.EqualTo(1));
        }

        [Test]
        public void ASinkAddedLate_GetsTheCurrentStateWithoutWaitingForAWrite()
        {
            // A screen opened while the simulation is idle would otherwise show nothing: the
            // chunk has not been written since the bridge last ran, so the change filter
            // correctly skips it, and the new sink would sit empty until something happened.
            var bridge = this.CreateBridge();
            var first  = new RecordingSink();

            this.CreateHud(77, 100);
            bridge.AddSink(first);
            bridge.Update();

            var late = new RecordingSink();
            bridge.AddSink(late);
            bridge.Update();

            Assert.That(late.Received, Has.Count.EqualTo(1), "a late sink must be caught up");
            Assert.That(late.Received[0].Health, Is.EqualTo(77));
        }

        [Test]
        public void AddingANullSink_Throws()
        {
            var bridge = this.CreateBridge();

            Assert.Throws<ArgumentNullException>(() => bridge.AddSink(null));
        }

        [Test]
        public void RemovingANullOrUnknownSink_DoesNothing()
        {
            var bridge = this.CreateBridge();

            Assert.DoesNotThrow(() => bridge.RemoveSink(null));
            Assert.DoesNotThrow(() => bridge.RemoveSink(new RecordingSink()));
        }

        #endregion

        #region The optional value-level guard

        [Test]
        public void WithValueDeduplication_AWriteOfAnIdenticalValuePushesNothing()
        {
            // The chunk-granular filter is conservative: writing the same value still marks
            // the chunk changed. HasChanged is the opt-in second line of defence, and this is
            // the case that distinguishes the two mechanisms.
            var bridge = this.world.CreateSystemManaged<ValueDedupedHudBridge>();
            var sink   = new RecordingSink();
            var entity = this.CreateHud(40, 100);

            bridge.AddSink(sink);
            bridge.Update();
            Assert.That(sink.Received, Has.Count.EqualTo(1), "precondition");

            this.world.EntityManager.SetComponentData(entity, new TestHudData { Health = 40, MaxHealth = 100 });
            bridge.Update();

            Assert.That(sink.Received, Has.Count.EqualTo(1), "an identical value must not reach the sink");
        }

        [Test]
        public void WithValueDeduplication_ARealChangeStillPushes()
        {
            var bridge = this.world.CreateSystemManaged<ValueDedupedHudBridge>();
            var sink   = new RecordingSink();
            var entity = this.CreateHud(40, 100);

            bridge.AddSink(sink);
            bridge.Update();

            this.world.EntityManager.SetComponentData(entity, new TestHudData { Health = 39, MaxHealth = 100 });
            bridge.Update();

            Assert.That(sink.Received, Has.Count.EqualTo(2));
            Assert.That(sink.Received[1].Health, Is.EqualTo(39));
        }

        #endregion

        #region Registration lifetime

        [Test]
        public void ARegistrationAddsTheSink_AndDisposingRemovesIt()
        {
            // The leak this prevents is quiet: a sink keeps the Presenter alive, which keeps
            // the View alive, which keeps the visual tree alive, long after the screen closed.
            var bridge = this.CreateBridge();
            var sink   = new RecordingSink();

            var registration = EcsSinkRegistration.Bind(bridge, sink);
            Assert.That(bridge.Sinks, Has.Member(sink));

            registration.Dispose();
            Assert.That(bridge.Sinks, Has.No.Member(sink));
            Assert.That(bridge.Enabled, Is.False);
        }

        [Test]
        public void DisposingARegistrationTwice_IsHarmless()
        {
            var bridge       = this.CreateBridge();
            var registration = EcsSinkRegistration.Bind(bridge, new RecordingSink());

            Assert.DoesNotThrow(() =>
            {
                registration.Dispose();
                registration.Dispose();
            });
        }

        [Test]
        public void ARegistrationOverNullArguments_Throws()
        {
            var bridge = this.CreateBridge();

            Assert.Throws<ArgumentNullException>(() => EcsSinkRegistration.Bind<TestHudData, TestHudViewModel>(null, new RecordingSink()));
            Assert.Throws<ArgumentNullException>(() => EcsSinkRegistration.Bind(bridge, null));
        }

        #endregion

        #region System-group placement

        [Test]
        public void AnUnattributedSubclass_IsStillPlacedInPresentationSystemGroup()
        {
            // The question this settles: does [UpdateInGroup(typeof(PresentationSystemGroup))]
            // on the abstract base reach a concrete subclass that does not repeat it?
            //
            // It matters more than a placement detail. If it does not inherit, a host bridge
            // is created but never added to any group, so it never updates: the screen simply
            // stays blank, nothing throws, and nothing in a log says why. That is the worst
            // failure shape available — silent, and indistinguishable from "the simulation is
            // not writing the component".
            //
            // AddSystemsToRootLevelSystemGroups is the function Unity's own bootstrap uses to
            // read the attribute and place systems, so this exercises the real path rather
            // than a reimplementation of it.
            using var attributeWorld = new World("UpdateInGroupInheritance");

            DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(
                attributeWorld,
                typeof(PresentationSystemGroup),
                typeof(UnattributedHudBridge));

            var group  = attributeWorld.GetExistingSystemManaged<PresentationSystemGroup>();
            var bridge = attributeWorld.GetExistingSystemManaged<UnattributedHudBridge>();

            Assert.That(group, Is.Not.Null, "the presentation group was not created");
            Assert.That(bridge, Is.Not.Null, "the bridge system was not created");

            var sink = new RecordingSink();
            bridge.AddSink(sink);

            var entity = attributeWorld.EntityManager.CreateEntity(typeof(TestHudData));
            attributeWorld.EntityManager.SetComponentData(entity, new TestHudData { Health = 12, MaxHealth = 20 });

            // Running the GROUP, not the system: if placement did not happen, this updates
            // nothing and the sink stays empty.
            group.Update();

            Assert.That(sink.Received, Has.Count.EqualTo(1),
                "the bridge did not run inside PresentationSystemGroup — [UpdateInGroup] did not inherit onto the subclass, "
                + "so every host bridge would be created and never updated.");
            Assert.That(sink.Received[0].Health, Is.EqualTo(12));
        }

        #endregion

        #region The layering itself

        [Test]
        public void NothingInTheEcsAssemblyReferencesUIElements()
        {
            // The architecture contract's ECS boundary, asserted rather than asserted-about.
            // A future edit that reaches for a VisualElement to "just set the label here"
            // fails at this line rather than in review, or not at all.
            var assembly = typeof(EcsViewModelBridge<,>).Assembly;

            var offenders = assembly
                .GetReferencedAssemblies()
                .Where(name => name.Name.Contains("UIElements"))
                .Select(name => name.Name)
                .ToList();

            Assert.That(offenders, Is.Empty,
                $"{assembly.GetName().Name} must not reference UI Toolkit at all — ECS reaches UI through a ViewModel and a Presenter, never directly. Found: {string.Join(", ", offenders)}");
        }

        [Test]
        public void TheSinkContractCarriesNothingButData()
        {
            // A ViewModel that could hold a VisualElement would make the boundary decorative.
            var push = typeof(IViewModelSink<>).GetMethods().Single(m => m.Name == "Push");

            Assert.That(push.GetParameters(), Has.Length.EqualTo(1));
            Assert.That(push.ReturnType, Is.EqualTo(typeof(void)), "a sink reports nothing back; the flow is one-way");
        }

        #endregion
    }
}
