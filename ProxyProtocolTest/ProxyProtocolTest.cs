using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using HAProxy;

namespace ProxyProtocolTest
{
    [TestClass]
    public class ProxyProtocolTest
    {
        private byte[] exampleHeader =
        {
            0x0d, 0x0a, 0x0d, 0x0a, 0x00, 0x0d, 0x0a, 0x51, 0x55, 0x49, 0x54, 0x0a, 0x21, 0x11, 0x00, 0x54, 0x7b, 0x67,
            0xc9, 0x9a, 0xac, 0x1f, 0x0c, 0x37, 0xb1, 0x16, 0x02, 0x4b, 0x03, 0x00, 0x04, 0x08, 0x94, 0x22, 0x07, 0x04,
            0x00, 0x3e, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };

        [TestMethod]
        public void ShouldRecogniseSignature()
        {
            Assert.IsTrue(ProxyProtocol.HasProxyProtocolSignature(this.exampleHeader));
        }

        [TestMethod]
        public void ShouldRecogniseInvalidSignature()
        {
            this.exampleHeader[2] = 0x0b;
            Assert.IsFalse(ProxyProtocol.HasProxyProtocolSignature(this.exampleHeader));
        }

        [TestMethod]
        public void ShouldIdentifyV2()
        {
            Assert.IsTrue(ProxyProtocol.IsProtocolV2(this.exampleHeader));
        }

        [TestMethod]
        public void ShouldIdentifyV1()
        {
            this.exampleHeader[12] = 0x10;
            Assert.IsFalse(ProxyProtocol.IsProtocolV2(this.exampleHeader));
        }

        [TestMethod]
        public void ShouldDetectProxyVLocal()
        {
            Assert.AreEqual("PROXY", ProxyProtocol.GetCommand(this.exampleHeader));
        }

        [TestMethod]
        public void ShouldDetectAddressFamily()
        {
            Assert.AreEqual(AddressFamily.InterNetwork, ProxyProtocol.GetAddressFamily(this.exampleHeader));
        }

        [TestMethod]
        public void ShouldReadLength()
        {
            Assert.AreEqual(84, ProxyProtocol.GetLength(this.exampleHeader));
        }

        [TestMethod]
        public void ShouldReadStreamToEndOfHeader()
        {
            var stream = new MemoryStream(this.exampleHeader);
            var proxy = new ProxyProtocol(stream, null);
            proxy.GetProxyProtocolHeader().Wait();

            Assert.AreEqual(100, stream.Position);
        }

        [TestMethod]
        public void CanGetSourceIP()
        {
            Assert.AreEqual("123.103.201.154", ProxyProtocol.GetSourceAddressIpv4(this.exampleHeader).ToString());
        }

        [TestMethod]
        public void CanGetDestinationIP()
        {
            Assert.AreEqual("172.31.12.55", ProxyProtocol.GetDestinationAddressIpv4(this.exampleHeader).ToString());
        }

        [TestMethod]
        public void CanGetSourcePort()
        {
            Assert.AreEqual(45334, ProxyProtocol.GetSourcePortIpv4(this.exampleHeader));
        }

        [TestMethod]
        public void CanGetDestinationPort()
        {
            Assert.AreEqual(587, ProxyProtocol.GetDestinationPortIpv4(this.exampleHeader));
        }

        [TestMethod]
        public void CanGetSourceIPv6()
        {
            var ip = IPAddress.Parse("2001:0db8:85a3:0000:0000:8a2e:0370:7334");
            var headeripv6 = this.exampleHeader.Take(16).Concat(ip.GetAddressBytes()).ToArray();
            Assert.IsTrue(ip.Equals(ProxyProtocol.GetSourceAddressIpv6(headeripv6)));
        }

        [TestMethod]
        public void CanGetDestinationIPv6()
        {
            var source = IPAddress.Parse("2001:0db8:85a3:0000:0000:8a2e:0370:7334");
            var destination = IPAddress.Parse("2001:0db8:85a3:0000:0000:8a2e:0370:7335");
            var headeripv6 = this.exampleHeader.Take(16).Concat(source.GetAddressBytes()).Concat(destination.GetAddressBytes()).ToArray();
            Assert.IsTrue(destination.Equals(ProxyProtocol.GetDestinationAddressIpv6(headeripv6)));
        }

        [TestMethod]
        public void CanGetSourcePortIPv6()
        {
            var source = IPAddress.Parse("2001:0db8:85a3:0000:0000:8a2e:0370:7334");
            var destination = IPAddress.Parse("2001:0db8:85a3:0000:0000:8a2e:0370:7335");
            var headeripv6 = this.exampleHeader
                .Take(16)
                .Concat(source.GetAddressBytes())
                .Concat(destination.GetAddressBytes())
                .Concat(IntTo2Bytes(1000))
                .Concat(IntTo2Bytes(1001))
                .ToArray();
            Assert.AreEqual(1000, ProxyProtocol.GetSourcePortIpv6(headeripv6));
        }

        [TestMethod]
        public void CanGetDestinationPortIPv6()
        {
            var source = IPAddress.Parse("2001:0db8:85a3:0000:0000:8a2e:0370:7334");
            var destination = IPAddress.Parse("2001:0db8:85a3:0000:0000:8a2e:0370:7335");
            var headeripv6 = this.exampleHeader
                .Take(16)
                .Concat(source.GetAddressBytes())
                .Concat(destination.GetAddressBytes())
                .Concat(IntTo2Bytes(1000))
                .Concat(IntTo2Bytes(1001))
                .ToArray();
            Assert.AreEqual(1001, ProxyProtocol.GetDestinationPortIpv6(headeripv6));
        }

        private static byte[] IntTo2Bytes(int i)
        {
            return BitConverter.GetBytes(i).Take(2).Reverse().ToArray();
        }

        [TestMethod]
        public void ShouldGetSourceIpAndPort()
        {
            var stream = new MemoryStream(this.exampleHeader);
            var proxy = new ProxyProtocol(stream, null);
            var endpoint = proxy.GetRemoteEndpoint().Result;

            Assert.AreEqual("123.103.201.154", endpoint.Address.ToString());
            Assert.AreEqual(45334, endpoint.Port);
        }
    }
}
