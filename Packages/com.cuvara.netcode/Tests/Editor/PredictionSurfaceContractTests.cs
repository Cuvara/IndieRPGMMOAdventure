using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Cuvara.Netcode.Prediction;
using Shared.GameLogic.Components;

namespace Cuvara.Netcode.Tests.Editor
{
    /// <summary>
    /// Pins the public surface of <see cref="LocalMovePredictor"/> that
    /// <c>com.cuvara.dots</c> drives from a system this package cannot see.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a test rather than a comment.</b> The DOTS adapter references
    /// <c>Cuvara.Netcode.Runtime</c>; netcode must never reference it back, so the adapter
    /// is not built in this repository and **its compiler errors cannot appear in this
    /// repository's CI**. Rename <c>Reconcile</c> here and everything stays green — the
    /// break surfaces in another repo, or in the Unity project, at whatever point someone
    /// next compiles it. This fixture is the only thing on this side that notices.
    /// </para>
    /// <para>
    /// It deliberately asserts <i>signatures</i>, not behaviour — behaviour is
    /// <see cref="LocalMovePredictorTests"/>' job. A failure here means "you changed a
    /// cross-package contract"; the fix is to add rather than change, or to agree the
    /// change on the dots side before it lands, not after.
    /// </para>
    /// <para>
    /// The call sites at the bottom are the other half: they will not compile if a
    /// signature moves, which catches the same thing a step earlier and with a worse error
    /// message. Both are wanted — the compile failure is immediate, the assertions explain.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class PredictionSurfaceContractTests
    {
        // Matched on name AND parameter types, not on name alone. The contract these
        // tests guard is that a given signature still EXISTS -- the class remarks tell
        // callers to "add rather than change", so an overload is the sanctioned way to
        // extend this surface. Selecting on the name alone made the sanctioned move fail:
        // the lookup threw "Sequence contains more than one matching element" the moment a
        // second Reconcile appeared, reporting an addition as a broken contract.
        private static MethodInfo Method(string name, params Type[] parameters) =>
            typeof(LocalMovePredictor)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .SingleOrDefault(m => m.Name == name &&
                    m.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters));

        private static void AssertSignature(string name, Type returnType, params Type[] parameters)
        {
            var method = Method(name, parameters);
            Assert.That(method, Is.Not.Null,
                $"LocalMovePredictor.{name} is part of the contract com.cuvara.dots drives. " +
                "Removing or renaming it -- or changing its parameters rather than adding " +
                "an overload -- breaks a consumer that is not compiled by this repo.");

            Assert.That(method.ReturnType, Is.EqualTo(returnType),
                $"LocalMovePredictor.{name}'s return type is part of the cross-package contract.");

            var actual = method.GetParameters().Select(p => p.ParameterType).ToArray();
            Assert.That(actual, Is.EqualTo(parameters),
                $"LocalMovePredictor.{name}'s parameters are part of the cross-package contract.");
        }

        [Test]
        public void RecordInputKeepsItsSignature() =>
            AssertSignature(nameof(LocalMovePredictor.RecordInput),
                typeof(void), typeof(long), typeof(float), typeof(float));

        /// <remarks>
        /// Takes a <see cref="Vec2"/> — the server's 2D space — not a world-space vector.
        /// The DOTS side converts from <c>ReconciliationAnchor.ServerPosition</c> at the
        /// boundary. Widening this to accept world space would silently move the clamp
        /// against <see cref="MapBounds"/> into the wrong coordinate system.
        /// </remarks>
        [Test]
        public void ReconcileKeepsItsSignature() =>
            AssertSignature(nameof(LocalMovePredictor.Reconcile),
                typeof(void), typeof(Vec2), typeof(long));

        [Test]
        public void ReconcileOffersTheServerTickOverload()
        {
            Assert.That(
                Method(nameof(LocalMovePredictor.Reconcile),
                    typeof(Vec2), typeof(long), typeof(long)),
                Is.Not.Null,
                "Reconcile(position, ackTick, serverTick) is what lets the replay window " +
                "be anchored to the tick the server actually simulated, so the prediction " +
                "lead survives a snapshot that acknowledges every outstanding input. " +
                "It is an addition: the two-argument form above stays for com.cuvara.dots.");
        }

