using System;
using System.Text.RegularExpressions;
using Cuvara.DOTS.Netcode;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cuvara.DOTS.Tests.Netcode
{
    /// <summary>
    /// The resolver that replaced <c>PrefixArchetypeResolver</c>: it reads the kind the server sent
    /// instead of inferring one from how an id is spelled.
    /// </summary>
    public sealed class TypeArchetypeResolverTests
    {
        private static readonly TypeArchetypeResolver.Rule PlayerRule = new TypeArchetypeResolver.Rule("player", "player-remote");
        private static readonly TypeArchetypeResolver.Rule MobRule = new TypeArchetypeResolver.Rule("mob", "goblin");

        private static TypeArchetypeResolver Resolver(string local = null, string unknown = null) =>
            new TypeArchetypeResolver(local, unknown, PlayerRule, MobRule);

        private static NetworkEntityDescriptor Entity(string type, bool isLocal = false, string id = "uuid-a") =>
            new NetworkEntityDescriptor(id, type, isLocal);

        [Test]
        public void Type_SelectsTheArchetype()
        {
            var resolver = Resolver();

            Assert.IsTrue(resolver.TryResolve(Entity("mob"), out var mob));
            Assert.AreEqual("goblin", mob);

            Assert.IsTrue(resolver.TryResolve(Entity("player"), out var player));
            Assert.AreEqual("player-remote", player);
        }

        [Test]
        public void IdSpelling_IsIgnoredEntirely()
        {
            // The point of the whole release: an id that looks like a monster is a player if the
            // server says so, and vice versa.
            var resolver = Resolver();

            Assert.IsTrue(resolver.TryResolve(Entity("player", id: "enemy-9"), out var stillPlayer));
            Assert.AreEqual("player-remote", stillPlayer);

            Assert.IsTrue(resolver.TryResolve(Entity("mob", id: "totally-normal-uuid"), out var stillMob));
            Assert.AreEqual("goblin", stillMob);
        }

        [Test]
        public void LocalOverride_BeatsTheTypeRule()
        {
            // isLocal comes from comparing the id with the client's own UserId, so it is the one
            // field here that does not depend on the server's vocabulary being what we expect.
            var resolver = Resolver(local: "player-local");

            Assert.IsTrue(resolver.TryResolve(Entity("player", isLocal: true), out var name));
            Assert.AreEqual("player-local", name);
        }

        [Test]
        public void LocalOverride_AppliesEvenWhenTheServerCallsTheLocalPlayerSomethingElse()
        {
            // Incoherent server data. Answering it with the client's own belief about who it is
            // beats rendering the player's avatar as a goblin.
            var resolver = Resolver(local: "player-local");

            Assert.IsTrue(resolver.TryResolve(Entity("mob", isLocal: true), out var name));
            Assert.AreEqual("player-local", name);
        }

        [Test]
        public void WithoutALocalOverride_TheLocalPlayerResolvesByType()
        {
            var resolver = Resolver();

            Assert.IsTrue(resolver.TryResolve(Entity("player", isLocal: true), out var name));
            Assert.AreEqual("player-remote", name);
        }

        [Test]
        public void UnmappedType_IsRefused_AndReported()
        {
            LogAssert.Expect(LogType.Error, new Regex("projectile"));

            Assert.IsFalse(Resolver().TryResolve(Entity("projectile"), out var name));
            Assert.IsNull(name);
        }

        [Test]
        public void EmptyType_IsRefused_AndReportedDistinctly()
        {
            // netcode documents type as never null and empty when the server sent none. That is a
            // different diagnosis from "unknown kind" and gets a different message.
            LogAssert.Expect(LogType.Error, new Regex("no type"));

            Assert.IsFalse(Resolver().TryResolve(Entity(string.Empty), out _));
        }

        [Test]
        public void EachUnmappedType_IsReportedOnce_NotOncePerSpawn()
        {
            // A snapshot-rate log would bury the one line that matters. LogAssert fails the test on
            // an unexpected error, so a second log here is a failure rather than a silent pass.
            LogAssert.Expect(LogType.Error, new Regex("projectile"));

            var resolver = Resolver();
            for (var i = 0; i < 5; i++)
            {
                Assert.IsFalse(resolver.TryResolve(Entity("projectile", id: $"uuid-{i}"), out _));
            }
        }

        [Test]
        public void UnknownArchetype_TurnsARefusalIntoACatchAll()
        {
            var resolver = Resolver(unknown: "placeholder");

            Assert.IsTrue(resolver.TryResolve(Entity("projectile"), out var unknownType));
            Assert.AreEqual("placeholder", unknownType);

            Assert.IsTrue(resolver.TryResolve(Entity(string.Empty), out var noType));
            Assert.AreEqual("placeholder", noType);
        }

        [Test]
        public void Matching_IsOrdinal_NotCaseInsensitive()
        {
            // The type is a wire enum in string clothing. Folding case would paper over a schema
            // disagreement that should be visible.
            LogAssert.Expect(LogType.Error, new Regex("Mob"));

            Assert.IsFalse(Resolver().TryResolve(Entity("Mob"), out _));
        }

        [Test]
        public void DuplicateType_ThrowsAtConstruction()
        {
            // Two equally plausible rules; picking one would be a coin flip made on the caller's
            // behalf, minutes before the wrong archetype shows up in a scene.
            Assert.Throws<ArgumentException>(() => new TypeArchetypeResolver(
                null, null,
                new TypeArchetypeResolver.Rule("mob", "goblin"),
                new TypeArchetypeResolver.Rule("mob", "dragon")));
        }

        [Test]
        public void IncompleteRule_ThrowsAtConstruction()
        {
            Assert.Throws<ArgumentException>(() => new TypeArchetypeResolver(
                null, null, new TypeArchetypeResolver.Rule(null, "goblin")));

            Assert.Throws<ArgumentException>(() => new TypeArchetypeResolver(
                null, null, new TypeArchetypeResolver.Rule("mob", "")));
        }

        [Test]
        public void NoRulesAtAll_IsLegal_AndRefusesEverything()
        {
            LogAssert.Expect(LogType.Error, new Regex("player"));

            var resolver = new TypeArchetypeResolver(null, null);
            Assert.AreEqual(0, resolver.RuleCount);
            Assert.IsFalse(resolver.TryResolve(Entity("player"), out _));
        }

        [Test]
        public void Descriptor_NormalisesNulls_SoAResolverNeverNullChecks()
        {
            // netcode promises a non-null type, but the descriptor is public and a consumer builds
            // one too. Normalising at the boundary is cheaper than every implementation guarding.
            var descriptor = new NetworkEntityDescriptor(null, null, isLocal: false);

            Assert.AreEqual(string.Empty, descriptor.Id);
            Assert.AreEqual(string.Empty, descriptor.Type);
            Assert.IsFalse(descriptor.HasType);
        }
    }
}
