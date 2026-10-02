// System
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Shinshi.Launcher.Tests.Client
{
    // 읽기 전용 래퍼, 소비자가 실제로 읽어 간 바이트 수를 알린다
    internal sealed class CountingReadStream : Stream
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Stream      _inner;
        private readonly Action<int> _onRead;

        /****************************************
        *              Properties
        ****************************************/

        public override bool CanRead  => true;
        public override bool CanSeek  => false;
        public override bool CanWrite => false;
        public override long Length   => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /****************************************
        *                Methods
        ****************************************/

        public CountingReadStream(Stream inner, Action<int> onRead)
        {
            _inner  = inner;
            _onRead = onRead;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = _inner.Read(buffer, offset, count);
            _onRead(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            _onRead(read);
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }

        public override void SetLength(long value) { throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync().ConfigureAwait(false);
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}
