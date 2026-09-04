namespace Tests.Runtime
{
    using System.Collections;
    using NUnit.Framework;
    using Scripts.Benchmark;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>
    /// The recorder over a real player loop: it samples frames, honors its config, reports
    /// through <see cref="BenchmarkRecorder.BuildReportNow"/>, and its JSON round-trips. No
    /// workload, no scene — the recorder's contract is that it works anywhere.
    /// </summary>
    public sealed class BenchmarkRecorderPlayModeTests
    {
        private GameObject host;

        [TearDown]
        public void TearDown()
        {
            if (this.host != null)
            {
                Object.Destroy(this.host);
            }
        }

        [UnityTest]
        public IEnumerator Recorder_SamplesFramesAndBuildsReport()
        {
            var config = ScriptableObject.CreateInstance<BenchmarkConfig>();
            config.WarmupSeconds = 0f;
            config.SettleSeconds = 0f;
            config.AutoQuit = false; // never let a test runner's player quit itself
            config.Phases = new[] { new BenchmarkPhase("test", 0, 3600f) };

            this.host = new GameObject("recorder-under-test");
            this.host.SetActive(false);
            var recorder = this.host.AddComponent<BenchmarkRecorder>();
            typeof(BenchmarkRecorder)
                .GetField("config", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(recorder, config);
            this.host.SetActive(true);

            for (var i = 0; i < 10; i++)
            {
                yield return null;
            }

            var report = recorder.BuildReportNow();

            Assert.That(report.TotalFrames, Is.GreaterThanOrEqualTo(5));
            Assert.That(report.Overall.FrameCount, Is.GreaterThanOrEqualTo(5));
            Assert.That(report.Overall.MeanMs, Is.GreaterThan(0f));
            Assert.That(report.Overall.MedianMs, Is.GreaterThan(0f));
            Assert.That(report.Phases, Has.Length.EqualTo(1));
            Assert.That(report.Phases[0].Label, Is.EqualTo("test"));
            Assert.That(report.Truncated, Is.False);
            Assert.That(report.UnityVersion, Is.EqualTo(Application.unityVersion));

            // The JSON the device run emits must round-trip: what logcat carries is what
            // tooling parses.
            var json = JsonUtility.ToJson(report);
            var roundTripped = JsonUtility.FromJson<BenchmarkReport>(json);
            Assert.That(roundTripped.TotalFrames, Is.EqualTo(report.TotalFrames));
            Assert.That(roundTripped.Overall.MeanMs, Is.EqualTo(report.Overall.MeanMs).Within(1e-5f));

            Object.Destroy(config);
        }
    }
}
