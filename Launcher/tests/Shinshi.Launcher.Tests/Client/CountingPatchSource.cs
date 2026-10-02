// System
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

// Project
using Shinshi.Patch.Client;

namespace Shinshi.Launcher.Tests.Client
{
    // 실제 원본을 감싸 키별 요청 횟수와 읽은 바이트를 세고, 응답을 바꿔치기해 손상과 취소를 흉내 낸다
    internal sealed class CountingPatchSource : IPatchSource
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string BLOB_MARKER = "/blobs/";

        private readonly IPatchSource             _inner;
        private readonly Dictionary<string, int>  _opens     = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _bytesRead = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly object                   _gate      = new object();

        /****************************************
        *              Properties
        ****************************************/

        // 키, 그 키의 몇 번째 요청인지(1부터), 원본 바이트를 받아 내보낼 바이트를 돌려준다
        public Func<string, int, byte[], byte[]>? Tamper { get; set; }
        public Action<string>?                    OnOpen { get; set; }

        /****************************************
        *                Methods
        ****************************************/

        public CountingPatchSource(IPatchSource inner)
        {
            _inner = inner;
        }

        public async Task<Stream> OpenAsync(string key, CancellationToken cancellationToken)
        {
            int count;
            lock (_gate)
            {
                _opens.TryGetValue(key, out count);
                ++count;
                _opens[key] = count;
            }

            OnOpen?.Invoke(key);
            Stream inner = await _inner.OpenAsync(key, cancellationToken).ConfigureAwait(false);
            if (Tamper == null)
            {
                return new CountingReadStream(inner, read => AddBytesRead(key, read));
            }

            byte[] original;
            await using (inner)
            {
                using MemoryStream buffer = new MemoryStream();
                await inner.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                original = buffer.ToArray();
            }

            byte[] served = Tamper(key, count, original);
            return new CountingReadStream(new MemoryStream(served), read => AddBytesRead(key, read));
        }

        public int GetOpenCount(string key)
        {
            lock (_gate)
            {
                return _opens.TryGetValue(key, out int count) ? count : 0;
            }
        }

        public long GetBytesRead(string key)
        {
            lock (_gate)
            {
                return _bytesRead.TryGetValue(key, out long bytes) ? bytes : 0;
            }
        }

        public int GetBlobOpenCount()
        {
            int total = 0;
            lock (_gate)
            {
                foreach (KeyValuePair<string, int> pair in _opens)
                {
                    if (pair.Key.Contains(BLOB_MARKER, StringComparison.Ordinal))
                    {
                        total += pair.Value;
                    }
                }
            }

            return total;
        }

        public int GetTotalOpenCount()
        {
            int total = 0;
            lock (_gate)
            {
                foreach (int count in _opens.Values)
                {
                    total += count;
                }
            }

            return total;
        }

        public void ResetCounts()
        {
            lock (_gate)
            {
                _opens.Clear();
                _bytesRead.Clear();
            }
        }

        private void AddBytesRead(string key, int read)
        {
            lock (_gate)
            {
                _bytesRead.TryGetValue(key, out long bytes);
                _bytesRead[key] = bytes + read;
            }
        }
    }
}
