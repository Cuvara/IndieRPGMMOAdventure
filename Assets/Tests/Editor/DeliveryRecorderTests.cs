using System.IO;
using NUnit.Framework;
using TestLab;

namespace Tests.Editor
{
    /// <summary>
    /// The testlab's delivery files (contract C2): the exact columns the factory analyzer parses,
    /// empty fields for unknown values, and the snapshot age a render row reports.
    /// </summary>
    public sealed class DeliveryRecorderTests
    {
        private static string[] Lines(StringWriter w) =>
            w.ToString().Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

        [Test]
        public void Headers_AreTheDocumentedColumns()
        {
            var delivery = new StringWriter();
            var render = new StringWriter();
            using (new DeliveryRecorder(delivery, render))
            {
            }

            Assert.That(Lines(delivery)[0], Is.EqualTo("world_tick,recv_us,applied_us,bytes,full,entities,frame"));
            Assert.That(Lines(render)[0],
                Is.EqualTo("frame,t_us,render_tick,clock_newest_tick,newest_tick,newest_recv_us,snapshot_age_ms"));
        }

        [Test]
        public void Snapshot_WithAndWithoutTheTap_WritesUnknownsAsEmpty()
        {
            var delivery = new StringWriter();
            var recorder = new DeliveryRecorder(delivery, new StringWriter());

            recorder.OnSnapshot(100, 5_000, 5_120, 812, true, 7, 3);
            recorder.OnSnapshot(101, -1, 55_000, -1, false, 2, 4);

            var lines = Lines(delivery);
            Assert.That(lines[1], Is.EqualTo("100,5000,5120,812,1,7,3"));
            Assert.That(lines[2], Is.EqualTo("101,,55000,,0,2,4"));
            Assert.That(recorder.Snapshots, Is.EqualTo(2));
            Assert.That(recorder.SnapshotsWithBytes, Is.EqualTo(1));
        }

        [Test]
        public void Frame_AgeIsMeasuredFromTheNewestTicksReceiveTime()
        {
            var render = new StringWriter();
            var recorder = new DeliveryRecorder(new StringWriter(), render);

            recorder.OnFrame(1, 1_000, false, double.NaN, -1);
            recorder.OnSnapshot(200, 10_000, 10_050, 500, false, 1, 2);
            // An older tick arriving late must not become "newest".
            recorder.OnSnapshot(199, 12_000, 12_010, 500, false, 1, 2);
            recorder.OnFrame(2, 26_500, true, 198.25, 200);

            var lines = Lines(render);
            Assert.That(lines[1], Is.EqualTo("1,1000,,,,,"));
            Assert.That(lines[2], Is.EqualTo("2,26500,198.25,200,200,10000,16.5"));
        }

        [Test]
        public void Frame_WithoutTheTap_AgesFromTheApplyTime()
        {
            var render = new StringWriter();
            var recorder = new DeliveryRecorder(new StringWriter(), render);

            recorder.OnSnapshot(50, -1, 2_000, -1, true, 1, 1);
            recorder.OnFrame(1, 3_000, true, 49.5, 50);

            Assert.That(Lines(render)[1], Is.EqualTo("1,3000,49.5,50,50,2000,1"));
        }

        [Test]
        public void ResetSession_ForgetsTheNewestTick()
        {
            var render = new StringWriter();
            var recorder = new DeliveryRecorder(new StringWriter(), render);

            recorder.OnSnapshot(900, 1_000, 1_001, 10, true, 1, 1);
            recorder.ResetSession();
            recorder.OnSnapshot(5, 2_000, 2_001, 10, true, 1, 2);
            recorder.OnFrame(2, 2_500, false, double.NaN, -1);

            Assert.That(Lines(render)[1], Is.EqualTo("2,2500,,,5,2000,0.5"));
        }
    }
}
