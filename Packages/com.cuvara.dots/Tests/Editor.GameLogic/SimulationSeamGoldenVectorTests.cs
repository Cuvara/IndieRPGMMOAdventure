using System;
using System.Collections.Generic;
using Cuvara.DOTS.GameLogic;
using Cuvara.DOTS.Simulation;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cuvara.DOTS.Tests.GameLogic
{
    /// <summary>
    /// Replays the shared movement golden vectors <b>through the seam</b> — the same fixtures the
    /// server and the netcode package replay, but entering via
    /// <see cref="ISimulationModel.TryMove"/> with <c>float2</c> and <see cref="SimBounds"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The netcode package already proves <c>Shared.GameLogic</c> computes the server's numbers
    /// under Unity's compiler. What it cannot prove is that <i>this package's conversion layer</i>
    /// preserves them: a <c>float2</c> → <c>Vec2</c> hop that reordered a field, or a
    /// <see cref="SimBounds"/> that normalized its edges differently from <c>MapBounds</c>, would
    /// leave the shared suite green and break prediction anyway. That gap is what these cases close.
    /// </para>
    /// <para>
    /// Comparison is bit-exact and the expected values come only from the fixtures. A red test here
    /// means either the conversion layer is lossy or the shared logic moved without the vectors
    /// being regenerated — two different problems that must be told apart, not averaged over.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class SimulationSeamGoldenVectorTests
    {
        private static IEnumerable<TestCaseData> MovementCases()
        {
            // Built at collection time: a fixture that cannot be read has to fail loudly rather
            // than yield an empty, silently passing suite.
            foreach (var c in GoldenMovementVectors.Load())
            {
                yield return new TestCaseData(c).SetName(c.name);
            }
        }

        [TestCaseSource(nameof(MovementCases))]
        public void Movement_ThroughTheSeam_MatchesTheFixture(MovementCase c)
        {
            ISimulationModel model = new SharedGameLogicSimulation();

            var entity = new SimEntity
            {
                Position = new float2(GoldenMovementVectors.Float(c.posX), GoldenMovementVectors.Float(c.posY)),
                Speed = GoldenMovementVectors.Float(c.speed),
                Dead = c.dead,
                Hp = c.dead ? 0 : 100,
                MaxHp = 100,
            };

            var bounds = new SimBounds(
                GoldenMovementVectors.Float(c.minX), GoldenMovementVectors.Float(c.minY),
                GoldenMovementVectors.Float(c.maxX), GoldenMovementVectors.Float(c.maxY));

            var input = new float2(GoldenMovementVectors.Float(c.moveX), GoldenMovementVectors.Float(c.moveY));

            var result = model.TryMove(in entity, input, GoldenMovementVectors.Float(c.dt), in bounds, out var moved);

            var expected = (SimMoveResult)Enum.Parse(typeof(SimMoveResult), c.expectedResult);
            Assert.AreEqual(expected, result, c.name + ".result");
            GoldenMovementVectors.AssertBitEqual(c.expectedX, moved.x, c.name + ".x");
            GoldenMovementVectors.AssertBitEqual(c.expectedY, moved.y, c.name + ".y");
        }

        [Test]
        public void PassiveModel_RefusesRatherThanApproximates()
        {
            ISimulationModel model = new PassiveSimulationModel();
            var entity = new SimEntity { Position = new float2(4f, -2f), Speed = 5f };

            var result = model.TryMove(in entity, new float2(1f, 0f), 1f / 15f, new SimBounds(-500f, -500f, 500f, 500f), out var moved);

            Assert.IsFalse(model.IsAuthoritative, "prediction code keys off this");
            Assert.AreEqual(SimMoveResult.Unavailable, result);
            Assert.AreEqual(entity.Position, moved, "the entity stays where the server last put it");
            Assert.IsFalse(model.Constants.IsPopulated);
        }
    }
}
