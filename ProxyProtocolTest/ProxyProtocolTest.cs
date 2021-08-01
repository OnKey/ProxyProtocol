using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ProxyProtocolTest
{
    [TestClass]
    public class ProxyProtocolTest
    {
        private static readonly byte[] ExampleHeader =
        {
            0x0d, 0x0a, 0x0d, 0x0a, 0x00, 0x0d, 0x0a, 0x51, 0x55, 0x49, 0x54, 0x0a, 0x21, 0x11, 0x00, 0x54, 0x7b, 0x67,
            0xc9, 0x9a, 0xac, 0x1f, 0x0c, 0x37, 0xb1, 0x16, 0x02, 0x4b, 0x03, 0x00, 0x04, 0x08, 0x94, 0x22, 0x07, 0x04,
            0x00, 0x3e, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };

        internal static byte[] GetProxyHeaderClone() => (byte[])ExampleHeader.Clone();
        
        [TestMethod]
        public void ShouldRecogniseSignature()
        {
            Assert.IsTrue(ProxyProtocol.ProxyProtocol.HasProxyProtocolSignature(ExampleHeader));
        }

        [TestMethod]
        public void ShouldRecogniseInvalidSignature()
        {
            var header = GetProxyHeaderClone();
            header[2] = 0x0b;
            Assert.IsFalse(ProxyProtocol.ProxyProtocol.HasProxyProtocolSignature(header));
        }

        [TestMethod]
        public void ShouldIdentifyV2()
        {
            Assert.IsTrue(ProxyProtocol.ProxyProtocol.IsProtocolV2(ExampleHeader));
        }

        [TestMethod]
        public void ShouldIdentifyV1()
        {
            var header = GetProxyHeaderClone();
            header[12] = 0x10;
            Assert.IsFalse(ProxyProtocol.ProxyProtocol.IsProtocolV2(header));
        }

        [TestMethod]
        public void ShouldDetectProxyVLocal()
        {
            Assert.AreEqual("PROXY", ProxyProtocol.ProxyProtocol.GetCommand(ExampleHeader));
            
            var header = GetProxyHeaderClone();
            header[12] = 0x22;
            Assert.AreEqual("LOCAL", ProxyProtocol.ProxyProtocol.GetCommand(header));
        }

        [TestMethod]
        public void ShouldDetectAddressFamily()
        {
            Assert.AreEqual(AddressFamily.InterNetwork, ProxyProtocol.ProxyProtocol.GetAddressFamily(ExampleHeader));
        }

        [TestMethod]
        public void ShouldReadLength()
        {
            Assert.AreEqual(84, ProxyProtocol.ProxyProtocol.GetLength(ExampleHeader));
        }

        [TestMethod]
        public void ShouldReadStreamToEndOfHeader()
        {
            var stream = new MemoryStream(GetProxyHeaderClone());
            var proxy = new ProxyProtocol.ProxyProtocol(stream, null);
            proxy.GetProxyProtocolHeader().Wait();

            Assert.AreEqual(101, stream.Position);
        }

        [TestMethod]
        public void CanGetSourceIP()
        {
            Assert.AreEqual("123.103.201.154", ProxyProtocol.ProxyProtocol.GetSourceAddressIpv4(ExampleHeader).ToString());
        }

        [TestMethod]
        public void CanGetDestinationIP()
        {
            Assert.AreEqual("172.31.12.55", ProxyProtocol.ProxyProtocol.GetDestinationAddressIpv4(ExampleHeader).ToString());
        }

        [TestMethod]
        public void CanGetSourcePort()
        {
            Assert.AreEqual(45334, ProxyProtocol.ProxyProtocol.GetSourcePortIpv4(ExampleHeader));
        }

        [TestMethod]
        public void CanGetDestinationPort()
        {
            Assert.AreEqual(587, ProxyProtocol.ProxyProtocol.GetDestinationPortIpv4(ExampleHeader));
        }

        [TestMethod]
        public void CanGetSourceIPv6()
        {
            var ip = IPAddress.Parse("2001:0db8:85a3:0000:0000:8a2e:0370:7334");
            var headeripv6 = GetProxyHeaderClone().Take(16).Concat(ip.GetAddressBytes()).ToArray();
            Assert.IsTrue(ip.Equals(ProxyProtocol.ProxyProtocol.GetSourceAddressIpv6(headeripv6)));
        }

        [TestMethod]
        public void CanGetDestinationIPv6()
        {
            var source = IPAddress.Parse("2001:0db8:85a3:0000:0000:8a2e:0370:7334");
            var destination = IPAddress.Parse("2001:0db8:85a3:0000:0000:8a2e:0370:7335");
            var headeripv6 = GetProxyHeaderClone().Take(16).Concat(source.GetAddressBytes()).Concat(destination.GetAddressBytes()).ToArray();
            Assert.IsTrue(destination.Equals(ProxyProtocol.ProxyProtocol.GetDestinationAddressIpv6(headeripv6)));
        }

        [TestMethod]
        public async Task CanGetSourcePortIPv6()
        {
            var source = IPAddress.Parse("2001:0db8:85a3:0000:0000:8a2e:0370:7334");
            var destination = IPAddress.Parse("2001:0db8:85a3:0000:0000:8a2e:0370:7335");
            var headeripv6 = GetProxyHeaderClone()
                .Take(16)
                .Concat(source.GetAddressBytes())
                .Concat(destination.GetAddressBytes())
                .Concat(IntTo2Bytes(1000))
                .Concat(IntTo2Bytes(1001))
                .ToArray();
            headeripv6[13] = 0x21;
            var stream = new MemoryStream(headeripv6);
            var proxy = new ProxyProtocol.ProxyProtocol(stream, new IPEndPoint(IPAddress.Parse("10.0.0.1"), 123));

            var actual = await proxy.GetRemoteEndpoint();

            Assert.AreEqual(1000, actual.Port);
        }

        [TestMethod]
        public void CanGetDestinationPortIPv6()
        {
            var source = IPAddress.Parse("2001:0db8:85a3:0000:0000:8a2e:0370:7334");
            var destination = IPAddress.Parse("2001:0db8:85a3:0000:0000:8a2e:0370:7335");
            var headeripv6 = GetProxyHeaderClone()
                .Take(16)
                .Concat(source.GetAddressBytes())
                .Concat(destination.GetAddressBytes())
                .Concat(IntTo2Bytes(1000))
                .Concat(IntTo2Bytes(1001))
                .ToArray();
            Assert.AreEqual(1001, ProxyProtocol.ProxyProtocol.GetDestinationPortIpv6(headeripv6));
        }

        [TestMethod]
        public void ShouldGetSourceIpAndPort()
        {
            var stream = new MemoryStream(GetProxyHeaderClone());
            var proxy = new ProxyProtocol.ProxyProtocol(stream, null);
            var endpoint = proxy.GetRemoteEndpoint().Result;

            Assert.AreEqual("123.103.201.154", endpoint.Address.ToString());
            Assert.AreEqual(45334, endpoint.Port);
        }
        
        [TestMethod]
        public void ShouldGetSourceIpAndPortForNonProxyProtocolConnections()
        {
            var header = GetProxyHeaderClone();
            header[0] = 0x00;
            var stream = new MemoryStream(header);
            var proxy = new ProxyProtocol.ProxyProtocol(stream, new IPEndPoint(IPAddress.Parse("10.0.0.1"), 123));
            
            var endpoint = proxy.GetRemoteEndpoint().Result;

            Assert.AreEqual("10.0.0.1", endpoint.Address.ToString());
            Assert.AreEqual(123, endpoint.Port);
        }

        [TestMethod]
        public void ShouldGetCorrectHeaderLength()
        {
            var stream = new MemoryStream(GetProxyHeaderClone());
            var proxy = new ProxyProtocol.ProxyProtocol(stream, null);
            var length = proxy.GetProxyHeaderLength().Result;

            Assert.AreEqual(100, length);
        }

        private static byte[] IntTo2Bytes(int i)
        {
            return BitConverter.GetBytes(i).Take(2).Reverse().ToArray();
        }
    }
}
