// System
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Shinshi.Patch
{
    /// <summary> 런처가 믿는 공개키 목록, 키 교체 기간에는 새 키와 옛 키를 함께 둔다 </summary>
    public sealed class TrustedKeySet
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Dictionary<string, PatchPublicKey> _keys = new Dictionary<string, PatchPublicKey>(StringComparer.Ordinal);

        /****************************************
        *              Properties
        ****************************************/

        public int Count => _keys.Count;

        /****************************************
        *                Methods
        ****************************************/

        public TrustedKeySet(IEnumerable<PatchPublicKey> keys)
        {
            ArgumentNullException.ThrowIfNull(keys);
            foreach (PatchPublicKey key in keys)
            {
                ArgumentNullException.ThrowIfNull(key, nameof(keys));
                if (!_keys.TryAdd(key.KeyId, key))
                {
                    throw new ArgumentException("Trusted key set contains a duplicate key id.", nameof(keys));
                }
            }
        }

        public TrustedKeySet(params PatchPublicKey[] keys)
            : this((IEnumerable<PatchPublicKey>)keys)
        {
        }

        public bool TryGet(string? keyId, [NotNullWhen(true)] out PatchPublicKey? key)
        {
            key = null;
            if (keyId == null)
            {
                return false;
            }

            return _keys.TryGetValue(keyId, out key);
        }
    }
}
