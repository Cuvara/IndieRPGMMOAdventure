using System;

namespace Scripts.Net.Diagnostics
{
    /// <summary>
    /// Logging seam for the networking layer, so that everything except
    /// <see cref="UnityNetLog"/> stays free of engine types and can be exercised
    /// outside the Editor.
    /// </summary>
    public interface INetLog
    {
        void Info(string message);

        void Warn(string message);

        void Error(string message, Exception exception = null);
    }
}
