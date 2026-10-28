// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.UI
{
    /// <summary> UI 효과음 "그룹/동작"과 클립을 잇는 표시 전용 데이터, 서버 판정과 GameDB에 들어가지 않는다 </summary>
    [CreateAssetMenu(fileName = RESOURCE_NAME, menuName = "Shinshi/UI/UiSoundCatalog")]
    public sealed class UiSoundCatalogSO : ScriptableObject
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string RESOURCE_NAME = "UiSoundCatalog";

        private const int ACTION_BITS = 8; // 동작 값이 255를 넘으면 키가 겹친다

        [SerializeField] private UiSoundEntry[] _entries = Array.Empty<UiSoundEntry>();

        private Dictionary<int, UiSoundEntry> _lookup = null;

        /****************************************
        *                Methods
        ****************************************/

        public static UiSoundCatalogSO Load()
        {
            return Resources.Load<UiSoundCatalogSO>(RESOURCE_NAME);
        }

        /// <summary> 그 칸이 비어 있으면 공통 그룹의 같은 동작을 찾는다. 둘 다 없으면 false </summary>
        public bool TryGet(EUiSoundGroup group, EUiSoundAction action, out UiSoundEntry entry)
        {
            if (_lookup == null)
            {
                BuildLookup();
            }

            if (_lookup.TryGetValue(ToKey(group, action), out entry))
            {
                return true;
            }

            return (group != EUiSoundGroup.Common) && ((group != EUiSoundGroup.Toast) || (action != EUiSoundAction.Open))
                && (_lookup.TryGetValue(ToKey(EUiSoundGroup.Common, action), out entry));
        }

        // 에디터에서 항목을 고치면 도메인 재로드 없이도 다시 만들어지도록 검증 때 비운다
        private void OnValidate()
        {
            _lookup = null;
        }

        private void BuildLookup()
        {
            _lookup = new Dictionary<int, UiSoundEntry>(_entries.Length);
            foreach (UiSoundEntry entry in _entries)
            {
                if ((entry == null) || (entry.Clip == null))
                {
                    continue;
                }

                int key = ToKey(entry.Group, entry.Action);
                if (_lookup.ContainsKey(key))
                {
                    Debug.LogWarning($"[UiSound] {entry.Group}/{entry.Action} 칸이 두 번 있어 뒤의 항목을 씁니다");
                }

                _lookup[key] = entry;
            }
        }

        private static int ToKey(EUiSoundGroup group, EUiSoundAction action)
        {
            return ((int)group << ACTION_BITS) | (int)action;
        }
    }
}
