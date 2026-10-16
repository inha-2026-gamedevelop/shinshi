// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Systems
{
    /// <summary> 창 단축키의 기본 배치. 로컬 표현 전용이며 서버 판정과 GameDB 콘텐츠 해시에 넣지 않는다 </summary>
    // 키를 바꿀 때마다 서버까지 다시 배포하지 않도록 GameDB 루트에 넣지 않고 HotkeyRouter가 직접 참조한다.
    // 사용자별 키 변경이 생기면 이 에셋은 기본값으로 두고 바꾼 값은 따로 저장한다
    [CreateAssetMenu(fileName = "HotkeyConfig", menuName = "Shinshi/HotkeyConfigSO")]
    public class HotkeyConfigSO : ScriptableObject, IDataValidatable
    {
        /// <summary> 동작 하나와 키 하나의 짝 </summary>
        [Serializable]
        public class Binding
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private EHotkeyAction _action;
            [SerializeField] private KeyCode       _key;
            [SerializeField] private string        _label; // 설정 화면과 안내에 보여 줄 동작 이름

            public EHotkeyAction Action => _action;
            public KeyCode       Key    => _key;
            public string        Label  => _label;

            /****************************************
            *                Methods
            ****************************************/

            public Binding(EHotkeyAction action, KeyCode key, string label)
            {
                _action = action;
                _key    = key;
                _label  = label;
            }
        }

        /****************************************
        *                Fields
        ****************************************/

        // 전투 입력이 직접 읽는 키다. 창 단축키가 같은 키를 쓰면 한 번 눌러 두 동작이 함께 일어난다
        private static readonly KeyCode[] G_CombatKeys =
        {
            Constants.Player.KEY_DODGE,
            Constants.Player.KEY_SKILL_Q, Constants.Player.KEY_SKILL_W, Constants.Player.KEY_SKILL_E, Constants.Player.KEY_SKILL_R,
            Constants.Player.KEY_ASCENSION_Z, Constants.Player.KEY_ASCENSION_X, Constants.Player.KEY_ASCENSION_C,
            Constants.Player.KEY_CANCEL_TARGETING,
            Constants.Player.KEY_POTION_SMALL, Constants.Player.KEY_POTION_LARGE
        };

        [SerializeField] private Binding[] _bindings =
        {
            new Binding(EHotkeyAction.Inventory, KeyCode.I, "인벤토리"),
            new Binding(EHotkeyAction.AuctionHouse, KeyCode.Y, "경매장"),
            new Binding(EHotkeyAction.CloseWindow, KeyCode.Escape, "창 닫기"),
            new Binding(EHotkeyAction.SortiePrep, KeyCode.P, "출정 준비"),
            new Binding(EHotkeyAction.Chat, KeyCode.Return, "채팅"),
            new Binding(EHotkeyAction.CombatMeter, KeyCode.L, "딜미터"),
            new Binding(EHotkeyAction.MailBox, KeyCode.M, "우편함"),
            new Binding(EHotkeyAction.CharacterInfo, KeyCode.U, "캐릭터 정보"),
            new Binding(EHotkeyAction.EquipmentOption, KeyCode.O, "옵션 재설정"),
            new Binding(EHotkeyAction.SkillWindow, KeyCode.K, "스킬"),
            new Binding(EHotkeyAction.Friends, KeyCode.N, "친구"),
            new Binding(EHotkeyAction.Guild, KeyCode.G, "길드")
        };

        public IReadOnlyList<Binding> Bindings => _bindings;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 동작에 묶인 키를 찾는다, 없으면 거짓 </summary>
        public bool TryGetKey(EHotkeyAction action, out KeyCode key)
        {
            int count = (_bindings == null) ? 0 : _bindings.Length;
            for (int i = 0; i < count; ++i)
            {
                Binding binding = _bindings[i];
                if ((binding != null) && (binding.Action == action))
                {
                    key = binding.Key;
                    return true;
                }
            }

            key = KeyCode.None;
            return false;
        }

        public void Validate(List<string> errors)
        {
            const string OWNER = nameof(HotkeyConfigSO);

            if ((_bindings == null) || (_bindings.Length == 0))
            {
                errors.Add($"{OWNER}.{nameof(_bindings)}: 배치가 없다");
                return;
            }

            var actions = new HashSet<EHotkeyAction>();
            var keys    = new HashSet<KeyCode>();
            for (int i = 0; i < _bindings.Length; ++i)
            {
                Binding binding = _bindings[i];
                string  field   = $"{OWNER}.{nameof(_bindings)}[{i}]";
                if (binding == null)
                {
                    errors.Add($"{field}: 비어 있다");
                    continue;
                }

                if ((binding.Action == EHotkeyAction.None) || (!Enum.IsDefined(typeof(EHotkeyAction), binding.Action)))
                {
                    errors.Add($"{field}: 동작이 없다");
                }
                else if (!actions.Add(binding.Action))
                {
                    errors.Add($"{field}: {binding.Action} 동작이 두 번 있다");
                }

                if (string.IsNullOrWhiteSpace(binding.Label))
                {
                    errors.Add($"{field}: 표시 이름이 없다");
                }

                if (binding.Key == KeyCode.None)
                {
                    errors.Add($"{field}: 키가 없다");
                    continue;
                }

                if (!keys.Add(binding.Key))
                {
                    errors.Add($"{field}: {binding.Key} 키를 다른 동작이 이미 쓴다");
                }

                if (IsCombatKeyConflict(binding))
                {
                    errors.Add($"{field}: {binding.Key} 키는 전투 입력이 쓴다");
                }
            }
        }

        // 창 닫기의 Esc는 지점 지정 취소와 같은 키를 일부러 함께 쓴다. 지정 중에는 보통 열린 창이 없다
        private static bool IsCombatKeyConflict(Binding binding)
        {
            if ((binding.Action == EHotkeyAction.CloseWindow) && (binding.Key == Constants.Player.KEY_CANCEL_TARGETING))
            {
                return false;
            }

            return Array.IndexOf(G_CombatKeys, binding.Key) >= 0;
        }

        // 인스펙터에서 값을 바꾸는 즉시 오류를 보여 준다
        private void OnValidate()
        {
            var errors = new List<string>();
            Validate(errors);

            for (int i = 0; i < errors.Count; ++i)
            {
                Debug.LogError($"[Hotkey] {errors[i]}", this);
            }
        }
    }
}
