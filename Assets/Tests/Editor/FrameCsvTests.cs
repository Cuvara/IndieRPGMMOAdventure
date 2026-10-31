using NUnit.Framework;
using TestLab;

namespace Tests.Editor
{
    /// <summary>The <c>frames.csv</c> row: original columns first and unchanged, new columns appended, empty when unavailable.</summary>
    public sealed class FrameCsvTests
    {
        [Test]
        public void Header_KeepsOriginalColumnsFirst()
        {
            Assert.AreEqual("frame,t,dt_ms,scenario_t,gc_alloc_b,gc_count,main_ms,used_mb", FrameCsv.Header);
        }

        [Test]
        public void Row_WritesAppendedColumns()
        {
            Assert.AreEqual("7,1.5,16.6,0.25,2048,3,12.345,100.5",
                FrameCsv.Row(7, "1.5", "16.6", "0.25", 2048, 3, 12345000, 100L * 1024 * 1024 + 512 * 1024));
        }

        [Test]
        public void Row_LeavesUnavailableColumnsEmpty()
        {
            var cells = FrameCsv.Row(7, "1.5", "16.6", "0.25", -1, 3, 0, -1).Split(',');
            Assert.AreEqual(8, cells.Length);
            Assert.AreEqual("0.25", cells[3]);
            Assert.AreEqual(string.Empty, cells[4]);
            Assert.AreEqual("3", cells[5]);
            Assert.AreEqual(string.Empty, cells[6]);
            Assert.AreEqual(string.Empty, cells[7]);
        }
    }
}
