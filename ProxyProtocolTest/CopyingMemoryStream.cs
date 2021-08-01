using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace ProxyProtocolTest
{
    /// <summary>
    /// A stream for unit testing which records what's written by to it and where we can send messages to be read from it.
    /// </summary>
    public class CopyingMemoryStream : MemoryStream
    {
        private BufferBlock<string> WrittenByServer = new();
        private BufferBlock<byte[]> ToSendToServer = new();
        private byte[] internalBuffer;
        private int bufferIndex = 0;
        
        public override void Write(byte[] buffer, int offset, int count)
        {
            this.WrittenByServer.Post(this.ConvertToString(buffer.Skip(offset).Take(count).ToArray()));
        }
        
        public override int Read(byte[] buffer, int offset, int count)
        {
            var maxBytesToRead = count - offset;
            var data = new byte[maxBytesToRead];
            var bytesRead = this.ReadAsync(data, CancellationToken.None).Result;
            Array.Copy(data, 0, buffer, offset, bytesRead);
            
            return bytesRead;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var maxBytesToRead = count - offset;
            var data = new byte[maxBytesToRead];
            var bytesRead = await this.ReadAsync(data, cancellationToken);
            Array.Copy(data, 0, buffer, offset, bytesRead);
            
            return bytesRead;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            this.internalBuffer ??= await this.ToSendToServer.ReceiveAsync();
            var (data, bytesRead) = this.ReadFromBuffer(buffer.Length);
            var rightSizedArray = new byte[bytesRead];
            Array.Copy(data, 0, rightSizedArray, 0, bytesRead);
            rightSizedArray.CopyTo(buffer);
            
            return bytesRead;
        }

        private (byte[] data, int bytesRead) ReadFromBuffer(int maxBytes)
        {
            var data = new byte[maxBytes];
            var bytesRead = 0;
            if (this.internalBuffer != null)
            {
                var availableBytes = this.internalBuffer.Length - this.bufferIndex;
                if (maxBytes >= availableBytes)
                {
                    Array.Copy(this.internalBuffer, this.bufferIndex, data, 0, availableBytes);
                    bytesRead = availableBytes;
                    this.internalBuffer = null;
                    this.bufferIndex = 0;
                }
                else
                {
                    Array.Copy(this.internalBuffer, this.bufferIndex, data, 0, maxBytes);
                    this.bufferIndex += maxBytes;
                    bytesRead = maxBytes;
                }
            }
            
            return (data, bytesRead);
        }

        public void SendLine(string line)
        {
            var bytes = Encoding.UTF8.GetBytes(line + "\r\n");
            this.SendBytes(bytes);
        }

        public void SendBytes(byte[] bytes) => this.ToSendToServer.Post(bytes);

        public string ConvertToString(byte[] buffer) => Encoding.UTF8.GetString(buffer);

        public async Task<string> ReadLine()
        {
            var line = await this.WrittenByServer.ReceiveAsync();
            while (!line.EndsWith("\n"))
            {
                var nextline = await this.WrittenByServer.ReceiveAsync();
                line += nextline;
            }

            return line;
        }
    }
}