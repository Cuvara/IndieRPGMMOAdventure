using Cuvara.DOTS.GameLogic;
using NUnit.Framework;
using Shared.GameLogic.Components;

namespace Cuvara.DOTS.Tests.GameLogic
{
    /// <summary>
    /// Every <c>SimConstants</c> field must equal its <c>GameConstants</c> source.
    /// </summary>
    /// <remarks>
    /// This is the guard against the literal-copy trap. Restating one of these numbers in the
    /// package compiles cleanly, matches the server on the day it is written, and then diverges
    /// silently the first time <c>com.rpgmmo.shared-gamelogic</c> is bumped — with the client
    /// predicting against one value and the server validating against another. Comparisons are
    /// exact, not approximate: a constant that is nearly right is a constant that is wrong.
    /// </remarks>
    public sealed class SimConstantsParityTests
    {
        private SharedGameLogicSimulation _model;

        [SetUp]
        public void SetUp() => _model = new SharedGameLogicSimulation();

        [Test]
        public void Constants_AreReportedAsPopulated()
        {
            Assert.IsTrue(_model.Constants.IsPopulated);
            Assert.IsTrue(_model.IsAuthoritative);
        }

        [Test]
        public void MovementConstants_MatchTheSource()
        {
            var c = _model.Constants;
            Assert.AreEqual(GameConstants.MaxInputMagnitude, c.MaxInputMagnitude);
            Assert.AreEqual(GameConstants.InputDeadzoneSq, c.InputDeadzoneSq);
            Assert.AreEqual(GameConstants.MaxDeltaTime, c.MaxDeltaTime);
            Assert.AreEqual(GameConstants.DisplacementTolerance, c.DisplacementTolerance);
            Assert.AreEqual(GameConstants.DefaultMapWidth, c.DefaultMapWidth);
            Assert.AreEqual(GameConstants.DefaultMapHeight, c.DefaultMapHeight);
        }

        [Test]
        public void CombatConstants_MatchTheSource()
        {
            var c = _model.Constants;
            Assert.AreEqual(GameConstants.AttackRange, c.AttackRange);
            Assert.AreEqual(GameConstants.AttackCooldownMs, c.AttackCooldownMs);
            Assert.AreEqual(GameConstants.MinDamage, c.MinDamage);
        }

        [Test]
        public void SimulationConstants_MatchTheSource()
        {
            var c = _model.Constants;
            Assert.AreEqual(GameConstants.DefaultAoiRadius, c.DefaultAoiRadius);
            Assert.AreEqual(GameConstants.DefaultTickRate, c.DefaultTickRate);
            Assert.AreEqual(GameConstants.DefaultKeyframeInterval, c.DefaultKeyframeInterval);
        }

        [Test]
        public void AttackCooldownTicks_DelegatesInsteadOfRecomputing()
        {
            // The ceiling rounding lives in GameConstants; recomputing it here would be the same
            // trap in arithmetic form.
            foreach (var tickRate in new[] { -1, 0, 1, 15, 30, 60, 128 })
            {
                Assert.AreEqual(GameConstants.AttackCooldownTicks(tickRate), _model.AttackCooldownTicks(tickRate),
                    $"tickRate {tickRate}");
            }
        }

        [Test]
        public void DeltaTimeForTickRate_MatchesTheSharedMovementSystem()
        {
            foreach (var tickRate in new[] { 0, 15, 30, 60 })
            {
                Assert.AreEqual(
                    Shared.GameLogic.Systems.MovementSystem.DeltaTimeForTickRate(tickRate),
                    _model.DeltaTimeForTickRate(tickRate),
                    $"tickRate {tickRate}");
            }
        }
    }
}
