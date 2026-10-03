// System
using System;

// Unity
using UnityEngine;

// 서드파티
using TMPro;

namespace Shinshi.Network
{
    /// <summary> 캐릭터 선택 화면의 칸 하나. 같은 칸을 무대 표시와 리스트 카드가 함께 보여 주므로 글자는 여러 곳에 쓴다 </summary>
    [Serializable]
    public sealed class CharacterSelectSlot
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string EMPTY_NAME    = "빈 칸";
        private const string UNKNOWN_POWER = "전투력 -";
        private const string UNKNOWN_LEVEL = "Lv.-";

        // 캐릭터가 있을 때만 켜는 것(무대 모델, 무대 표시 글자 묶음)
        [SerializeField] private GameObject[] _filledObjects = null;
        [SerializeField] private TMP_Text[]   _nameTexts     = null;
        [SerializeField] private TMP_Text[]   _classTexts    = null;
        [SerializeField] private TMP_Text[]   _powerTexts    = null;
        [SerializeField] private TMP_Text[]   _levelTexts    = null;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 캐릭터 정보를 칸에 쓴다. 이름이 null이면 빈 칸으로 보인다 </summary>
        public void Show(string characterName, string className)
        {
            bool filled = characterName != null;
            SetActive(_filledObjects, filled);
            SetText(_nameTexts, filled ? characterName : EMPTY_NAME);
            SetText(_classTexts, filled ? className : string.Empty);

            // 목록 API가 아직 전투력과 레벨을 주지 않는다. 받을 때까지 값 없음으로 둔다
            SetText(_powerTexts, filled ? UNKNOWN_POWER : string.Empty);
            SetText(_levelTexts, filled ? UNKNOWN_LEVEL : string.Empty);
        }

        private static void SetActive(GameObject[] targets, bool active)
        {
            if (targets == null)
            {
                return;
            }

            for (int i = 0; i < targets.Length; ++i)
            {
                if (targets[i] != null)
                {
                    targets[i].SetActive(active);
                }
            }
        }

        private static void SetText(TMP_Text[] targets, string value)
        {
            if (targets == null)
            {
                return;
            }

            for (int i = 0; i < targets.Length; ++i)
            {
                if (targets[i] != null)
                {
                    targets[i].text = value;
                }
            }
        }
    }
}
