using System;
using System.Threading;

namespace Scripts.Nakama.Auth
{
    /// <summary>
    /// A monotonically increasing operation counter that turns "the newest login
    /// wins" into a check any async step can make after it resumes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Nakama SDK cannot always abandon an HTTP call that is already in flight,
    /// and even where it can, a call that completes in the same frame as a cancel
    /// still delivers its result. The gap that matters is between the call
    /// completing and its result being applied: a login the player backed out of,
    /// or a login superseded by a newer one, must not become <c>Session</c>. Each
    /// login <see cref="Begin"/>s a generation before its first await and calls
    /// <see cref="ThrowIfStale"/> after each one; sign-out <see cref="Invalidate"/>s.
    /// </para>
    /// <para>
    /// Pure C#, no Unity or Nakama types — so it is what the EditMode tests pin.
    /// </para>
    /// </remarks>
    public sealed class OperationGeneration
    {
        int _current;

        /// <summary>The generation that currently owns the outcome.</summary>
        public int Current => Volatile.Read(ref _current);

        /// <summary>Starts a new operation; everything begun earlier is now stale.</summary>
        public int Begin() => Interlocked.Increment(ref _current);

        /// <summary>Makes every operation in flight stale without starting a new one.</summary>
        public void Invalidate() => Interlocked.Increment(ref _current);

        public bool IsCurrent(int generation) => generation == Current;

        /// <summary>
        /// Call after every await, before any state is written. The caller's token
        /// is checked first — a cancel is reported as a cancel even when a newer
        /// operation also superseded this one — then the generation.
        /// </summary>
        /// <exception cref="OperationCanceledException">
        /// Cancelled, or superseded by a newer operation. Callers treat both alike:
        /// discard the result, apply nothing.
        /// </exception>
        public void ThrowIfStale(int generation, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!IsCurrent(generation))
            {
                throw new OperationCanceledException(
                    $"operation {generation} was superseded by operation {Current}; its result is discarded");
            }
        }
    }
}
