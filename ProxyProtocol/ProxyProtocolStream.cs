using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace ProxyProtocol
{
    /// <summary>
    /// A Stream which implements Proxy Protocol to read the real client IP
    /// </summary>
    public class ProxyProtocolStream : Stream
    {
        private Stream innerStream;
        private bool newStream = true;
        private Action<ProxyProtocol> callback;
        private IPEndPoint remoteEndPoint;
        private byte[] readBuffer;
        private int currentPosition = 0;
        private int endPosition = 0;

        public ProxyProtocolStream(Stream innerStream, IPEndPoint remoteEndPoint, Action<ProxyProtocol> callback)
        {
            this.innerStream = innerStream;
            this.remoteEndPoint = remoteEndPoint;
            this.callback = callback;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var maxBytesToRead = count - offset;
            var data = new byte[maxBytesToRead];
            var bytesRead = this.ReadAsync(data, CancellationToken.None).Result;
            Array.Copy(data, 0, buffer, offset, bytesRead);
            
            return bytesRead;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (this.newStream)
            {
                this.newStream = false;
                var proxy = new ProxyProtocol(this.innerStream, this.remoteEndPoint, buffer.Length);
                this.readBuffer = await proxy.GetBytesWithoutProxyHeader();
                this.endPosition = await proxy.GetLengthWithoutProxyHeader();
                this.callback(proxy);
            }

            return await this.ReadAsyncSavedBytes(buffer, cancellationToken);
        }

        private async Task<int> ReadAsyncSavedBytes(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var savedBytesCopied = this.CopySavedData(buffer);

            return savedBytesCopied == 0
                ? await this.innerStream.ReadAsync(buffer, cancellationToken)
                : savedBytesCopied;
        }

        private int CopySavedData(Memory<byte> buffer)
        {
            var availableBytes = this.endPosition - this.currentPosition;
            if (availableBytes == 0)
            {
                return 0;
            }
            
            var data = new byte[buffer.Length];
            var bytesToCopy = availableBytes >= data.Length ? data.Length : availableBytes;
            Array.Copy(this.readBuffer, this.currentPosition, data, 0, bytesToCopy);
            this.currentPosition += bytesToCopy;
            data.CopyTo(buffer);
            
            return bytesToCopy;
        }

        public override void Flush() => this.innerStream.Flush();

        public override long Seek(long offset, SeekOrigin origin) => this.innerStream.Seek(offset, origin);

        public override void SetLength(long value) => this.innerStream.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) =>
            this.innerStream.Write(buffer, offset, count);

        public override bool CanRead => this.innerStream.CanRead;
        public override bool CanSeek => this.innerStream.CanSeek;
        public override bool CanWrite => this.innerStream.CanWrite;
        public override long Length => this.innerStream.Length;

        public override long Position
        {
            get => this.innerStream.Position;
            set => this.innerStream.Position = value;
        }
    }
}