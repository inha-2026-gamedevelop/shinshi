// System
using System;

// Unity
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 친구, 길드 창 목록의 한 줄. 접속 점, 이름, 보조 글자 둘, 버튼 최대 3개 </summary>
    public sealed class SocialRowView : MonoBehaviour
    {
        // 버튼 글자가 비어 있으면 그 버튼을 숨긴다. 줄이 무엇을 뜻하는지는 바인더가 탭마다 정한다

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private Image      _presenceDot  = null;
        [SerializeField] private TMP_Text   _nameLabel    = null;
        [SerializeField] private TMP_Text   _detailLabel  = null;
        [SerializeField] private TMP_Text   _infoLabel    = null;
        [SerializeField] private Button[]   _buttons      = null;
        [SerializeField] private TMP_Text[] _buttonLabels = null;

        [Header("접속 점 색")]
        [SerializeField] private Color _townColor    = new Color(0.463f, 0.902f, 0.741f, 1f);
        [SerializeField] private Color _dungeonColor = new Color(0.91f, 0.737f, 0.408f, 1f);
        [SerializeField] private Color _offlineColor = new Color(0.4f, 0.45f, 0.5f, 1f);

        private UnityAction[] _handlers;
        private Color         _infoDefaultColor = Color.white;

        /****************************************
        *              Properties
        ****************************************/

        public int ButtonCount => (_buttons == null) ? 0 : _buttons.Length;

        public event Action<int> ButtonClicked;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if (_infoLabel != null)
            {
                _infoDefaultColor = _infoLabel.color;
            }

            if (_buttons == null)
            {
                return;
            }

            // 버튼마다 자기 번호를 넘겨야 해서 처리기를 따로 만들어 두고 해제할 때 같은 것을 쓴다
            _handlers = new UnityAction[_buttons.Length];
            for (int i = 0; i < _buttons.Length; ++i)
            {
                int index = i;
                _handlers[i] = () => ButtonClicked?.Invoke(index);
                if (_buttons[i] != null)
                {
                    _buttons[i].onClick.AddListener(_handlers[i]);
                }
            }
        }

        private void OnDestroy()
        {
            if ((_buttons == null) || (_handlers == null))
            {
                return;
            }

            for (int i = 0; i < _buttons.Length; ++i)
            {
                if ((_buttons[i] != null) && (i < _handlers.Length))
                {
                    _buttons[i].onClick.RemoveListener(_handlers[i]);
                }
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible)
            {
                gameObject.SetActive(visible);
            }
        }

        public void SetTexts(string name, string detail, string info)
        {
            SetText(_nameLabel, name);
            SetText(_detailLabel, detail);
            SetText(_infoLabel, info);
        }

        /// <summary> 접속 점을 보이거나 숨긴다. 요청, 초대 줄에는 접속 위치가 없어 숨긴다 </summary>
        // 접속 위치 글자도 점과 같은 색으로 칠해 신시, 출정 중, 오프라인을 글자만으로도 구분하게 한다
        public void SetPresence(bool visible, EPresence presence)
        {
            Color color = (presence == EPresence.Town) ? _townColor
                : (presence == EPresence.Dungeon) ? _dungeonColor : _offlineColor;
            if (_infoLabel != null)
            {
                _infoLabel.color = visible ? color : _infoDefaultColor;
            }

            if (_presenceDot == null)
            {
                return;
            }

            _presenceDot.enabled = visible;
            _presenceDot.color   = color;
        }

        /// <summary> 버튼 하나를 정한다. 글자가 비면 숨긴다 </summary>
        public void SetButton(int index, string label, bool interactable)
        {
            if ((_buttons == null) || (index < 0) || (index >= _buttons.Length) || (_buttons[index] == null))
            {
                return;
            }

            bool visible = !string.IsNullOrEmpty(label);
            if (_buttons[index].gameObject.activeSelf != visible)
            {
                _buttons[index].gameObject.SetActive(visible);
            }

            _buttons[index].interactable = interactable;
            if ((_buttonLabels != null) && (index < _buttonLabels.Length))
            {
                SetText(_buttonLabels[index], label);
            }
        }

        public void HideButtons()
        {
            for (int i = 0; i < ButtonCount; ++i)
            {
                SetButton(i, null, false);
            }
        }

        private static void SetText(TMP_Text label, string text)
        {
            if (label != null)
            {
                label.text = text ?? string.Empty;
            }
        }
    }
}
