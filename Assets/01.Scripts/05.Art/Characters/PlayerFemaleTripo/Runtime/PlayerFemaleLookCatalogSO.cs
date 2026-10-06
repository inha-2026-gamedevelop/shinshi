// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> 커스터마이즈 조립에 쓰는 얼굴 1종, 헤어 3종, 의상 3종의 프리팹과 라벨. 부모가 실제 에셋으로 채운다 </summary>
    [CreateAssetMenu(fileName = "PlayerFemaleLookCatalog", menuName = "Shinshi/PlayerFemaleLookCatalogSO")]
    public sealed class PlayerFemaleLookCatalogSO : ScriptableObject
    {
        /****************************************
        *                Fields
        ****************************************/

        [Serializable]
        public sealed class LookEntry
        {
            [SerializeField] private int        _id;
            [SerializeField] private string      _label       = string.Empty;
            [SerializeField] private GameObject  _classPrefab = null;

            public int        Id          => _id;
            public string     Label       => _label;
            public GameObject ClassPrefab => _classPrefab;
        }

        [SerializeField] private LookEntry[] _faces   = Array.Empty<LookEntry>();
        [SerializeField] private LookEntry[] _hairs   = Array.Empty<LookEntry>();
        [SerializeField] private LookEntry[] _outfits = Array.Empty<LookEntry>();

        // 9조합 전부가 쓰는 공용 로코모션 컨트롤러
        [SerializeField] private RuntimeAnimatorController _locomotionController = null;

        /****************************************
        *              Properties
        ****************************************/

        public RuntimeAnimatorController LocomotionController => _locomotionController;

        /****************************************
        *                Methods
        ****************************************/

        public bool TryGetFace(int id, out LookEntry entry)   { return TryFind(_faces, id, out entry); }
        public bool TryGetHair(int id, out LookEntry entry)   { return TryFind(_hairs, id, out entry); }
        public bool TryGetOutfit(int id, out LookEntry entry) { return TryFind(_outfits, id, out entry); }

        private static bool TryFind(LookEntry[] entries, int id, out LookEntry entry)
        {
            entry = null;
            if (entries == null)
            {
                return false;
            }

            for (int i = 0; i < entries.Length; ++i)
            {
                if ((entries[i] != null) && (entries[i].Id == id))
                {
                    entry = entries[i];
                    return true;
                }
            }

            return false;
        }
    }
}
