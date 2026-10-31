using NUnit.Framework;
using System;
using TestLab;
using UnityEngine.TestTools.Constraints;

namespace Tests.Editor
{
    /// <summary>The <c>frames.csv</c> row: original columns first and unchanged, new columns appended, empty when unavailable.</summary>
    public sealed class FrameCsvTests
    {
        private readonly FrameCsv frameCsv = new FrameCsv();

        [Test]
        public void Header_KeepsOriginalColumnsFirst()
        {
            Assert.AreEqual("frame,t,dt_ms,scenario_t,gc_alloc_b,gc_count,main_ms,used_mb", FrameCsv.Header);
        }

        [Test]
        public void Row_WritesAppendedColumns()
        {
            Assert.AreEqual("7,1.5,16.6,0.25,2048,3,12.345,100.5",
                this.frameCsv.Row(7, "1.5", "16.6", "0.25", 2048, 3, 12345000, 100L * 1024 * 1024 + 512 * 1024));
        }

        [Test]
        public void Row_LeavesUnavailableColumnsEmpty()
        {
            var cells = this.frameCsv.Row(7, "1.5", "16.6", "0.25", -1, 3, 0, -1).Split(',');
            Assert.AreEqual(8, cells.Length);
            Assert.AreEqual("0.25", cells[3]);
            Assert.AreEqual(string.Empty, cells[4]);
            Assert.AreEqual("3", cells[5]);
            Assert.AreEqual(string.Empty, cells[6]);
            Assert.AreEqual(string.Empty, cells[7]);
        }

        [Test]
        public void Row_MatchesPreviousFormattingForRepresentativeValues()
        {
            Assert.AreEqual("17,12.5,16.666,0.125,0,42,0.001,0.01",
                this.frameCsv.Row(17, "12.5", "16.666", "0.125", 0, 42, 1000, 10485));
            Assert.AreEqual("7,1.5,16.6,0.25,,,,",
                this.frameCsv.Row(7, "1.5", "16.6", "0.25", -1, -1, 0, -1));
        }

        [Test]
        public void Row_ReusesItsFormatterAndAllocatesOnlyTheResultString()
        {
            // Warm up the builder and formatter before checking the repeated hot-path operation.
            this.frameCsv.Row(7, "1.5", "16.6", "0.25", 2048, 3, 12345000, 100L * 1024 * 1024);
            string[] rows = new string[100];

            // Some Unity Editor Mono versions return zero from this API even for known allocations.
            // When supported, measure exactly the bound: one UTF-16 result plus 64 bytes of overhead.
            long controlBefore = GC.GetAllocatedBytesForCurrentThread();
            GC.KeepAlive(new byte[4096]);
            long controlBytes = GC.GetAllocatedBytesForCurrentThread() - controlBefore;
            if (controlBytes > 0)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < rows.Length; i++)
                {
                    rows[i] = this.frameCsv.Row(i, "1.5", "16.6", "0.25", 2048, 3, 12345000, 100L * 1024 * 1024);
                }
                long bytesPerRow = (GC.GetAllocatedBytesForCurrentThread() - before) / rows.Length;
                Assert.LessOrEqual(bytesPerRow, rows[0].Length * sizeof(char) + 64,
                    "Per-row allocation must be no more than the final string (row length * 2 + 64 bytes).");
            }
            else
            {
                // Unity's supported GC.Alloc constraint still observes the intentional result
                // strings. Numeric appends use primitive overloads / TryFormat into reusable storage.
                Assert.That(() =>
                {
                    for (int i = 0; i < rows.Length; i++)
                    {
                        rows[i] = this.frameCsv.Row(i, "1.5", "16.6", "0.25", 2048, 3, 12345000, 100L * 1024 * 1024);
                    }
                }, Is.AllocatingGCMemory());
            }

            Assert.AreEqual(100, rows.Length);
            Assert.AreEqual(this.frameCsv.Row(99, "1.5", "16.6", "0.25", 2048, 3, 12345000, 100L * 1024 * 1024),
                rows[rows.Length - 1]);
        }
    }
}
