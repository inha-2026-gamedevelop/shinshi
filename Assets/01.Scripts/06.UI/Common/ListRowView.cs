// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary> 목록 한 줄. 글자 칸 여러 개를 순서대로 채우고 누르면 알린다. 경매장, 거래, 우편함 목록이 함께 쓴다 </summary>
    public sealed class ListRowView : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private Button     _button       = null;
        [SerializeField] private TMP_Text[] _columns      = null;
        [SerializeField] private GameObject _selectedMark = null;
        [SerializeField] private Image      _gradeBar     = null;
        [SerializeField] private Color[]    _gradeColors  = null;

        /****************************************
        *              Properties
        ****************************************/

        public int Index { get; set; }

        public event Action<ListRowView> Clicked;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if (_button != null)
            {
                _button.onClick.AddListener(OnClicked);
            }
        }

        private void OnDestroy()
        {
            if (_button != null)
            {
                _button.onClick.RemoveListener(OnClicked);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 칸 수보다 많은 글자는 버리고, 모자라는 칸은 비운다 </summary>
        public void SetColumns(IReadOnlyList<string> values)
        {
            int count = (_columns != null) ? _columns.Length : 0;
            for (int i = 0; i < count; ++i)
            {
                if (_columns[i] != null)
                {
                    _columns[i].text = ((values != null) && (i < values.Count)) ? values[i] : string.Empty;
                }
            }
        }

        public void SetGrade(int grade)
        {
            if ((_gradeBar != null) && (_gradeColors != null) && (grade >= 0) && (grade < _gradeColors.Length))
            {
                _gradeBar.color = _gradeColors[grade];
            }
        }

        public void SetSelected(bool selected)
        {
            if (_selectedMark != null)
            {
                _selectedMark.SetActive(selected);
            }
        }

        public void SetVisible(bool visible)
        {
            gameObject.SetActive(visible);
        }

        public void SetInteractable(bool interactable)
        {
            if (_button != null)
            {
                _button.interactable = interactable;
            }
        }

        private void OnClicked()
        {
            Clicked?.Invoke(this);
        }
    }
}
