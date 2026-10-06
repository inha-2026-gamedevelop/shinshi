// System
using System;
using System.Collections.Generic;

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
        public class Entry
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private string _id;     // 카탈로그 정의 ID, 등급이 붙은 ID는 등급을 뗀 ID로 대신 찾는다
            [SerializeField] private Sprite _sprite;

            public string Id     => _id;
            public Sprite Sprite => _sprite;
        }

        /****************************************
        *                Fields
        ****************************************/

        private const char ID_SEPARATOR = '_';

        [Header("아이콘 (정의 ID 기준)")]
        [SerializeField] private Entry[] _entries = null;

        [Header("등급 틀 (일반, 희귀, 영웅, 전설 순서)")]
        [SerializeField] private Sprite[] _gradeFrames = null;

        [Header("칸 바탕 (일반, 희귀, 영웅, 전설 순서)")]
        [SerializeField] private Sprite[] _gradeBackgrounds = null;
        [SerializeField] private Sprite   _emptyBackground  = null;

        [Header("등급 색 (일반, 희귀, 영웅, 전설 순서)")]
        [SerializeField] private Color[] _gradeColors = null;

        [Header("부적 단계 배지")]
        [SerializeField] private Sprite _tierSupport = null;
        [SerializeField] private Sprite _tierCore    = null;

        // 찾은 결과를 원래 ID로 기억해 두므로 등급을 떼어 찾는 경우도 두 번째부터는 문자열을 만들지 않는다
        private Dictionary<string, Sprite> _lookup = null;

        /****************************************
        *              Properties
        ****************************************/

        public Sprite TierSupport     => _tierSupport;
        public Sprite TierCore        => _tierCore;
        public Sprite EmptyBackground => _emptyBackground;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable() { _lookup = null; }

        private void OnValidate() { _lookup = null; }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>정확히 맞는 ID가 없으면 마지막 마디를 떼며 찾는다 (equip_weapon_rare -> equip_weapon). 없으면 null</summary>
        public Sprite FindIcon(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            EnsureLookup();

            if (_lookup.TryGetValue(id, out Sprite cached))
            {
                return cached;
            }

            Sprite found = null;
            string key = id;
            int cut = key.LastIndexOf(ID_SEPARATOR);

            while (cut > 0)
            {
                key = key.Substring(0, cut);

                if (_lookup.TryGetValue(key, out found))
                {
                    break;
                }

                cut = key.LastIndexOf(ID_SEPARATOR);
            }

            _lookup[id] = found;
            return found;
        }

        public Sprite FindGradeFrame(EItemGrade grade) { return PickByGrade(_gradeFrames, grade); }

        /// <summary>등급 색 그라데이션 칸 바탕, 없으면 null</summary>
        public Sprite FindGradeBackground(EItemGrade grade) { return PickByGrade(_gradeBackgrounds, grade); }

        /// <summary>칸 아래 등급 줄에 쓰는 색, 없으면 흰색</summary>
        public Color FindGradeColor(EItemGrade grade)
        {
            int index = (int)grade;

            if ((_gradeColors == null) || (index < 0) || (index >= _gradeColors.Length))
            {
                return Color.white;
            }

            return _gradeColors[index];
        }

        private static Sprite PickByGrade(Sprite[] sprites, EItemGrade grade)
        {
            int index = (int)grade;

            if ((sprites == null) || (index < 0) || (index >= sprites.Length))
            {
                return null;
            }

            return sprites[index];
        }

        private void EnsureLookup()
        {
            if (_lookup != null)
            {
                return;
            }

            _lookup = new Dictionary<string, Sprite>();

            if (_entries == null)
            {
                return;
            }

            for (int i = 0; i < _entries.Length; ++i)
            {
                Entry entry = _entries[i];

                if ((entry == null) || string.IsNullOrEmpty(entry.Id))
                {
                    continue;
                }

                _lookup[entry.Id] = entry.Sprite;
            }
        }
    }
}
