// System
using System;

// Unity
using UnityEngine;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Contracts;

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
        [SerializeField] private TMP_Text     _errorText     = null;

        // 칸마다 저장된 외형을 직접 조립해 보여 준다
        [SerializeField] private PlayerFemaleLookPreviewView _preview = null;

        // 해석/조립이 실패해도 이전에 성공했던 preview의 IsReady 캐시가 그대로 남아 있을 수 있다.
        // 이 값으로 그 성공 캐시를 가리지 않고 실제 이번 해석 결과만 본다
        private bool   _resolvedValid = true;
        private string _lastError     = string.Empty;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsLookReady     => (_resolvedValid) && ((_preview == null) || (_preview.IsReady));
        public bool IsResolvedValid => _resolvedValid;

        // Screen이 공용 _status에 표시할 오류. 해석 실패면 그 사유를, 해석은 됐지만 조립이 실패했으면
        // preview.LastError를 돌려준다. 둘 다 아니면 빈 문자열이다
        public string LastError
        {
            get
            {
                if (!_resolvedValid)
                {
                    return _lastError;
                }

                return ((_preview != null) && (!_preview.IsReady)) ? _preview.LastError : string.Empty;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 캐릭터 정보를 칸에 쓴다. 이름이 null이면 빈 칸으로 보인다 </summary>
        public void Show(string characterName, string className, int level = 0)
        {
            bool filled = characterName != null;
            SetActive(_filledObjects, filled);
            SetText(_nameTexts, filled ? characterName : EMPTY_NAME);
            SetText(_classTexts, filled ? className : string.Empty);

            // 전투력은 아직 목록 API가 주지 않는다
            SetText(_powerTexts, filled ? UNKNOWN_POWER : string.Empty);
            SetText(_levelTexts, filled ? (level > 0 ? "Lv." + level.ToString(System.Globalization.CultureInfo.InvariantCulture) : UNKNOWN_LEVEL) : string.Empty);

            // 빈 칸으로 다시 쓰이면 이전 캐릭터의 해석 실패 상태를 들고 있지 않는다. ShowLook이 다시 불리면 덮인다
            if (!filled)
            {
                _resolvedValid = true;
                _lastError     = string.Empty;
                SetText(new[] { _errorText }, string.Empty);
            }
        }

        /// <summary> classId로 TryResolve한 외형을 조립한다. 해석/조립 실패는 오류를 보이고 시작을 막는다 </summary>
        public void ShowLook(int classId, CharacterAppearance storedAppearance)
        {
            if (_preview == null)
            {
                return;
            }

            if ((!CharacterAppearanceCatalog.TryResolve(classId, storedAppearance, out CharacterAppearance canonical))
                || (!CharacterP09Look.TryFrom(canonical, out CharacterP09Look look)))
            {
                _resolvedValid = false;
                _lastError     = "외형을 불러올 수 없다.";
                SetText(new[] { _errorText }, _lastError);
                return;
            }

            _resolvedValid = true;
            _lastError     = string.Empty;
            SetText(new[] { _errorText }, string.Empty);
            _preview.Show(look);
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
