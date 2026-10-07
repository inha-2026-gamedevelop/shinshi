// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.EventSystems;

// 서드파티
using TMPro;

namespace Shinshi.Systems
{
    /// <summary> 단축키 설정 에셋의 키를 읽어, 동작마다 등록된 처리기를 부른다. 씬마다 하나 둔다 </summary>
    // 처리기는 처리했으면 참을 돌려준다. 나중에 등록한 처리기부터 부르고 참이 나오면 멈춘다.
    // 창을 열 때 닫기 처리기를 다시 등록하면 가장 최근에 연 창이 Esc를 먼저 받는다
    public sealed class HotkeyRouter : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private HotkeyConfigSO _config = null;

        // 인덱스는 EHotkeyAction 값이다. 키가 없는 동작은 KeyCode.None으로 남는다
        private KeyCode[]          _keys     = null;
        private List<Func<bool>>[] _handlers = null;

        /****************************************
        *              Properties
        ****************************************/

        public HotkeyConfigSO Config => _config;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            EnsureTables();
            if (_config == null)
            {
                Debug.LogError($"[Hotkey] {nameof(HotkeyRouter)}에 단축키 설정 에셋이 없어 단축키를 끈다", this);
                enabled = false;
                return;
            }

            var errors = new List<string>();
            _config.Validate(errors);
            if (errors.Count > 0)
            {
                Debug.LogError($"[Hotkey] 단축키 설정 오류로 단축키를 끈다: {string.Join(" / ", errors)}", this);
                enabled = false;
                return;
            }

            for (int i = 0; i < _keys.Length; ++i)
            {
                _config.TryGetKey((EHotkeyAction)i, out _keys[i]);
            }
        }

        // 눌린 키가 있을 때만 입력 칸 포커스를 확인한다
        private void Update()
        {
            for (int i = 0; i < _keys.Length; ++i)
            {
                KeyCode key = _keys[i];
                if ((key == KeyCode.None) || (_handlers[i].Count == 0) || (!Input.GetKeyDown(key)))
                {
                    continue;
                }

                if (IsTyping())
                {
                    return;
                }

                Dispatch(_handlers[i]);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 동작에 처리기를 붙인다. 이미 있으면 맨 뒤로 옮겨 가장 먼저 받게 한다 </summary>
        public void Register(EHotkeyAction action, Func<bool> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            List<Func<bool>> list = GetList(action);
            list.Remove(handler);
            list.Add(handler);
        }

        public void Unregister(EHotkeyAction action, Func<bool> handler)
        {
            if (handler == null)
            {
                return;
            }

            GetList(action).Remove(handler);
        }

        /// <summary> 동작의 키 이름, 안내 문구에 쓴다. 키가 없으면 빈 문자열 </summary>
        public string GetKeyLabel(EHotkeyAction action)
        {
            if ((_config == null) || (!_config.TryGetKey(action, out KeyCode key)))
            {
                return string.Empty;
            }

            return key.ToString();
        }

        /// <summary> 키 입력 없이 동작을 부른다, 처리한 처리기가 있으면 참. 버튼과 시험에서 쓴다 </summary>
        public bool Trigger(EHotkeyAction action)
        {
            return Dispatch(GetList(action));
        }

        // 처리기가 자기를 등록 해제해도 건너뛰지 않도록 뒤에서부터 돈다
        private static bool Dispatch(List<Func<bool>> list)
        {
            for (int i = list.Count - 1; i >= 0; --i)
            {
                if (i >= list.Count)
                {
                    continue;
                }

                if (list[i]())
                {
                    return true;
                }
            }

            return false;
        }

        private List<Func<bool>> GetList(EHotkeyAction action)
        {
            EnsureTables();
            int index = (int)action;
            if ((action == EHotkeyAction.None) || (index < 0) || (index >= _handlers.Length))
            {
                throw new ArgumentOutOfRangeException(nameof(action), action, "등록할 수 없는 단축키 동작이다");
            }

            return _handlers[index];
        }

        // 다른 컴포넌트의 OnEnable이 이 Awake보다 먼저 등록할 수 있어 처음 쓰일 때 만든다
        private void EnsureTables()
        {
            if (_handlers != null)
            {
                return;
            }

            Array values = Enum.GetValues(typeof(EHotkeyAction));
            int   size   = 0;
            for (int i = 0; i < values.Length; ++i)
            {
                size = Mathf.Max(size, (int)values.GetValue(i) + 1);
            }

            _keys     = new KeyCode[size];
            _handlers = new List<Func<bool>>[size];
            for (int i = 0; i < size; ++i)
            {
                _handlers[i] = new List<Func<bool>>();
            }
        }

        // 채팅이나 이름 입력 중에 글자를 치면 창이 열리지 않게 한다
        private static bool IsTyping()
        {
            EventSystem system = EventSystem.current;
            GameObject  focus  = (system != null) ? system.currentSelectedGameObject : null;
            if (focus == null)
            {
                return false;
            }

            if ((focus.TryGetComponent(out TMP_InputField tmpField)) && (tmpField.isFocused))
            {
                return true;
            }

            return (focus.TryGetComponent(out UnityEngine.UI.InputField field)) && (field.isFocused);
        }
    }
}
