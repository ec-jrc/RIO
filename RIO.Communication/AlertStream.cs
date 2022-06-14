using System;
using System.IO;

namespace RIO
{
    /// <summary>
    /// This class catches the exceptions raised by the underlying stream and raises events instead:
    /// WriteError, ReadError and Error.
    /// </summary>
    /// <inheritdoc/>
    internal class AlertStream : Stream
    {
        private readonly Stream stream;

        public AlertStream(Stream stream)
        {
            this.stream = stream;
        }

        public override bool CanRead => stream?.CanRead == true;

        public override bool CanSeek => stream?.CanSeek == true;

        public override bool CanWrite => stream?.CanWrite == true;

        public override long Length => stream?.Length ?? 0;

        public override long Position { get => stream?.Position ?? 0; set { if (stream != null) stream.Position = value; } }

        public override void Flush()
        {
            try
            {
                stream?.Flush();
            }
            catch (System.Exception ex)
            {
                WriteError?.Invoke(this, "Flush");
                Error?.Invoke(this, "Flush");
                throw ex;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            try
            {
                return stream?.Read(buffer, offset, count) ?? 0;
            }
            catch (System.Exception ex)
            {
                ReadError?.Invoke(this, "Read");
                Error?.Invoke(this, "Read");
                throw ex;
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            try
            {
                return stream?.Seek(offset, origin) ?? 0;
            }
            catch (System.Exception ex)
            {
                ReadError?.Invoke(this, "Seek");
                Error?.Invoke(this, "Seek");
                throw ex;
            }
        }

        public override void SetLength(long value)
        {
            try
            {
                stream?.SetLength(value);
            }
            catch (System.Exception ex)
            {
                WriteError?.Invoke(this, "SetLength");
                Error?.Invoke(this, "SetLength");
                throw ex;
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            try
            {
                stream?.Write(buffer, offset, count);
            }
            catch (System.Exception ex)
            {
                WriteError?.Invoke(this, "Write");
                Error?.Invoke(this, "Write");
                throw ex;
            }
        }

        public event EventHandler<string> WriteError;
        public event EventHandler<string> ReadError;
        public event EventHandler<string> Error;
    }
}