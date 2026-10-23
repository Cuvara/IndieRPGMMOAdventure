using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using NUnit.Framework;
using TestLab;

namespace Tests.Editor
{
    /// <summary>
    /// The testlab's background file writer: the writing (main) thread never waits on the disk,
    /// nothing is lost or reordered, the writer allocates nothing per write, and a disk too slow
    /// for the buffer pool is counted, not hidden.
    /// </summary>
    public sealed class BackgroundFileWriterTests
    {
        private string path;

        [SetUp]
        public void SetUp() => this.path = Path.Combine(Path.GetTempPath(), "testlab-writer-" + Guid.NewGuid().ToString("N") + ".txt");

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(this.path))
            {
                File.Delete(this.path);
            }
        }

        [Test]
        public void EverythingWritten_InOrder_ByteIdenticalToAStreamWriter()
        {
            var expected = new StringBuilder();
            using (var w = new BackgroundFileWriter(this.path, bufferChars: 1000, bufferCount: 4))
            {
                for (int i = 0; i < 20_000; i++)
                {
                    string line = "{\"i\":" + i + ",\"pad\":\"" + new string('x', i % 50) + "\"}";
                    w.WriteLine(line);
                    expected.Append(line).Append(Environment.NewLine);
                    w.Write('#');
                    expected.Append('#');
                    if (i % 1000 == 0)
                    {
                        w.Flush();
                    }
                }
            }

            Assert.That(File.ReadAllText(this.path, Encoding.UTF8), Is.EqualTo(expected.ToString()));
            Assert.That(File.ReadAllBytes(this.path)[0], Is.Not.EqualTo(0xEF), "no BOM, like the StreamWriter it replaces");
        }

        [Test]
        public void ASlowDisk_NeverBlocksTheWritingThread()
        {
            var slow = new SlowStream(delayMs: 100);
            var w = new BackgroundFileWriter(slow, "slow", bufferChars: 64 * 1024, bufferCount: 32);
            string line = new string('s', 16_000); // one samples.ndjson line

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 40; i++)
            {
                w.WriteLine(line); // 40 x 16 KB = about 10 buffers handed to a disk that takes 100 ms each
                if (i % 10 == 0)
                {
                    w.Flush();
                }
            }

            long writingMs = sw.ElapsedMilliseconds;
            Assert.That(writingMs, Is.LessThan(150), "the writing thread must not wait for the disk");
            Assert.That(w.BackpressureMs, Is.EqualTo(0));

            // Control: the disk really was slow - draining it on Dispose takes the time.
            sw.Restart();
            w.Dispose();
            Assert.That(sw.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(100));
            Assert.That(slow.BytesWritten, Is.EqualTo(40L * (16_000 + Environment.NewLine.Length)));
        }

        [Test]
        public void ADiskSlowerThanThePool_IsCountedAsBackpressure_AndNothingIsDropped()
        {
            var slow = new SlowStream(delayMs: 30);
            var w = new BackgroundFileWriter(slow, "tiny", bufferChars: 100, bufferCount: 2);
            for (int i = 0; i < 20; i++)
            {
                w.Write(new string('b', 100));
            }

            w.Dispose();
            Assert.That(w.BackpressureMs, Is.GreaterThan(0));
            Assert.That(slow.BytesWritten, Is.EqualTo(2000));
        }

        [Test]
        public void Writing_AllocatesNothing()
        {
            var w = new BackgroundFileWriter(Stream.Null, "null", bufferChars: 4096, bufferCount: 8);
            string line = new string('a', 300);
            var chars = line.ToCharArray();
            w.WriteLine(line); // warm up

            Assert.That(() => { GC.KeepAlive(new byte[4096]); },
                UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(), "allocation probe does not work here");
            Assert.That(() =>
                {
                    for (int i = 0; i < 10_000; i++)
                    {
                        w.WriteLine(line);
                        w.Write(chars, 0, chars.Length);
                        w.Write(',');
                        if (i % 100 == 0)
                        {
                            w.Flush();
                        }
                    }
                },
                UnityEngine.TestTools.Constraints.ConstraintExtensions.AllocatingGCMemory(UnityEngine.TestTools.Constraints.Is.Not));
            w.Dispose();
        }

        [Test]
        public void AFailingDisk_IsReported_AndNamesTheFile_NotSwallowed()
        {
            var failing = new FailingStream(failAfterWrites: 1, failOnDispose: false);
            var w = new BackgroundFileWriter(failing, "samples", bufferChars: 100, bufferCount: 4);
            for (int i = 0; i < 10; i++)
            {
                w.Write(new string('f', 100));
            }

            w.Dispose();
            Assert.That(w.Failure, Is.InstanceOf<IOException>());
            Assert.That(w.DroppedChars, Is.GreaterThan(0), "what was not written is counted");

            using (var ok = new BackgroundFileWriter(Stream.Null, "ok"))
            {
                var report = TestLabRunner.WriterFailure(new[]
                {
                    new System.Collections.Generic.KeyValuePair<string, BackgroundFileWriter>("events.ndjson", ok),
                    new System.Collections.Generic.KeyValuePair<string, BackgroundFileWriter>("samples.ndjson", w),
                });
                Assert.That(report, Is.EqualTo("output samples.ndjson failed: IOException: disk full"));
            }
        }

        [Test]
        public void AFailingClose_IsReported_AndDoesNotEscapeTheWriterThread()
        {
            var failing = new FailingStream(failAfterWrites: int.MaxValue, failOnDispose: true);
            var w = new BackgroundFileWriter(failing, "close", bufferChars: 100, bufferCount: 4);
            w.Write("data");
            Assert.DoesNotThrow(() => w.Dispose());
            Assert.That(w.Failure, Is.InstanceOf<IOException>());
            Assert.That(w.Failure.Message, Is.EqualTo("close failed"));
            Assert.That(w.DroppedChars, Is.EqualTo(0), "the data itself was written");
            using (var ok = new BackgroundFileWriter(Stream.Null, "ok"))
            {
                Assert.That(TestLabRunner.WriterFailure(new[]
                {
                    new System.Collections.Generic.KeyValuePair<string, BackgroundFileWriter>("frames.csv", ok),
                }), Is.Null, "a healthy writer reports nothing");
            }
        }

        private sealed class FailingStream : Stream
        {
            private readonly int failAfterWrites;
            private readonly bool failOnDispose;
            private int writes;

            public FailingStream(int failAfterWrites, bool failOnDispose)
            {
                this.failAfterWrites = failAfterWrites;
                this.failOnDispose = failOnDispose;
            }

            public override bool CanRead => false;

            public override bool CanSeek => false;

            public override bool CanWrite => true;

            public override long Length => 0;

            public override long Position { get => 0; set => throw new NotSupportedException(); }

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (++this.writes > this.failAfterWrites)
                {
                    throw new IOException("disk full");
                }
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (disposing && this.failOnDispose)
                {
                    throw new IOException("close failed");
                }
            }
        }

        private sealed class SlowStream : Stream
        {
            private readonly int delayMs;
            private long written;

            public SlowStream(int delayMs) => this.delayMs = delayMs;

            public long BytesWritten => Interlocked.Read(ref this.written);

            public override bool CanRead => false;

            public override bool CanSeek => false;

            public override bool CanWrite => true;

            public override long Length => this.BytesWritten;

            public override long Position { get => this.BytesWritten; set => throw new NotSupportedException(); }

            public override void Write(byte[] buffer, int offset, int count)
            {
                Thread.Sleep(this.delayMs);
                Interlocked.Add(ref this.written, count);
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();
        }
    }
}
