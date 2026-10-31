namespace Scripts.DI
{
    using System;
    using System.Threading.Tasks;
    using Cysharp.Threading.Tasks;
    using UnityEngine;
    using VContainer.Unity;

    /// <summary>
    /// Root-scoped hook that logs exceptions nobody observed: a faulted <c>UniTask</c> that was
    /// forgotten, and a faulted <see cref="Task"/> collected without being awaited.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>No double logging.</b> UniTask 2.5.x logs an unobserved exception itself
    /// (<c>UniTaskScheduler.UnobservedExceptionWriteLogType</c>, default <c>Exception</c>) only when
    /// <see cref="UniTaskScheduler.UnobservedTaskException"/> has no subscriber. Subscribing here
    /// therefore replaces that default line with exactly one <c>[Unobserved]</c> line. An
    /// <see cref="OperationCanceledException"/> is ignored on both paths:
    /// <c>UniTaskScheduler.PropagateOperationCanceledException</c> is false by default so UniTask
    /// never raises it, and the <see cref="TaskScheduler"/> handler skips it for the same reason.
    /// </para>
    /// <para>
    /// <see cref="TaskScheduler.UnobservedTaskException"/> has no default logging in Unity, so it
    /// is not duplicated; it fires on the finalizer thread some time after the fault, hence the
    /// message names the source. The exception is not marked observed.
    /// </para>
    /// </remarks>
    public sealed class UnobservedExceptionReporter : IStartable, IDisposable
    {
        public const string Prefix = "[Unobserved]";

        private bool subscribed;

        public bool IsSubscribed => this.subscribed;

        public void Start()
        {
            if (this.subscribed) return;
            this.subscribed = true;
            UniTaskScheduler.UnobservedTaskException += this.OnUniTaskUnobserved;
            TaskScheduler.UnobservedTaskException += this.OnTaskUnobserved;
        }

        public void Dispose()
        {
            if (!this.subscribed) return;
            this.subscribed = false;
            UniTaskScheduler.UnobservedTaskException -= this.OnUniTaskUnobserved;
            TaskScheduler.UnobservedTaskException -= this.OnTaskUnobserved;
        }

        internal void OnUniTaskUnobserved(Exception exception) => Report("UniTask", exception);

        internal void OnTaskUnobserved(object sender, UnobservedTaskExceptionEventArgs args)
        {
            if (args?.Exception == null) return;
            var flat = args.Exception.Flatten();
            if (flat.InnerExceptions.Count == 1 && flat.InnerException is OperationCanceledException) return;
            Report("Task", flat);
        }

        private static void Report(string source, Exception exception)
        {
            if (exception == null) return;
            Debug.LogError($"{Prefix} {source}: {exception}");
        }
    }
}