        [Test]
        public void AdvanceKeepsItsSignature() =>
            AssertSignature(nameof(LocalMovePredictor.Advance), typeof(void), typeof(float));

        [Test]
        public void ResetKeepsItsSignature() =>
            AssertSignature(nameof(LocalMovePredictor.Reset), typeof(void));

        [Test]
        public void PositionIsAReadableVec2()
        {
            var property = typeof(LocalMovePredictor).GetProperty(nameof(LocalMovePredictor.Position));
            Assert.That(property, Is.Not.Null);
            Assert.That(property.PropertyType, Is.EqualTo(typeof(Vec2)),
                "The DOTS driving system reads this straight into LocalTransform via the " +
                "space mapping; its type is part of the contract.");
            Assert.That(property.CanRead, Is.True);
        }

        [Test]
        public void IsEnabledIsAReadableBool()
        {
            var property = typeof(LocalMovePredictor).GetProperty(nameof(LocalMovePredictor.IsEnabled));
            Assert.That(property, Is.Not.Null);
            Assert.That(property.PropertyType, Is.EqualTo(typeof(bool)));
            Assert.That(property.CanRead, Is.True,
                "The DOTS side gates adding the PredictedTransform marker on this. Without " +
                "it, a refusing predictor would still claim LocalTransform and nothing would " +
                "write the transform at all — the avatar freezes, in a build, not in CI.");
        }

        /// <summary>
        /// The predictor must stay free of DOTS and Unity types, or the dots package could
        /// not drive it without this package taking the dependency back.
        /// </summary>
        [Test]
        public void PredictorSurfaceNamesNoEngineTypes()
        {
            var assembly = typeof(LocalMovePredictor).Assembly;

            var offenders = typeof(LocalMovePredictor)
                .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(DescribeType)
                .Where(t => t != null)
                .Where(t => t.Namespace != null &&
                            (t.Namespace.StartsWith("Unity", StringComparison.Ordinal) ||
                             t.Namespace.StartsWith("UnityEngine", StringComparison.Ordinal)))
                .Select(t => t.FullName)
                .Distinct()
                .ToArray();

            Assert.That(offenders, Is.Empty,
                "LocalMovePredictor's surface must name no Unity or DOTS type. It is driven " +
                "from com.cuvara.dots, which references this assembly — so a DOTS type here " +
                "would force the dependency to point both ways. It is also what keeps the " +
                "algorithm testable in EditMode without a World. Offenders: " +
                string.Join(", ", offenders));

            Assert.That(assembly.GetReferencedAssemblies().Select(a => a.Name),
                Has.No.Member("Unity.Entities"),
                "Cuvara.Netcode.Runtime must not reference Unity.Entities.");
        }

        private static Type DescribeType(MemberInfo member) => member switch
        {
            PropertyInfo p => p.PropertyType,
            FieldInfo f => f.FieldType,
            MethodInfo m => m.ReturnType,
            _ => null,
        };

        /// <summary>
        /// Compile-time half: this is the call sequence the DOTS driving system makes. It
        /// stops compiling if any signature moves, which is the earliest possible warning.
        /// </summary>
        [Test]
        public void TheDotsDrivingSequenceStillCompilesAndRuns()
        {
            var predictor = new LocalMovePredictor(
                new PredictionSettings(tickRate: 15, speed: 5f, MapBounds.Default));

            Assert.That(predictor.IsEnabled, Is.True);

            // Anchor + AckTick, paired by the caller — the seam that only the predictor sees.
            predictor.Reconcile(new Vec2(1f, 2f), 0L);
            predictor.RecordInput(1L, 1f, 0f);
            predictor.Reconcile(new Vec2(1.1f, 2f), 1L);
            predictor.Advance(1f / 60f);

            Vec2 render = predictor.Position;
            Assert.That(float.IsFinite(render.X) && float.IsFinite(render.Y), Is.True);

            predictor.Reset();
        }
    }
}
