using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

[assembly: InternalsVisibleTo("ProxyProtocolTest")]
namespace ProxyProtocol
{
    /// <summary>
    /// Implements proxy protocol v2 https://www.haproxy.org/download/1.8/doc/proxy-protocol.txt
    /// Which can be used to read the original source IP and port where the service is behind a load
    /// balancer implementing proxy protocol v2 i.e. HAProxy or AWS NLB
    /// </summary>
    public class ProxyProtocol
    {
        private static byte[] proxyProtocolV2Signature = { 0x0D, 0x0A, 0x0D, 0x0A, 0x00, 0x0D, 0x0A, 0x51, 0x55, 0x49, 0x54, 0x0A };
        private const int SignatureLength = 16;
        private const int Ipv4Length = 4;
        private const int Ipv6Length = 16;
        private const int MinPacketSize = 536;
        private IPEndPoint remoteEndPoint;
        private Stream stream;
        private byte[] readBuffer;
        private int bytesRead;

        public ProxyProtocol(Stream stream, IPEndPoint remoteEndPoint, int readBufferSize = MinPacketSize)
        {
            this.stream = stream;
            this.remoteEndPoint = remoteEndPoint;
            // We need to make sure the buffer is large enough that when using Proxy Protocol we get the whole
            // signature on the first read
            this.readBuffer = new byte[readBufferSize < MinPacketSize ? MinPacketSize : readBufferSize];
        }

        public async Task<IPEndPoint> GetRemoteEndpoint()
        {
            if (!await this.IsProxyProtocolV2() || await this.IsLocalConnection())
            {
                return this.remoteEndPoint;
            }

            if (GetAddressFamily(this.readBuffer) == AddressFamily.InterNetwork)
            {
                return new IPEndPoint(GetSourceAddressIpv4(this.readBuffer), GetSourcePortIpv4(this.readBuffer));
            }

            if (GetAddressFamily(this.readBuffer) == AddressFamily.InterNetworkV6)
            {
                return new IPEndPoint(GetSourceAddressIpv6(this.readBuffer), GetSourcePortIpv6(this.readBuffer));
            }

            throw new NotImplementedException("Address family of connection not supported");
        }

        public async Task<bool> IsLocalConnection() => await this.IsProxyProtocolV2() 
                                                       && GetCommand(this.readBuffer) == "LOCAL";

        public async Task<bool> IsProxyProtocolV2()
        {
            await this.GetProxyProtocolHeader();
            return HasProxyProtocolSignature(this.readBuffer) && IsProtocolV2(this.readBuffer);
        }

        internal async Task GetProxyProtocolHeader()
        {
            if (this.bytesRead > 0)
            {
                return;
            }

            await this.ReadNextBytesInToBuffer(this.readBuffer.Length);
        }

        internal async Task<byte[]> GetBytesWithoutProxyHeader() =>
            this.readBuffer.Skip(await this.GetProxyHeaderLength()).ToArray();

        internal async Task<int> GetProxyHeaderLength() => 
            await this.IsProxyProtocolV2() ? SignatureLength + GetLength(this.readBuffer) : 0;

        internal async Task<int> GetLengthWithoutProxyHeader() => this.bytesRead - await this.GetProxyHeaderLength();

        private async Task ReadNextBytesInToBuffer(int length)
        {
            if (this.bytesRead + length > this.readBuffer.Length)
            {
                Array.Resize(ref this.readBuffer, this.bytesRead + length);
            }
            
            this.bytesRead += await this.stream.ReadAsync(this.readBuffer, this.bytesRead, length);
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

        internal static bool HasProxyProtocolSignature(byte[] signatureBytes) => 
            signatureBytes.Length >= proxyProtocolV2Signature.Length && 
            signatureBytes.Take(proxyProtocolV2Signature.Length).SequenceEqual(proxyProtocolV2Signature);

        internal static int GetLength(byte[] header) => 
            BytesToUInt16(header.Skip(SignatureLength - 2).Take(2).ToArray());

        internal static IPAddress GetSourceAddressIpv4(byte[] header) => 
            new(header.Skip(SignatureLength).Take(Ipv4Length).ToArray());

        internal static IPAddress GetDestinationAddressIpv4(byte[] header) => 
            new(header.Skip(SignatureLength + Ipv4Length).Take(Ipv4Length).ToArray());

        internal static int GetSourcePortIpv4(byte[] header) => 
            BytesToUInt16(header.Skip(SignatureLength + 2* Ipv4Length).Take(2).ToArray());

        internal static int GetDestinationPortIpv4(byte[] header) => 
            BytesToUInt16(header.Skip(SignatureLength + 2* Ipv4Length + 2).Take(2).ToArray());

        internal static IPAddress GetSourceAddressIpv6(byte[] header) => 
            new(header.Skip(SignatureLength).Take(Ipv6Length).ToArray());

        internal static IPAddress GetDestinationAddressIpv6(byte[] header) => 
            new(header.Skip(SignatureLength + Ipv6Length).Take(Ipv6Length).ToArray());

        internal static int GetSourcePortIpv6(byte[] header) => 
            BytesToUInt16(header.Skip(SignatureLength + 2 * Ipv6Length).Take(2).ToArray());

        internal static int GetDestinationPortIpv6(byte[] header) => 
            BytesToUInt16(header.Skip(SignatureLength + 2 * Ipv6Length + 2).Take(2).ToArray());

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
