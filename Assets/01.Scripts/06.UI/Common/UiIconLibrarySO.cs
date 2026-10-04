// System
using System;

// Unity
using UnityEngine;

// 프로젝트
using Shinshi.Common;

namespace Shinshi.UI
{
    /// <summary>정의 ID로 UI 아이콘과 등급 틀을 찾는 표이며 게임 밸런스 데이터가 아니라 화면 자원 목록이다</summary>
    [CreateAssetMenu(fileName = "UiIconLibrary", menuName = "Shinshi/UI/Icon Library")]
    public sealed class UiIconLibrarySO : ScriptableObject
    {
        /****************************************
        *                Types
        ****************************************/

        [Serializable]
        public struct Entry
        {
            public string Id;
            public Sprite Sprite;
        }

        /****************************************
        *                Fields
        ****************************************/

        private const char ID_SEPARATOR = '_';

        [Header("아이콘 (정의 ID 기준)")]
        [SerializeField] private Entry[] _entries = null;

        [Header("등급 틀 (일반, 희귀, 영웅, 전설 순서)")]
        [SerializeField] private Sprite[] _gradeFrames = null;

        [Header("부적 단계 배지")]
        [SerializeField] private Sprite _tierSupport = null;
        [SerializeField] private Sprite _tierCore    = null;

        /****************************************
        *              Properties
        ****************************************/

        public Sprite TierSupport => _tierSupport;
        public Sprite TierCore    => _tierCore;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>정확히 맞는 ID가 없으면 마지막 마디를 떼며 찾는다 (equip_weapon_rare -> equip_weapon)</summary>
        public Sprite FindIcon(string id)
        {
            if ((_entries == null) || string.IsNullOrEmpty(id))
            {
                return null;
            }

            string key = id;

            while (true)
            {
                for (int i = 0; i < _entries.Length; ++i)
                {
                    if (_entries[i].Id == key)
                    {
                        return _entries[i].Sprite;
                    }
                }

                int cut = key.LastIndexOf(ID_SEPARATOR);

                if (cut <= 0)
                {
                    return null;
                }

                key = key.Substring(0, cut);
            }
        }

        public Sprite FindGradeFrame(EItemGrade grade)
        {
            int index = (int)grade;

            if ((_gradeFrames == null) || (index < 0) || (index >= _gradeFrames.Length))
            {
                return null;
            }

            return _gradeFrames[index];
        }
    }
}
