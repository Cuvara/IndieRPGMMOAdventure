namespace Tests.Editor
{
    using NUnit.Framework;
    using Scripts.Session;

    /// <summary>
    /// Pins the <c>[DOTSNet]</c> session log lines byte for byte. They are a machine contract:
    /// <list type="bullet">
    /// <item><c>Tools/verify-multiclient.sh</c> greps <c>IN WORLD</c>, awk-matches <c>Auth OK</c> with its
    /// <c>user_id=</c> token, and fails on <c>FATAL</c>.</item>
    /// <item><c>Tools/run-clients.sh</c> and the rpg-mmo-server <c>checks_client.sh</c> read the same lines.</item>
    /// <item>The rpg-factory TestLab analyzer (<c>testlab/lab/analyze.py</c>) matches <c>FATAL</c> in client
    /// logs. Its <c>clients_in_world</c> check reads the TestLab event stream (<c>events.ndjson</c>), not
    /// these log lines.</item>
    /// <item>CLAUDE.md and the DOTS sample (<c>DOTSNetworkBridge</c>) print the same text.</item>
    /// </list>
    /// Editing a constant here without editing every consumer in the same change breaks the harness
    /// silently (a client that "works" but is never counted). Update the literals only together
    /// with those consumers. See docs/HARNESS-MARKERS.md.
    /// </summary>
    public sealed class HarnessMarkerContractTests
    {
        [Test]
        public void HarnessMarkers_MatchTheContractByteForByte()
        {
            Assert.AreEqual("[DOTSNet]", MainSessionFlow.Tag);
            Assert.AreEqual("[DOTSNet] Authenticating device=", MainSessionFlow.AuthenticatingPrefix);
            Assert.AreEqual("[DOTSNet] Auth OK, user_id=", MainSessionFlow.AuthOkPrefix);
            Assert.AreEqual("[DOTSNet] IN WORLD as ", MainSessionFlow.InWorldPrefix);
            Assert.AreEqual("[DOTSNet] party ready: ", MainSessionFlow.PartyPrefix);
            Assert.AreEqual("[DOTSNet] character: ", MainSessionFlow.CharacterPrefix);
            Assert.AreEqual("[DOTSNet] FATAL: ", MainSessionFlow.FatalPrefix);
            Assert.AreEqual("[DOTSNet] Cancelled", MainSessionFlow.CancelledLine);
        }
    }
}
