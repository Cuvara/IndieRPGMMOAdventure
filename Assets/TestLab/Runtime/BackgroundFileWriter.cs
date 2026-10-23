namespace TestLab
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading;

    /// <summary>
    /// A <see cref="TextWriter"/> whose disk writes happen on a background thread. The thread that
    /// writes (the player's main thread) only copies characters into a pooled buffer; encoding and
    /// <see cref="Stream.Write(byte[],int,int)"/> run on the writer's own thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Why.</b> The testlab streamed its outputs through <c>StreamWriter</c> on the main
    /// thread. A <c>samples.ndjson</c> line is about 16 KB, four times the writer's buffer, so every
    /// sample (20 Hz) was a synchronous <c>WriteFile</c> on the main thread, and the once-a-second
    /// flush was another. In the one-hour soak 20261023T015749Z-S09-4zym, 11 of the 18 frames over
    /// 100 ms on the three clients came right after a sample frame (about 1 expected by chance), and
    /// two of them were the single-process stalls (271 ms, 274 ms) that the snapshot check blamed on
    /// the client. Any disk hiccup on the host became a frame stall in the game being measured.</para>
    /// <para><b>Bounded.</b> A fixed pool of buffers is allocated up front (count x chars per
    /// buffer, chosen per stream by the runner: about 8 MiB for <c>samples.ndjson</c>, which holds
    /// some 25 s of samples, and 512 KiB for each other stream). Writing allocates nothing. If the disk is so
    /// slow that every buffer is queued, the writing thread waits for one to free up - nothing is
    /// ever dropped - and the time it waited is counted in <see cref="BackpressureMs"/>, so a run
    /// that was slowed by its own instrumentation says so.</para>
    /// <para><b>Order and durability.</b> Buffers are written in order. <see cref="Flush"/> hands the
    /// current buffer to the thread without waiting for the disk. <see cref="Dispose(bool)"/> hands
    /// over the rest and waits until everything is written and the file is closed, so a run's files
    /// are complete when the runner finishes.</para>
    /// </remarks>
    public sealed class BackgroundFileWriter : TextWriter
    {
        public const int DefaultBufferChars = 32 * 1024;
        public const int DefaultBufferCount = 8;

        private readonly int bufferChars;
        private readonly int bufferCount;

        private readonly Stream stream;
        private readonly Encoding encoding;
        private readonly object gate = new object();
        private readonly char[][] free;
        private readonly char[][] queued;
        private readonly int[] queuedLength;
        private readonly byte[] bytes;
        private readonly Thread thread;
        private int freeCount;
        private int queueHead;
        private int queueCount;
        private bool closing;
        private Exception failure;

        private char[] current;
        private int currentLength;
        private long backpressureTicks;

        public BackgroundFileWriter(string path, int bufferChars = DefaultBufferChars, int bufferCount = DefaultBufferCount)
            : this(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16), Path.GetFileName(path), bufferChars, bufferCount)
        {
        }

        public BackgroundFileWriter(Stream stream, string name, int bufferChars = DefaultBufferChars, int bufferCount = DefaultBufferCount)
        {
            this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
            if (bufferChars < 1 || bufferCount < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(bufferCount), "at least 2 buffers of 1 char");
            }

            this.bufferChars = bufferChars;
            this.bufferCount = bufferCount;
            this.encoding = new UTF8Encoding(false);
            this.free = new char[bufferCount][];
            this.queued = new char[bufferCount][];
            this.queuedLength = new int[bufferCount];
            for (int i = 0; i < bufferCount - 1; i++)
            {
                this.free[this.freeCount++] = new char[bufferChars];
            }

            this.current = new char[bufferChars];
            this.bytes = new byte[this.encoding.GetMaxByteCount(bufferChars)];
            this.thread = new Thread(this.Run) { IsBackground = true, Name = "TestLab writer " + name };
            this.thread.Start();
        }

        public override Encoding Encoding => this.encoding;

        /// <summary>Total milliseconds the writing thread waited because every buffer was queued.</summary>
        public double BackpressureMs => Interlocked.Read(ref this.backpressureTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        public override void Write(char value)
        {
            if (this.currentLength == this.bufferChars)
            {
                this.Handoff();
            }

            this.current[this.currentLength++] = value;
        }

        public override void Write(string value)
        {
            if (value == null)
            {
                return;
            }

            int offset = 0;
            while (offset < value.Length)
            {
                if (this.currentLength == this.bufferChars)
                {
                    this.Handoff();
                }

                int n = Math.Min(value.Length - offset, this.bufferChars - this.currentLength);
                value.CopyTo(offset, this.current, this.currentLength, n);
                this.currentLength += n;
                offset += n;
            }
        }

        public override void Write(char[] buffer, int index, int count)
        {
            while (count > 0)
            {
                if (this.currentLength == this.bufferChars)
                {
                    this.Handoff();
                }

                int n = Math.Min(count, this.bufferChars - this.currentLength);
                Array.Copy(buffer, index, this.current, this.currentLength, n);
                this.currentLength += n;
                index += n;
                count -= n;
            }
        }

        /// <summary>Overridden so a line never goes through a concatenated temporary string.</summary>
        public override void WriteLine(string value)
        {
            this.Write(value);
            this.Write(this.CoreNewLine, 0, this.CoreNewLine.Length);
        }

        /// <summary>Hands the buffered characters to the writer thread; does not wait for the disk.</summary>
        public override void Flush()
        {
            if (this.current != null && this.currentLength > 0)
            {
                this.Handoff();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && this.current != null)
            {
                this.Flush();
                lock (this.gate)
                {
                    this.closing = true;
                    Monitor.PulseAll(this.gate);
                }

                this.thread.Join();
                this.current = null;
                if (this.failure != null)
                {
                    UnityEngine.Debug.LogWarning("[TestLab] background writer failed: " + this.failure.Message);
                }
            }

            base.Dispose(disposing);
        }

        private void Handoff()
        {
            lock (this.gate)
            {
                this.queued[(this.queueHead + this.queueCount) % this.bufferCount] = this.current;
                this.queuedLength[(this.queueHead + this.queueCount) % this.bufferCount] = this.currentLength;
                this.queueCount++;
                Monitor.PulseAll(this.gate);

                if (this.freeCount == 0)
                {
                    long start = System.Diagnostics.Stopwatch.GetTimestamp();
                    while (this.freeCount == 0)
                    {
                        Monitor.Wait(this.gate);
                    }

                    Interlocked.Add(ref this.backpressureTicks, System.Diagnostics.Stopwatch.GetTimestamp() - start);
                }

                this.current = this.free[--this.freeCount];
                this.currentLength = 0;
            }
        }

        private void Run()
        {
            try
            {
                while (true)
                {
                    char[] chunk;
                    int length;
                    lock (this.gate)
                    {
                        while (this.queueCount == 0 && !this.closing)
                        {
                            Monitor.Wait(this.gate);
                        }

                        if (this.queueCount == 0)
                        {
                            break;
                        }

                        chunk = this.queued[this.queueHead];
                        length = this.queuedLength[this.queueHead];
                    }

                    if (this.failure == null)
                    {
                        try
                        {
                            int n = this.encoding.GetBytes(chunk, 0, length, this.bytes, 0);
                            this.stream.Write(this.bytes, 0, n);
                            this.stream.Flush();
                        }
                        catch (Exception ex)
                        {
                            // Keep draining so the main thread never waits on a dead disk.
                            this.failure = ex;
                        }
                    }

                    lock (this.gate)
                    {
                        this.queued[this.queueHead] = null;
                        this.queueHead = (this.queueHead + 1) % this.bufferCount;
                        this.queueCount--;
                        this.free[this.freeCount++] = chunk;
                        Monitor.PulseAll(this.gate);
                    }
                }
            }
            finally
            {
                this.stream.Dispose();
            }
        }
    }
}
