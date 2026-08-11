using System;
using System.Globalization;

namespace Scripts.Net.Transport
{
    /// <summary>A parsed <c>host:port</c> pair, as carried by <c>server_addr</c>.</summary>
    public readonly struct NetworkEndpoint
    {
        public NetworkEndpoint(string host, int port)
        {
            Host = host;
            Port = port;
        }

        public string Host { get; }

        public int Port { get; }

        public override string ToString() =>
            Host + ":" + Port.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Parses <c>host:port</c>. Splits on the last colon so a bracketed IPv6
        /// literal (<c>[::1]:9000</c>) parses too; the brackets are stripped,
        /// because <see cref="System.Net.Sockets.TcpClient"/> wants the bare address.
        /// </summary>
        public static NetworkEndpoint Parse(string address)
        {
            if (string.IsNullOrEmpty(address))
            {
                throw new TransportException("server address is empty");
            }

            var separator = address.LastIndexOf(':');
            if (separator <= 0 || separator == address.Length - 1)
            {
                throw new TransportException($"server address '{address}' is not host:port");
            }

            var host = address.Substring(0, separator);
            var portText = address.Substring(separator + 1);

            if (host.Length > 1 && host[0] == '[' && host[host.Length - 1] == ']')
            {
                host = host.Substring(1, host.Length - 2);
            }

            if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port) ||
                port <= 0 || port > 65535)
            {
                throw new TransportException($"server address '{address}' has an invalid port");
            }

            return new NetworkEndpoint(host, port);
        }
    }
}
