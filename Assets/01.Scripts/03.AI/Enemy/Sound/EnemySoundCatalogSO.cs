// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    /// <summary> 일반 적 효과음 ID와 클립을 잇는 표시 전용 데이터, 서버 판정과 GameDB에 들어가지 않는다 </summary>
    [CreateAssetMenu(fileName = "EnemySoundCatalog", menuName = "Shinshi/AI/EnemySoundCatalog")]
    public sealed class EnemySoundCatalogSO : ScriptableObject
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private EnemySoundEntry[] _entries = Array.Empty<EnemySoundEntry>();

        private Dictionary<EEnemySound, EnemySoundEntry> _lookup = null;

        /****************************************
        *                Methods
        ****************************************/

        public bool TryGet(EEnemySound id, out EnemySoundEntry entry)
        {
            if (_lookup == null)
            {
                BuildLookup();
            }

            return _lookup.TryGetValue(id, out entry);
        }

        // 에디터에서 항목을 고치면 도메인 재로드 없이도 다시 만들어지도록 검증 때 비운다
        private void OnValidate()
        {
            _lookup = null;
        }

        private void BuildLookup()
        {
            _lookup = new Dictionary<EEnemySound, EnemySoundEntry>(_entries.Length);
            foreach (EnemySoundEntry entry in _entries)
            {
                if ((entry != null) && (entry.Id != EEnemySound.None) && (entry.PickClip() != null))
                {
                    _lookup[entry.Id] = entry;
                }
            }
        }
    }
}
