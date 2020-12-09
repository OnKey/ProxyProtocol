using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

[assembly: InternalsVisibleTo("ProxyProtocolTest")]
namespace HAProxy
{
    /// <summary>
    /// Implements proxy protocol v2 https://www.haproxy.org/download/1.8/doc/proxy-protocol.txt
    /// Which can be used to read the original source IP and port where the service is behind a load
    /// balancer implementing proxy protocol v2 i.e. HAProxy or AWS NLB
    /// </summary>
    public class ProxyProtocol
    {
        private static byte[] ProxyProtocolV2Signature = { 0x0D, 0x0A, 0x0D, 0x0A, 0x00, 0x0D, 0x0A, 0x51, 0x55, 0x49, 0x54, 0x0A };
        private IPEndPoint remotEndPoint;
        private Stream stream;
        internal byte[] readBuffer = new byte[100];
        private int bytesRead = 0;

        public ProxyProtocol(TcpClient client)
        {
            this.remotEndPoint = client.Client.RemoteEndPoint as IPEndPoint;
            this.stream = client.GetStream();
        }

        internal ProxyProtocol(Stream stream, IPEndPoint remotEndPoint)
        {
            this.stream = stream;
            this.remotEndPoint = remotEndPoint;
        }

        public async Task<IPEndPoint> GetRemoteEndpoint()
        {
            await this.GetProxyProtocolHeader();
            if (GetCommand(this.readBuffer) == "LOCAL")
            {
                return this.remotEndPoint;
            }

            if (GetAddressFamily(this.readBuffer) == AddressFamily.InterNetwork)
            {
                return new IPEndPoint(GetSourceAddressIpv4(this.readBuffer), GetSourcePortIpv4(this.readBuffer));
            }
            else if (GetAddressFamily(this.readBuffer) == AddressFamily.InterNetworkV6)
            {
                return new IPEndPoint(GetSourceAddressIpv6(this.readBuffer), GetSourcePortIpv6(this.readBuffer));
            }
            else
            {
                throw new NotImplementedException("Address family of connection not supported");
            }
        }

        internal async Task GetProxyProtocolHeader()
        {
            if (this.bytesRead != 0)
            {
                return;
            }
            await this.ReadNextBytesInToBuffer(16);
            await this.CheckSignature();
            var length = GetLength(this.readBuffer);
            if (length != 0)
            {
                await this.ReadNextBytesInToBuffer(length);
            }
        }

        private async Task ReadNextBytesInToBuffer(int length)
        {
            if (this.bytesRead + length > this.readBuffer.Length)
            {
                Array.Resize(ref this.readBuffer, this.bytesRead + length);
            }

            await this.stream.ReadAsync(this.readBuffer, this.bytesRead, length);
            this.bytesRead += length;
        }

        private async Task CheckSignature()
        {
            if (HasProxyProtocolSignature(this.readBuffer) && IsProtocolV2(this.readBuffer))
            {
                return;
            }

            throw new ArgumentException("Not Proxy Protocol v2");
        }

        internal static string GetCommand(byte[] header)
        {
            var version = header[12];
            return (version & 0x0F) == 0x01 ? "PROXY" : "LOCAL";
        }

        internal static bool IsProtocolV2(byte[] header)
        {
            var version = header[12];
            return (version & 0xF0) == 0x20;
        }

        internal static AddressFamily GetAddressFamily(byte[] header)
        {
            var family = header[13] & 0xF0;
            switch (family)
            {
                case 0x00:
                    return AddressFamily.Unspecified;
                case 0x10:
                    return AddressFamily.InterNetwork;
                case 0x20:
                    return AddressFamily.InterNetworkV6;
                case 0x30:
                    return AddressFamily.Unix;
                default:
                    throw new ArgumentException("Invalid address family");
            }
        }

        internal static bool HasProxyProtocolSignature(byte[] signatureBytes)
        {
            return signatureBytes.Length >= ProxyProtocolV2Signature.Length && signatureBytes.Take(ProxyProtocolV2Signature.Length).SequenceEqual(ProxyProtocolV2Signature);
        }

        internal static int GetLength(byte[] header) => BytesToUInt16(header.Skip(14).Take(2).ToArray());

        internal static IPAddress GetSourceAddressIpv4(byte[] header) => new IPAddress(header.Skip(16).Take(4).ToArray());

        internal static IPAddress GetDestinationAddressIpv4(byte[] header) => new IPAddress(header.Skip(16 + 4).Take(4).ToArray());

        internal static int GetSourcePortIpv4(byte[] header) => BytesToUInt16(header.Skip(16 + 8).Take(2).ToArray());

        internal static int GetDestinationPortIpv4(byte[] header) => BytesToUInt16(header.Skip(16 + 8 + 2).Take(2).ToArray());

        internal static IPAddress GetSourceAddressIpv6(byte[] header) => new IPAddress(header.Skip(16).Take(16).ToArray());

        internal static IPAddress GetDestinationAddressIpv6(byte[] header) => new IPAddress(header.Skip(16 + 16).Take(16).ToArray());

        internal static int GetSourcePortIpv6(byte[] header) => BytesToUInt16(header.Skip(16 + 32).Take(2).ToArray());

        internal static int GetDestinationPortIpv6(byte[] header) => BytesToUInt16(header.Skip(16 + 32 + 2).Take(2).ToArray());

        private static int BytesToUInt16(byte[] bytes)
        {
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            return BitConverter.ToUInt16(bytes, 0);
        }
    }
}
