using System;
using System.IO;
using Cuvara.Netcode.Prediction;
using NUnit.Framework;
using Shared.GameLogic.Components;
using TestLab;
using UnityEngine.Profiling;

namespace Tests.Editor
{
    /// <summary>
    /// The TestLab's predictor call trace (netcode &gt;= 8986908): every line is one well-formed JSON
    /// object, events round-trip through netcode's own parser, nothing is written when the trace is
    /// off, and the per-frame cost does not grow with the run.
    /// </summary>
    public sealed class PredictorTraceRecorderTests
    {
        private static string[] Lines(StringWriter w) =>
            w.ToString().Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

        [Test]
        public void ALivePredictor_WritesHeaderFramesAndEvents_ThatParseBack()
        {
            var output = new StringWriter();
            var recorder = new PredictorTraceRecorder(output);
            var settings = new PredictionSettings(60, 6f, MapBounds.Default);
            var predictor = new LocalMovePredictor(settings);

            Assert.That(PredictorTraceRecorder.TryReadSettings(predictor, out var read), Is.True);
            Assert.That(read.TickRate, Is.EqualTo(60));
            recorder.WriteHeader("dev_arena", true, read, predictor.UsesCharacterMotor);
            predictor.Trace = recorder.Sink;

            for (int frame = 1; frame <= 10; frame++)
            {
                recorder.WriteFrame(frame, frame / 60.0, 1 / 60.0, 1 / 60.0);
                predictor.RecordInput(frame, 1f, 0f, false);
                predictor.Advance(1f / 60f);
            }

            var lines = Lines(output);
            Assert.That(lines[0], Does.StartWith("{\"kind\":\"header\",\"schema\":\"rpg.testlab.predictor-trace.v1\""));
            Assert.That(lines[0], Does.Contain("\"map\":\"dev_arena\"").And.Contain("\"tick_rate\":60").And.Contain("\"speed\":6"));
            Assert.That(recorder.Frames, Is.EqualTo(10));
            Assert.That(recorder.Events, Is.GreaterThanOrEqualTo(20), "RecordInput and Advance are traced every frame");

            int frames = 0;
            int events = 0;
            foreach (string line in lines)
            {
                Assert.That(line, Does.StartWith("{").And.EndWith("}"), line);
                if (line.StartsWith("{\"frame\":", StringComparison.Ordinal))
                {
                    frames++;
                }
                else if (line.StartsWith("{\"c\":", StringComparison.Ordinal))
                {
                    Assert.That(PredictorTraceEvent.TryParseJson(line, out var e), Is.True, line);
                    Assert.That(e.Call, Is.Not.EqualTo((PredictorCall)0));
                    events++;
                }
            }

            Assert.That(frames, Is.EqualTo(10));
            Assert.That(events, Is.EqualTo(recorder.Events));
        }

        [Test]
        public void Off_WritesNothing()
        {
            var plan = TestLabPlan.Parse("{\"schema\":\"rpg.testlab.plan.v1\",\"scenario_id\":\"t\"}");
            Assert.That(plan.predictor_trace, Is.False, "the trace is opt-in");
            Assert.That(TestLabPlan.Parse("{\"schema\":\"rpg.testlab.plan.v1\",\"scenario_id\":\"t\",\"predictor_trace\":true}").predictor_trace,
                Is.True);

            // A predictor without a sink, and a closed recorder, produce no line.
            var output = new StringWriter();
            var recorder = new PredictorTraceRecorder(output);
            var predictor = new LocalMovePredictor(new PredictionSettings(60, 6f, MapBounds.Default));
            predictor.RecordInput(1, 1f, 0f, false);
            recorder.Closed = true;
            predictor.Trace = recorder.Sink;
            predictor.RecordInput(2, 1f, 0f, false);
            recorder.WriteFrame(1, 0, 0, 0);
            Assert.That(output.ToString(), Is.Empty);
        }

        [Test]
        public void PerFrameAllocations_DoNotGrowWithTheRun()
        {
            var writer = new BackgroundFileWriter(Stream.Null, "trace-test", 64 * 1024, 4);
            var recorder = new PredictorTraceRecorder(writer);
            var predictor = new LocalMovePredictor(new PredictionSettings(60, 6f, MapBounds.Default)) { Trace = recorder.Sink };
            int tick = 0;

            void Frames(int n)
            {
                for (int i = 0; i < n; i++)
                {
                    tick++;
                    recorder.WriteFrame(tick, tick / 60.0, 1 / 60.0, 1 / 60.0);
                    predictor.RecordInput(tick, 1f, 0f, false);
                    predictor.Advance(1f / 60f);
                }
            }

            Frames(200); // warm up
            double early = CountAllocations(() => Frames(1_000)) / 1_000.0;
            Frames(20_000);
            double late = CountAllocations(() => Frames(1_000)) / 1_000.0;
            writer.Dispose();

            Assert.That(late, Is.LessThanOrEqualTo((early * 1.1) + 0.1),
                $"allocations per frame grew with the run: {early:0.##} early, {late:0.##} after 20k frames");
        }

        private static int CountAllocations(Action action)
        {
            var recorder = Recorder.Get("GC.Alloc");
            recorder.enabled = false;
            recorder.FilterToCurrentThread();
            recorder.enabled = true;
            try
            {
                action();
            }
            finally
            {
                recorder.enabled = false;
                recorder.CollectFromAllThreads();
            }

            return recorder.sampleBlockCount;
        }
    }
}
