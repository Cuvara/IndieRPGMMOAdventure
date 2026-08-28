namespace Cuvara.UIToolkit.TestSupport
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Cuvara.UIToolkit.Flow;

    /// <summary>
    /// Records the states a screen passed through, and can say whether that sequence was legal.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this ships in <c>Runtime/</c> rather than living in the package's own tests.</b>
    /// A consumer writing a screen needs to assert its lifecycle, and the alternative is that
    /// every consumer writes this class slightly differently and each one encodes a slightly
    /// different idea of what is legal. The transition table belongs with the state machine, and
    /// this is the state machine's table expressed as an assertion.</para>
    ///
    /// <para>It has no test-framework dependency — no NUnit, no <c>UnityEngine.TestRunner</c> — so
    /// it can be referenced from a runtime assembly, a test assembly, or a debug overlay. It
    /// reports failures by throwing <see cref="InvalidOperationException"/> with the full recorded
    /// sequence, because "expected Active but was Suspended" is not useful without the path that
    /// got there.</para>
    /// </remarks>
    public sealed class ScreenLifecycleRecorder
    {
        /// <summary>
        /// The legal successors of each state, as the flow defines them.
        /// </summary>
        /// <remarks>
        /// <para>Read this as the authoritative transition table. Two properties are worth naming
        /// because they are the ones most likely to be broken by a well-meaning change:</para>
        /// <list type="bullet">
        /// <item><see cref="ScreenLifecycleState.Disposed"/> has no successors at all. A screen
        /// that comes back from disposal is the hazard this whole design is written against —
        /// in the framework this replaces, close and hide both disposed an object that then
        /// kept living.</item>
        /// <item>Every pre-<see cref="ScreenLifecycleState.Active"/> state can go straight to
        /// <see cref="ScreenLifecycleState.Disposed"/>. That is not laxity: a failed load, a
        /// failed bind, or a cancellation during either must tear down a half-built screen, and
        /// the table has to permit the path that failure actually takes.</item>
        /// </list>
        /// </remarks>
        private static readonly IReadOnlyDictionary<ScreenLifecycleState, ScreenLifecycleState[]> Legal =
            new Dictionary<ScreenLifecycleState, ScreenLifecycleState[]>
            {
                [ScreenLifecycleState.Registered]  = new[] { ScreenLifecycleState.Creating },
                [ScreenLifecycleState.Creating]    = new[] { ScreenLifecycleState.Constructed, ScreenLifecycleState.Disposed },
                [ScreenLifecycleState.Constructed] = new[] { ScreenLifecycleState.Binding, ScreenLifecycleState.Disposed },
                [ScreenLifecycleState.Binding]     = new[] { ScreenLifecycleState.Opening, ScreenLifecycleState.Disposed },
                [ScreenLifecycleState.Opening]     = new[] { ScreenLifecycleState.Active, ScreenLifecycleState.Disposed },
                [ScreenLifecycleState.Active]      = new[] { ScreenLifecycleState.Suspended, ScreenLifecycleState.Closing },
                [ScreenLifecycleState.Suspended]   = new[] { ScreenLifecycleState.Active, ScreenLifecycleState.Closing },
                [ScreenLifecycleState.Closing]     = new[] { ScreenLifecycleState.Disposed },
                [ScreenLifecycleState.Disposed]    = Array.Empty<ScreenLifecycleState>(),
            };

        private readonly List<ScreenLifecycleState> states = new();

        /// <summary>Every state recorded, in order.</summary>
        public IReadOnlyList<ScreenLifecycleState> States => this.states;

        /// <summary>The most recent state, or <see cref="ScreenLifecycleState.Registered"/> if nothing was recorded.</summary>
        public ScreenLifecycleState Current => this.states.Count == 0 ? ScreenLifecycleState.Registered : this.states[^1];

        /// <summary>Records a transition into <paramref name="state"/>.</summary>
        public void Record(ScreenLifecycleState state) { this.states.Add(state); }

        /// <summary>True if <paramref name="state"/> was recorded at any point.</summary>
        public bool Reached(ScreenLifecycleState state) => this.states.Contains(state);

        /// <summary>How many times <paramref name="state"/> was entered. Useful for suspend/resume cycles.</summary>
        public int CountOf(ScreenLifecycleState state) => this.states.Count(recorded => recorded == state);

        /// <summary>Whether <paramref name="from"/> may legally be followed by <paramref name="to"/>.</summary>
        public static bool IsLegalTransition(ScreenLifecycleState from, ScreenLifecycleState to)
        {
            return Legal.TryGetValue(from, out var successors) && Array.IndexOf(successors, to) >= 0;
        }

        /// <summary>Throws if any recorded transition was illegal.</summary>
        /// <exception cref="InvalidOperationException">
        /// A transition in the recorded sequence is not in the table. The message carries the
        /// offending pair AND the whole sequence, because the pair alone rarely explains itself.
        /// </exception>
        public void AssertLegalSequence()
        {
            for (var i = 1; i < this.states.Count; ++i)
            {
                var from = this.states[i - 1];
                var to   = this.states[i];

                if (IsLegalTransition(from, to)) continue;

                throw new InvalidOperationException(
                    $"Illegal screen lifecycle transition {from} -> {to} at index {i}. Full sequence: {this}");
            }
        }

        /// <summary>Throws unless the recorded sequence is exactly <paramref name="expected"/>.</summary>
        public void AssertSequence(params ScreenLifecycleState[] expected)
        {
            if (expected == null) throw new ArgumentNullException(nameof(expected));

            if (!this.states.SequenceEqual(expected))
            {
                throw new InvalidOperationException(
                    $"Expected screen lifecycle [{string.Join(" -> ", expected)}] but recorded [{string.Join(" -> ", this.states)}].");
            }
        }

        /// <summary>Forgets everything recorded.</summary>
        public void Clear() { this.states.Clear(); }

        public override string ToString() => this.states.Count == 0 ? "(nothing recorded)" : string.Join(" -> ", this.states);
    }
}
