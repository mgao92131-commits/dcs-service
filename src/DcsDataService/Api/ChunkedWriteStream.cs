using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace DcsDataService.Api
{
    public sealed class ChunkedWriteStream : Stream
    {
        private readonly Stream _inner;
        private bool _completed;
        private bool _disposed;

        public ChunkedWriteStream(Stream inner)
        {
            if (inner == null) throw new ArgumentNullException("inner");
            if (!inner.CanWrite) throw new ArgumentException("The inner stream must be writable.", "inner");
            _inner = inner;
        }

        public void Complete()
        {
            EnsureWritable();
            if (_completed) throw new InvalidOperationException("The chunked stream is already complete.");
            byte[] terminator = Encoding.ASCII.GetBytes("0\r\n\r\n");
            _inner.Write(terminator, 0, terminator.Length);
            _inner.Flush();
            _completed = true;
        }

        public override bool CanRead { get { return false; } }
        public override bool CanSeek { get { return false; } }
        public override bool CanWrite { get { return !_disposed && !_completed; } }
        public override long Length { get { throw new NotSupportedException(); } }
        public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
        public override void Flush() { EnsureWritable(); _inner.Flush(); }

        public override int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
        public override void SetLength(long value) { throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureWritable();
            if (buffer == null) throw new ArgumentNullException("buffer");
            if (offset < 0 || count < 0 || offset > buffer.Length - count) throw new ArgumentOutOfRangeException();
            if (count == 0) return;
            byte[] prefix = Encoding.ASCII.GetBytes(count.ToString("X", CultureInfo.InvariantCulture) + "\r\n");
            _inner.Write(prefix, 0, prefix.Length);
            _inner.Write(buffer, offset, count);
            byte[] suffix = Encoding.ASCII.GetBytes("\r\n");
            _inner.Write(suffix, 0, suffix.Length);
        }

        protected override void Dispose(bool disposing)
        {
            _disposed = true;
            base.Dispose(disposing);
        }

        private void EnsureWritable()
        {
            if (_disposed) throw new ObjectDisposedException("ChunkedWriteStream");
            if (_completed) throw new InvalidOperationException("The chunked stream is already complete.");
        }
    }
}
