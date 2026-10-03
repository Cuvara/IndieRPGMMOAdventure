namespace Tests.Editor
{
    using System;
    using NUnit.Framework;
    using Scripts.Nakama.Auth;
    using Scripts.Nakama.Characters;
    using Scripts.Session;

    /// <summary>
    /// Headless character selection (ADR-31): request > last used > first slot, an empty roster
    /// creates a default, the placeholder name rule, and the Nakama payloads/parsers it rides on.
    /// </summary>
    public sealed class CharacterSelectionTests
    {
        private static readonly CharacterEntry[] Roster =
        {
            new CharacterEntry("id-b", "Bravo", 1),
            new CharacterEntry("id-a", "Alpha", 0),
            new CharacterEntry("id-c", "Charlie", 3),
        };

        [Test]
        public void EmptyRoster_CreatesTheDefaultName()
        {
            var decision = CharacterSelection.Decide(Array.Empty<CharacterEntry>(), null, null, "3f9c-77ab-01");

            Assert.That(decision.Create, Is.True);
            Assert.That(decision.Name, Is.EqualTo("Hero_3f9c77ab01"));
            Assert.That(CharacterSelection.IsValidName(decision.Name), Is.True);
        }

        [Test]
        public void DefaultName_AlwaysPassesTheRule()
        {
            Assert.That(CharacterSelection.DefaultName(null), Is.EqualTo("Hero"));
            Assert.That(CharacterSelection.DefaultName("---"), Is.EqualTo("Hero"));
            var longId = CharacterSelection.DefaultName("abcdefghijklmnopqrstuvwxyz");
            Assert.That(longId.Length, Is.EqualTo(CharacterSelection.NameMaxLength));
            Assert.That(CharacterSelection.IsValidName(longId), Is.True);
        }

        [Test]
        public void LastUsed_WinsOverTheFirstSlot_WhenStillOnTheRoster()
        {
            Assert.That(CharacterSelection.Decide(Roster, null, "id-c", "u").Id, Is.EqualTo("id-c"));
            Assert.That(CharacterSelection.Decide(Roster, null, "deleted-id", "u").Id, Is.EqualTo("id-a"), "falls back to the lowest slot");
            Assert.That(CharacterSelection.Decide(Roster, null, null, "u").Reason, Is.EqualTo("first slot"));
        }

        [Test]
        public void Request_ById_ThenByName_CaseInsensitive()
        {
            Assert.That(CharacterSelection.Decide(Roster, "id-b", "id-c", "u").Id, Is.EqualTo("id-b"));
            var byName = CharacterSelection.Decide(Roster, " charlie ", "id-a", "u");
            Assert.That(byName.Create, Is.False);
            Assert.That(byName.Id, Is.EqualTo("id-c"));
        }

        [Test]
        public void Request_ForAnUnknownValidName_CreatesIt()
        {
            var decision = CharacterSelection.Decide(Roster, "Scout_1", null, "u");
            Assert.That(decision.Create, Is.True);
            Assert.That(decision.Name, Is.EqualTo("Scout_1"));
        }

        [Test]
        public void Request_ThatIsNeitherOnTheRosterNorAValidName_IsRefused()
        {
            Assert.Throws<ArgumentException>(() => CharacterSelection.Decide(Roster, "no spaces allowed", null, "u"));
            Assert.Throws<ArgumentException>(() => CharacterSelection.Decide(Roster, "ab", null, "u"));
        }

        [TestCase("Arthas", true)]
        [TestCase("a_1", true)]
        [TestCase("ab", false)]
        [TestCase("seventeen_chars_x", false)]
        [TestCase("bad-dash", false)]
        [TestCase("ünicode", false)]
        public void NameRule_MirrorsTheServerPlaceholder(string name, bool valid)
        {
            Assert.That(CharacterSelection.IsValidName(name), Is.EqualTo(valid));
        }

        // ---- the Nakama contract (backend/nakama/docs/API.md) ----

        [Test]
        public void RosterParse_OrdersBySlot_AndReadsMaxSlots()
        {
            var roster = CharacterService.ParseRoster(
                "{\"characters\":[{\"id\":\"b\",\"slot\":2,\"name\":\"Bee\",\"created_at\":5},{\"id\":\"a\",\"slot\":0,\"name\":\"Ay\",\"created_at\":1}],\"max_slots\":4}");

            Assert.That(roster.MaxSlots, Is.EqualTo(4));
            Assert.That(roster.Characters.Count, Is.EqualTo(2));
            Assert.That(roster.Characters[0].Id, Is.EqualTo("a"));
            Assert.That(roster.Characters[1].Name, Is.EqualTo("Bee"));
            Assert.That(roster.Characters[1].CreatedAt, Is.EqualTo(5));

            Assert.That(CharacterService.ParseRoster("{\"characters\":[],\"max_slots\":4}").Characters, Is.Empty);
        }

        [Test]
        public void CreateParse_And_Payload()
        {
            var created = CharacterService.ParseCreated("{\"character\":{\"id\":\"5f0c\",\"slot\":0,\"name\":\"Arthas\",\"created_at\":1700000000}}");
            Assert.That(created.Id, Is.EqualTo("5f0c"));
            Assert.That(created.Name, Is.EqualTo("Arthas"));

            Assert.That(CharacterService.CreatePayload("Arthas", -1), Is.EqualTo("{\"name\":\"Arthas\"}"), "negative slot = lowest free, omitted");
            Assert.That(CharacterService.CreatePayload("Arthas", 2), Is.EqualTo("{\"name\":\"Arthas\",\"slot\":2}"));
            Assert.Throws<CharacterException>(() => CharacterService.ParseCreated("{}"));
            Assert.Throws<CharacterException>(() => CharacterService.ParseRoster("not json"));
        }

        [Test]
        public void GatewayTokenPayload_CarriesTheCharacter_OnlyWhenOneIsSelected()
        {
            Assert.That(NakamaAuthProvider.GatewayTokenPayload(null), Is.EqualTo("{}"));
            Assert.That(NakamaAuthProvider.GatewayTokenPayload(string.Empty), Is.EqualTo("{}"));
            Assert.That(NakamaAuthProvider.GatewayTokenPayload("5f0c-uuid"), Is.EqualTo("{\"character_id\":\"5f0c-uuid\"}"));
        }
    }
}
