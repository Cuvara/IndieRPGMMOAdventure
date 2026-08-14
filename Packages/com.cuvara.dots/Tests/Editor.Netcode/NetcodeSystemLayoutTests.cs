using System;
using System.Linq;
using Cuvara.DOTS.Groups;
using Cuvara.DOTS.Netcode;
using NUnit.Framework;
using Unity.Entities;

namespace Cuvara.DOTS.Tests.Netcode
{
    /// <summary>
    /// The adapter's placement in the frame, asserted by reflection for the same reason
    /// <c>ViewSystemGroupLayoutTests</c> does it: a wrong <c>[UpdateInGroup]</c> compiles, does not
    /// throw, and shows up only as views trailing the wire by a frame.
    /// </summary>
    public sealed class NetcodeSystemLayoutTests
    {
        private static readonly Type DrainSystem = typeof(NetworkViewCommandSystem);

        [Test]
        public void Drain_IsInTheNetcodeGroup_WhichIsInInitialization()
        {
            // This is the whole "same frame" argument, and it is two hops: the drain is in
            // NetcodeSystemGroup, and NetcodeSystemGroup is in InitializationSystemGroup — which runs
            // before SimulationSystemGroup (and so before TransformSystemGroup) and long before
            // PresentationSystemGroup, where ViewSystemGroup lives.
            // Three hops since 0.13.0, not two: the drain moved into SnapshotApplyGroup so that
            // prediction could order itself after it without naming a system. What this test
            // protects is the containment chain, which is unchanged — everything still lands in
            // InitializationSystemGroup, before this frame's transforms and views.
            var group = DrainSystem.GetCustomAttributes(typeof(UpdateInGroupAttribute), false)
                .Cast<UpdateInGroupAttribute>()
                .Single();

            Assert.AreEqual(typeof(SnapshotApplyGroup), group.GroupType);
            Assert.AreEqual(typeof(NetcodeSystemGroup),
                typeof(SnapshotApplyGroup).GetCustomAttributes(typeof(UpdateInGroupAttribute), false)
                    .Cast<UpdateInGroupAttribute>().Single().GroupType);
            Assert.AreEqual(typeof(InitializationSystemGroup),
                typeof(NetcodeSystemGroup).GetCustomAttributes(typeof(UpdateInGroupAttribute), false)
                    .Cast<UpdateInGroupAttribute>().Single().GroupType);
        }

        [Test]
        public void Drain_IsNotAutoCreated()
        {
            // Unity's default bootstrap creates every non-disabled system in EVERY world. A second
            // drain in a second world would race this one for the same queue and each would get
            // half the commands.
            Assert.IsNotEmpty(DrainSystem.GetCustomAttributes(typeof(DisableAutoCreationAttribute), false));
        }

        [Test]
        public void Drain_IsInternal_AndTheGroupIsThePublicContract()
        {
            Assert.IsFalse(DrainSystem.IsPublic, "a public system name is an accidental API promise");
            Assert.IsTrue(typeof(NetcodeSystemGroup).IsPublic);
        }

        [Test]
        public void PublicSurface_DoesNotCollideWithTheVocabularyOfTheDependencies()
        {
            // A previous type in this package's history was named Lifetime and broke an assembly its
            // author could not touch, invisibly from the declaring side. These are the simple names
            // this assembly adds; the check is that none of them is a word VContainer (Lifetime,
            // Scope), MessagePipe (IPublisher, ISubscriber), UniT (IAssetsManager) or Entities
            // (World, SystemState, Entity) already owns.
            var reserved = new[]
            {
                "Lifetime", "Scope", "LifetimeScope",
                "IPublisher", "ISubscriber",
                "IAssetsManager", "IObjectPoolManager",
                "World", "SystemState", "Entity", "EntityManager", "Health",
            };

            var declared = DrainSystem.Assembly.GetTypes()
                .Where(t => t.IsPublic || t.IsNestedPublic)
                .Select(t => t.Name)
                .ToArray();

            CollectionAssert.IsNotEmpty(declared);
            foreach (var name in declared)
            {
                CollectionAssert.DoesNotContain(reserved, name, $"'{name}' is a word a supported dependency already owns");
            }
        }
    }
}
