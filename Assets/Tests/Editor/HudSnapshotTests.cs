#if CUVARA_DOTS && CUVARA_NETCODE && CUVARA_UITOOLKIT_ENTITIES
namespace Tests.Editor
{
    using NUnit.Framework;
    using Scripts.UI.Hud.Ecs;

    /// <summary>
    /// The bridge's conversion, <see cref="HudSnapshot.From"/>, as the pure function it
    /// is: component values in, boundary ViewModel out. No world, no system.
    /// </summary>
    public class HudSnapshotTests
    {
        [Test]
        public void ALocalPlayer_FormatsCaption_Fraction_AndPosition()
        {
            var snapshot = HudSnapshot.From(new HudState
            {
                Hp = 57,
                MaxHp = 100,
                PosX = 12.3f,
                PosZ = 45.7f,
                PlayersVisible = 2,
                EntitiesVisible = 3,
                HasLocalPlayer = true,
            });

            Assert.That(snapshot.HealthCaption, Is.EqualTo("57/100"));
            Assert.That(snapshot.HealthFraction, Is.EqualTo(0.57f).Within(1e-5f));
            Assert.That(snapshot.PositionCaption, Is.EqualTo("(12.3, 45.7)"));
            Assert.That(snapshot.PlayersVisible, Is.EqualTo(2));
            Assert.That(snapshot.EntitiesVisible, Is.EqualTo(3));
            Assert.That(snapshot.HasLocalPlayer, Is.True);
        }

        [Test]
        public void NoLocalPlayer_ShowsPlaceholders_AndZeroFraction_ButStillCounts()
        {
            var snapshot = HudSnapshot.From(new HudState
            {
                PlayersVisible = 1,
                EntitiesVisible = 4,
                HasLocalPlayer = false,
            });

            Assert.That(snapshot.HealthCaption, Is.EqualTo(HudSnapshot.NoValue));
            Assert.That(snapshot.PositionCaption, Is.EqualTo(HudSnapshot.NoValue));
            Assert.That(snapshot.HealthFraction, Is.Zero);
            Assert.That(snapshot.PlayersVisible, Is.EqualTo(1));
            Assert.That(snapshot.EntitiesVisible, Is.EqualTo(4));
        }

        [Test]
        public void ZeroOrNegativeMaxHp_YieldsZeroFraction_NotADivideByZero()
        {
            var snapshot = HudSnapshot.From(new HudState { Hp = 10, MaxHp = 0, HasLocalPlayer = true });
            Assert.That(snapshot.HealthFraction, Is.Zero);
        }

        [Test]
        public void FractionIsClamped_WhenTheServerReportsOverhealOrNegativeHp()
        {
            Assert.That(HudSnapshot.From(new HudState { Hp = 150, MaxHp = 100, HasLocalPlayer = true }).HealthFraction, Is.EqualTo(1f));
            Assert.That(HudSnapshot.From(new HudState { Hp = -5, MaxHp = 100, HasLocalPlayer = true }).HealthFraction, Is.Zero);
        }

        [Test]
        public void PositionFormatting_IsInvariantCulture()
        {
            // "12.3", never "12,3" — whatever the OS locale says.
            var snapshot = HudSnapshot.From(new HudState { PosX = 1.5f, PosZ = -2.5f, MaxHp = 1, HasLocalPlayer = true });
            Assert.That(snapshot.PositionCaption, Is.EqualTo("(1.5, -2.5)"));
        }
    }
}
#endif
