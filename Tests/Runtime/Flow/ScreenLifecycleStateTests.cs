namespace Cuvara.UIToolkit.Flow.Tests
{
    using System;
    using System.Linq;
    using Cuvara.UIToolkit.Flow;
    using Cuvara.UIToolkit.TestSupport;
    using NUnit.Framework;

    /// <summary>
    /// The state machine, and the transition table that says which moves are legal.
    /// </summary>
    /// <remarks>
    /// <para>Two things are being protected here, and neither is "does the enum have nine
    /// values".</para>
    ///
    /// <para>The first is that <b>a disposed screen never comes back</b>. In the framework this
    /// package replaces, closing and hiding both called <c>Dispose()</c> on an object that then
    /// kept living and was reopened — which is the root of the unregister-then-register
    /// boilerplate every screen there carried. Making that unrepresentable in the table is what
    /// lets a screen author write no teardown code at all.</para>
    ///
    /// <para>The second is that <b>failure paths are in the table</b>. A half-built screen must be
    /// able to reach <see cref="ScreenLifecycleState.Disposed"/> from wherever it broke, or the
    /// flow has no legal way to clean up after a failed load or a cancelled bind.</para>
    /// </remarks>
    public class ScreenLifecycleStateTests
    {
        #region The enum itself

        [Test]
        public void TheStatesAreOrderedByProgression()
        {
            // Ordering is load-bearing: `state < Active` means "still coming up" and reads that
            // way at call sites. Renumbering to slot something in the middle would silently
            // invert such comparisons.
            Assert.That((int)ScreenLifecycleState.Registered, Is.LessThan((int)ScreenLifecycleState.Creating));
            Assert.That((int)ScreenLifecycleState.Creating, Is.LessThan((int)ScreenLifecycleState.Constructed));
            Assert.That((int)ScreenLifecycleState.Constructed, Is.LessThan((int)ScreenLifecycleState.Binding));
            Assert.That((int)ScreenLifecycleState.Binding, Is.LessThan((int)ScreenLifecycleState.Opening));
            Assert.That((int)ScreenLifecycleState.Opening, Is.LessThan((int)ScreenLifecycleState.Active));
            Assert.That((int)ScreenLifecycleState.Active, Is.LessThan((int)ScreenLifecycleState.Closing));
            Assert.That((int)ScreenLifecycleState.Closing, Is.LessThan((int)ScreenLifecycleState.Disposed));
        }

        [Test]
        public void EveryStateHasATransitionRule()
        {
            // A state absent from the table is one AssertLegalSequence can never validate, so it
            // would silently accept anything leaving it.
            foreach (ScreenLifecycleState state in Enum.GetValues(typeof(ScreenLifecycleState)))
            {
                var reachable = Enum.GetValues(typeof(ScreenLifecycleState))
                    .Cast<ScreenLifecycleState>()
                    .Any(to => ScreenLifecycleRecorder.IsLegalTransition(state, to));

                if (state == ScreenLifecycleState.Disposed)
                {
                    Assert.That(reachable, Is.False, "Disposed must be terminal");
                    continue;
                }

                Assert.That(reachable, Is.True, $"{state} has no legal successor at all");
            }
        }

        #endregion

        #region Disposed is the end

        [Test]
        public void NothingIsLegalAfterDisposed()
        {
            // The single most important row in the table. A screen that resurrects is the hazard
            // this design is written against.
            foreach (ScreenLifecycleState to in Enum.GetValues(typeof(ScreenLifecycleState)))
            {
                Assert.That(ScreenLifecycleRecorder.IsLegalTransition(ScreenLifecycleState.Disposed, to), Is.False,
                    $"Disposed -> {to} must be illegal");
            }
        }

        [Test]
        public void AResurrectionIsRejected()
        {
            var recorder = new ScreenLifecycleRecorder();

            recorder.Record(ScreenLifecycleState.Closing);
            recorder.Record(ScreenLifecycleState.Disposed);
            recorder.Record(ScreenLifecycleState.Active);

            var exception = Assert.Throws<InvalidOperationException>(() => recorder.AssertLegalSequence());

            Assert.That(exception.Message, Does.Contain("Disposed -> Active"));
            Assert.That(exception.Message, Does.Contain("Full sequence"), "the offending pair alone rarely explains itself");
        }

        [Test]
        public void SuspendedIsNotDisposed()
        {
            // Suspended keeps its scope and resumes without rebinding; Disposed does not exist.
            // Conflating them is what produces a "hidden" screen that has been Dispose()d.
            Assert.That(ScreenLifecycleRecorder.IsLegalTransition(ScreenLifecycleState.Suspended, ScreenLifecycleState.Active), Is.True);
            Assert.That(ScreenLifecycleRecorder.IsLegalTransition(ScreenLifecycleState.Disposed, ScreenLifecycleState.Active), Is.False);
        }

        #endregion

        #region The happy path and the failure paths

        [Test]
        public void TheFullOpenAndCloseSequenceIsLegal()
        {
            var recorder = new ScreenLifecycleRecorder();

            foreach (var state in new[]
            {
                ScreenLifecycleState.Registered, ScreenLifecycleState.Creating, ScreenLifecycleState.Constructed,
                ScreenLifecycleState.Binding, ScreenLifecycleState.Opening, ScreenLifecycleState.Active,
                ScreenLifecycleState.Closing, ScreenLifecycleState.Disposed,
            })
            {
                recorder.Record(state);
            }

            Assert.DoesNotThrow(() => recorder.AssertLegalSequence());
        }

        [Test]
        public void SuspendAndResumeCyclesAreLegalAndCounted()
        {
            var recorder = new ScreenLifecycleRecorder();

            recorder.Record(ScreenLifecycleState.Active);
            recorder.Record(ScreenLifecycleState.Suspended);
            recorder.Record(ScreenLifecycleState.Active);
            recorder.Record(ScreenLifecycleState.Suspended);
            recorder.Record(ScreenLifecycleState.Active);

            Assert.DoesNotThrow(() => recorder.AssertLegalSequence());
            Assert.That(recorder.CountOf(ScreenLifecycleState.Active), Is.EqualTo(3));
            Assert.That(recorder.CountOf(ScreenLifecycleState.Suspended), Is.EqualTo(2));
        }

        [Test]
        public void EveryPreActiveStateCanTearDown()
        {
            // Not laxity. A failed load, a failed bind or a cancellation during either must have
            // a legal path to Disposed, or the flow cannot clean up after itself.
            foreach (var state in new[]
            {
                ScreenLifecycleState.Creating, ScreenLifecycleState.Constructed,
                ScreenLifecycleState.Binding, ScreenLifecycleState.Opening,
            })
            {
                Assert.That(ScreenLifecycleRecorder.IsLegalTransition(state, ScreenLifecycleState.Disposed), Is.True,
                    $"{state} must be able to tear down");
            }
        }

        [Test]
        public void AnActiveScreenMustCloseBeforeItDisposes()
        {
            // Active -> Disposed skips the outro and the detach. The flow must go through
            // Closing, which is where those happen.
            Assert.That(ScreenLifecycleRecorder.IsLegalTransition(ScreenLifecycleState.Active, ScreenLifecycleState.Disposed), Is.False);
            Assert.That(ScreenLifecycleRecorder.IsLegalTransition(ScreenLifecycleState.Active, ScreenLifecycleState.Closing), Is.True);
        }

        [Test]
        public void SkippingBindIsRejected()
        {
            var recorder = new ScreenLifecycleRecorder();

            recorder.Record(ScreenLifecycleState.Constructed);
            recorder.Record(ScreenLifecycleState.Active);

            Assert.Throws<InvalidOperationException>(() => recorder.AssertLegalSequence());
        }

        #endregion

        #region The recorder

        [Test]
        public void AnEmptyRecorderIsLegalAndReportsRegistered()
        {
            var recorder = new ScreenLifecycleRecorder();

            Assert.DoesNotThrow(() => recorder.AssertLegalSequence());
            Assert.That(recorder.Current, Is.EqualTo(ScreenLifecycleState.Registered));
            Assert.That(recorder.ToString(), Is.EqualTo("(nothing recorded)"));
        }

        [Test]
        public void ASingleStateIsLegal()
        {
            var recorder = new ScreenLifecycleRecorder();
            recorder.Record(ScreenLifecycleState.Creating);

            Assert.DoesNotThrow(() => recorder.AssertLegalSequence());
            Assert.That(recorder.Current, Is.EqualTo(ScreenLifecycleState.Creating));
        }

        [Test]
        public void ReachedAndCountOfReportWhatWasRecorded()
        {
            var recorder = new ScreenLifecycleRecorder();
            recorder.Record(ScreenLifecycleState.Creating);
            recorder.Record(ScreenLifecycleState.Constructed);

            Assert.That(recorder.Reached(ScreenLifecycleState.Creating), Is.True);
            Assert.That(recorder.Reached(ScreenLifecycleState.Active), Is.False);
            Assert.That(recorder.CountOf(ScreenLifecycleState.Creating), Is.EqualTo(1));
        }

        [Test]
        public void AssertSequenceComparesExactly()
        {
            var recorder = new ScreenLifecycleRecorder();
            recorder.Record(ScreenLifecycleState.Creating);
            recorder.Record(ScreenLifecycleState.Constructed);

            Assert.DoesNotThrow(() => recorder.AssertSequence(ScreenLifecycleState.Creating, ScreenLifecycleState.Constructed));
            Assert.Throws<InvalidOperationException>(() => recorder.AssertSequence(ScreenLifecycleState.Creating));
        }

        [Test]
        public void ClearForgetsEverything()
        {
            var recorder = new ScreenLifecycleRecorder();
            recorder.Record(ScreenLifecycleState.Active);

            recorder.Clear();

            Assert.That(recorder.States, Is.Empty);
            Assert.That(recorder.Current, Is.EqualTo(ScreenLifecycleState.Registered));
        }

        [Test]
        public void AssertSequenceRejectsNull()
        {
            Assert.Throws<ArgumentNullException>(() => new ScreenLifecycleRecorder().AssertSequence(null));
        }

        #endregion

        #region ScreenOptions

        [Test]
        public void ScreenOptionsCombineAsFlags()
        {
            var options = ScreenOptions.Modal | ScreenOptions.DimsBelow;

            Assert.That(options.HasFlag(ScreenOptions.Modal), Is.True);
            Assert.That(options.HasFlag(ScreenOptions.DimsBelow), Is.True);
        }

        [Test]
        public void EveryScreenOptionHasADistinctBit()
        {
            // A duplicated bit makes two flags indistinguishable, and the symptom is one option
            // silently enabling another.
            var values = Enum.GetValues(typeof(ScreenOptions)).Cast<ScreenOptions>().Where(v => v != ScreenOptions.None).ToList();

            Assert.That(values.Select(v => (int)v).Distinct().Count(), Is.EqualTo(values.Count));

            foreach (var value in values)
            {
                // .NET Standard 2.1 has no BitOperations.PopCount, and a single-bit test does
                // not need one: a power of two ANDed with its predecessor is zero.
                var raw = (int)value;
                Assert.That(raw & (raw - 1), Is.Zero, $"{value} is not a single bit");
            }
        }

        [Test]
        public void EveryDeclaredOptionIsReadSomewhereInTheRuntime()
        {
            // Rule 5, made mechanical: a member of this enum that no runtime code reads is an
            // inert flag, and an author who sets it gets no behaviour and no diagnostic. The
            // check is deliberately crude — it looks for the member's NAME in the flow's source
            // — because the alternative is trusting that someone noticed.
            var runtimeSource = string.Concat(System.IO.Directory.GetFiles(
                System.IO.Path.Combine(UnityEngine.Application.dataPath, "..", "Packages", "com.cuvara.uitoolkit", "Runtime", "Flow"),
                "*.cs", System.IO.SearchOption.AllDirectories).Select(System.IO.File.ReadAllText));

            foreach (var value in Enum.GetValues(typeof(ScreenOptions)).Cast<ScreenOptions>())
            {
                if (value == ScreenOptions.None) continue;

                Assert.That(runtimeSource, Does.Contain($"ScreenOptions.{value}"),
                    $"{value} is declared but no runtime code reads it — an inert flag looks configured and does nothing.");
            }
        }

        [Test]
        public void NoneIsTheDefault()
        {
            Assert.That(default(ScreenOptions), Is.EqualTo(ScreenOptions.None));
        }

        #endregion
    }
}
