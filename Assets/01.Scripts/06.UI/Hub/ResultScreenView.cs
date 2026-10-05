// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 프로젝트
using Shinshi.Common;

namespace Shinshi.UI
{
    /// <summary>
    /// 전투 결과와 정산 화면. 확정 보상, 확률 보상, 정기 증감을 보여 주고 저장이 끝나기 전에는 귀환할 수 없게 막는다.
    /// 저장 상태는 서버 정산 영수증을 기준으로 호출하는 쪽이 넘긴다
    /// </summary>
    public sealed class ResultScreenView : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string SUCCESS_TITLE    = "토벌 성공";
        private const string TIMEOUT_TITLE    = "토벌 실패 - 시간 초과";
        private const string ALL_DEAD_TITLE   = "토벌 실패 - 전멸";
        private const string ABANDON_TITLE    = "토벌 중도 포기";
        private const string OTHER_TITLE      = "전투 종료";
        private const string BASE_FORMAT      = "+{0:N0}";
        private const string EXTRA_FORMAT     = "+{0:N0}";
        private const string LOST_FORMAT      = "손실 -{0:N0}";
        private const string RETURN_TEXT      = "귀환";
        private const string WAIT_HINT        = "저장이 끝나면 귀환할 수 있습니다";
        private const string EMPTY_HINT       = "없음";

        [Header("제목")]
        [SerializeField] private TMP_Text _titleLabel  = null;
        [SerializeField] private TMP_Text _regionLabel = null;

        [Header("정기")]
        [SerializeField] private TMP_Text _baseLabel  = null;
        [SerializeField] private TMP_Text _extraLabel = null;
        [SerializeField] private TMP_Text _lostLabel  = null;

        [Header("보상")]
        [SerializeField] private ItemSlotGridView _confirmedGrid = null;
        [SerializeField] private ItemSlotGridView _chanceGrid    = null;
        [SerializeField] private TMP_Text         _confirmedEmpty = null;
        [SerializeField] private TMP_Text         _chanceEmpty    = null;

        [Header("하단")]
        [SerializeField] private SaveStatusView _saveStatus   = null;
        [SerializeField] private Button         _returnButton = null;
        [SerializeField] private TMP_Text       _returnLabel  = null;
        [SerializeField] private TMP_Text       _hintLabel    = null;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary>귀환 버튼을 눌렀을 때 알린다. 저장이 끝난 뒤에만 눌린다</summary>
        public event Action ReturnClicked;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            if (_returnButton != null)
            {
                _returnButton.onClick.AddListener(OnReturn);
            }
        }

        private void OnDisable()
        {
            if (_returnButton != null)
            {
                _returnButton.onClick.RemoveListener(OnReturn);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public void SetResult(ResultScreenData data)
        {
            if (data == null)
            {
                return;
            }

            SetText(_titleLabel, ToTitle(data.Reason));
            SetText(_regionLabel, data.RegionName);
            SetText(_baseLabel, string.Format(BASE_FORMAT, data.BaseEssence));
            SetText(_extraLabel, string.Format(EXTRA_FORMAT, data.ExtraEssence));

            if (_lostLabel != null)
            {
                _lostLabel.gameObject.SetActive(data.LostEssence > 0);
                _lostLabel.text = string.Format(LOST_FORMAT, data.LostEssence);
            }

            ApplyItems(_confirmedGrid, _confirmedEmpty, data.ConfirmedItems);
            ApplyItems(_chanceGrid, _chanceEmpty, data.ChanceItems);
        }

        /// <summary>저장이 끝나기 전에는 귀환 버튼을 막고 안내 글자를 보여 준다</summary>
        public void SetSaveStatus(ESaveStatus status)
        {
            if (_saveStatus != null)
            {
                _saveStatus.SetStatus(status);
            }

            bool saved = (status == ESaveStatus.Saved);

            if (_returnButton != null)
            {
                _returnButton.interactable = saved;
            }

            SetText(_returnLabel, RETURN_TEXT);

            if (_hintLabel != null)
            {
                _hintLabel.gameObject.SetActive(!saved);
                _hintLabel.text = WAIT_HINT;
            }
        }

        private static void ApplyItems(ItemSlotGridView grid, TMP_Text emptyLabel, IReadOnlyList<ItemSlotData> items)
        {
            bool has = (items != null) && (items.Count > 0);

            if (grid != null)
            {
                grid.gameObject.SetActive(has);
                grid.SetItems(items);
            }

            if (emptyLabel != null)
            {
                emptyLabel.gameObject.SetActive(!has);
                emptyLabel.text = EMPTY_HINT;
            }
        }

        private static string ToTitle(ESessionEndReason reason)
        {
            switch (reason)
            {
                case ESessionEndReason.Success:
                    return SUCCESS_TITLE;
                case ESessionEndReason.Timeout:
                    return TIMEOUT_TITLE;
                case ESessionEndReason.AllDead:
                    return ALL_DEAD_TITLE;
                case ESessionEndReason.Abandon:
                    return ABANDON_TITLE;
                default:
                    return OTHER_TITLE;
            }
        }

        private static void SetText(TMP_Text label, string text)
        {
            if (label != null)
            {
                label.text = text;
            }
        }

        private void OnReturn()
        {
            Action handler = ReturnClicked;

            if (handler != null)
            {
                handler();
            }
        }
    }
}
