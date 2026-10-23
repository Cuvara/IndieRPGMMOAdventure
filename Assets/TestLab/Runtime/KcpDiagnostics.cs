namespace TestLab
{
    using System.Globalization;
    using System.Reflection;
    using System.Text;
    using Cuvara.Netcode.Transport;

    /// <summary>
    /// Read-only state of one gameplay KCP transport, for <c>summary.json</c> and the
    /// <c>session_closed</c> event: why a session failed and what its retransmission timer looked
    /// like at the time (the T57 dead-link analysis had to infer both). Test-only.
    /// </summary>
    /// <remarks>
    /// <c>KcpTransport.FailureReason</c> is public. The RTO and the silence counters live on its
    /// internal <c>KcpClientSession</c> (netcode &gt;= 4520ccf: <c>RtoMs</c>, <c>AwakeSilentMs</c>,
    /// <c>SilenceFailures</c>, <c>PendingSendSegments</c>), read here by reflection, by name. A member
    /// a netcode version lacks is written as <c>null</c>, never as zero. Reflection allocates; this is
    /// read at a session close and once at the end of the run, never per frame.
    /// </remarks>
    public static class KcpDiagnostics
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>
        /// Appends <c>"present":..,"failure_reason":..,"rto_ms":..,"awake_silent_ms":..,
        /// "silence_failures":..,"pending_send_segments":..,"datagrams_received":..</c> (no braces).
        /// </summary>
        public static void AppendFields(StringBuilder sb, KcpTransport transport)
        {
            sb.Append("\"present\":").Append(transport != null ? "true" : "false");
            object session = transport != null ? SessionOf(transport) : null;
            sb.Append(",\"failure_reason\":").Append(EntityLifecycle.Quote(transport?.FailureReason))
                .Append(",\"session\":").Append(session != null ? "true" : "false")
                .Append(",\"rto_ms\":").Append(Value(session, "RtoMs"))
                .Append(",\"awake_silent_ms\":").Append(Value(session, "AwakeSilentMs"))
                .Append(",\"silence_failures\":").Append(Value(session, "SilenceFailures"))
                .Append(",\"pending_send_segments\":").Append(Value(session, "PendingSendSegments"))
                .Append(",\"datagrams_received\":").Append(transport != null ? transport.DatagramsReceived.ToString(Inv) : "null");
        }

        private static object SessionOf(KcpTransport transport)
        {
            foreach (var f in typeof(KcpTransport).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (f.FieldType.Name == "KcpClientSession")
                {
                    return f.GetValue(transport);
                }
            }

            return null;
        }

        private static string Value(object target, string property)
        {
            if (target == null)
            {
                return "null";
            }

            var p = target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p == null)
            {
                return "null";
            }

            try
            {
                object v = p.GetValue(target);
                return v == null ? "null" : string.Format(Inv, "{0}", v);
            }
            catch (TargetInvocationException)
            {
                // A disposed session may refuse; say so rather than guess.
                return "null";
            }
        }
    }
}
