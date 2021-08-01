using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProxyProtocol;

namespace ProxyProtocolTest
{
    [TestClass]
    public class ProxyProtocolStreamTest
    {
        private CopyingMemoryStream memoryStream = new();
        
        [TestMethod]
        public async Task CanOpenProxyStream()
        {
            var stream = new MemoryStream(ProxyProtocolTest.GetProxyHeaderClone());
            IPEndPoint ipEndPoint = null;
            var proxy = new ProxyProtocolStream(stream, IPEndPoint.Parse("192.0.2.1"), async proxy => ipEndPoint = await proxy.GetRemoteEndpoint());
            
            await proxy.ReadAsync(new byte[200], CancellationToken.None);
            
            Assert.AreEqual("123.103.201.154", ipEndPoint.Address.ToString());
        }

        [TestMethod]
        public async Task CanOpenNonProxyStream()
        {
            var header = ProxyProtocolTest.GetProxyHeaderClone();
            header[0] = 0xFF;
            var stream = new MemoryStream(header);
            IPEndPoint ipEndPoint = null;
            var proxy = new ProxyProtocolStream(stream, IPEndPoint.Parse("192.0.2.1"), async proxy => ipEndPoint = await proxy.GetRemoteEndpoint());

            var result = new byte[200];
            var bytesRead = await proxy.ReadAsync(result, CancellationToken.None);
            
            Assert.AreEqual("192.0.2.1", ipEndPoint.Address.ToString());
            Assert.AreEqual(header.Length, bytesRead);
            for (var i = 0; i < header.Length; i++)
            {
                Assert.AreEqual(header[i], result[i]);
            }
        }

        [TestMethod]
        public async Task BytesReadIsCorrect()
        {
            var stream = new MemoryStream(ProxyProtocolTest.GetProxyHeaderClone());
            IPEndPoint ipEndPoint = null;
            var proxy = new ProxyProtocolStream(stream, IPEndPoint.Parse("192.0.2.1"), async proxy => ipEndPoint = await proxy.GetRemoteEndpoint());

            var bytesRead = await proxy.ReadAsync(new byte[200], CancellationToken.None);

            Assert.AreEqual(1, bytesRead);
        }

        [TestMethod]
        public async Task BytesReadIsCorrectForNonProxyStream()
        {
            var header = ProxyProtocolTest.GetProxyHeaderClone();
            header[0] = 0xFF;
            var stream = new MemoryStream(header);
            IPEndPoint ipEndPoint = null;
            var proxy = new ProxyProtocolStream(stream, IPEndPoint.Parse("192.0.2.1"), async proxy => ipEndPoint = await proxy.GetRemoteEndpoint());

            var bytesRead = await proxy.ReadAsync(new byte[200], CancellationToken.None);

            Assert.AreEqual(101, bytesRead);
        }

        [TestMethod]
        public async Task BytesReturnedAreCorrect()
        {
            var header = ProxyProtocolTest.GetProxyHeaderClone();
            header[100] = 0xFF;
            var stream = new MemoryStream(header);
            IPEndPoint ipEndPoint = null;
            var proxy = new ProxyProtocolStream(stream, IPEndPoint.Parse("192.0.2.1"), async proxy => ipEndPoint = await proxy.GetRemoteEndpoint());

            var read = new byte[200];
            await proxy.ReadAsync(read, CancellationToken.None);

            Assert.AreEqual(0xFF, read[0]);
        }
        
        [TestMethod]
        public async Task CanReadBytesAfterSignature()
        {
            var header = ProxyProtocolTest.GetProxyHeaderClone();
            header[0] = 0xFF;
            var stream = new MemoryStream(header);
            IPEndPoint ipEndPoint = null;
            var proxy = new ProxyProtocolStream(stream, IPEndPoint.Parse("192.0.2.1"), async proxy => ipEndPoint = await proxy.GetRemoteEndpoint());

            var bytesRead = await proxy.ReadAsync(new byte[16], CancellationToken.None);

            Assert.AreEqual(16, bytesRead);
            
            bytesRead = await proxy.ReadAsync(new byte[10], CancellationToken.None);

            Assert.AreEqual(10, bytesRead);
        }

        [TestMethod]
        public void CanReadSynchronously()
        {
            var stream = new MemoryStream(ProxyProtocolTest.GetProxyHeaderClone());
            IPEndPoint ipEndPoint = null;
            var proxy = new ProxyProtocolStream(stream, IPEndPoint.Parse("192.0.2.1"), proxy => ipEndPoint = proxy.GetRemoteEndpoint().Result);

            var bytesRead = proxy.Read(new byte[200], 0, 200);

            Assert.AreEqual(1, bytesRead);
        }
        
        [TestMethod]
        public async Task ShouldWorkWithNonProxyConnections()
        {
            var sut = new ProxyProtocolStream(this.memoryStream, IPEndPoint.Parse("192.0.2.1"), _ => { });
            var reader = new StreamReader(sut);
            var writer = new StreamWriter(sut);

            await writer.WriteLineAsync("test");
            await writer.FlushAsync();
            
            Assert.AreEqual("test\r\n", await this.memoryStream.ReadLine());
            
            var result = reader.ReadLineAsync();
            this.memoryStream.SendLine("finished");

            Assert.AreEqual("finished", result.Result);
        }
    }
}
