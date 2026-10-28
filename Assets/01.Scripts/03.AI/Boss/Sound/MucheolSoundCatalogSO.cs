// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    /// <summary> 무철 효과음 ID와 클립을 잇는 표시 전용 데이터, 서버 판정과 GameDB에 들어가지 않는다 </summary>
    [CreateAssetMenu(fileName = "MucheolSoundCatalog", menuName = "Shinshi/AI/MucheolSoundCatalog")]
    public sealed class MucheolSoundCatalogSO : ScriptableObject
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private MucheolSoundEntry[] _entries = Array.Empty<MucheolSoundEntry>();

        private Dictionary<EMucheolSound, MucheolSoundEntry> _lookup = null;

        /****************************************
        *                Methods
        ****************************************/

        public bool TryGet(EMucheolSound id, out MucheolSoundEntry entry)
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
            _lookup = new Dictionary<EMucheolSound, MucheolSoundEntry>(_entries.Length);
            foreach (MucheolSoundEntry entry in _entries)
            {
                if ((entry != null) && (entry.Id != EMucheolSound.None) && (entry.Clip != null))
                {
                    _lookup[entry.Id] = entry;
                }
            }
        }
    }
}
