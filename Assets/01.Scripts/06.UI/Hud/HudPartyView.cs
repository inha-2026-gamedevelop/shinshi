// System
using System;
using System.Globalization;

// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.UI
{
    /// <summary> HUD 파티 칸마다 이름, 직업 표지, 체력을 보여 준다. 값은 바인더가 서버 상태에서 넣는다 </summary>
    public sealed class HudPartyView : MonoBehaviour
    {
        /// <summary> 파티원 한 칸 </summary>
        [Serializable]
        private sealed class Slot
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private GameObject    _root   = null;
            [SerializeField] private TMP_Text      _name   = null;
            [SerializeField] private Image         _badge  = null;
            [SerializeField] private RectTransform _hpFill = null;
            [SerializeField] private TMP_Text      _hpText = null;

            // 값이 같으면 글자를 다시 만들지 않는다
            [NonSerialized] public bool         HasValue  = false;
            [NonSerialized] public string       LastName  = null;
            [NonSerialized] public EPlayerClass LastClass = EPlayerClass.Warrior;
            [NonSerialized] public int          LastHp    = -1;
            [NonSerialized] public int          LastMaxHp = -1;

            public GameObject    Root   => _root;
            public TMP_Text      Name   => _name;
            public Image         Badge  => _badge;
            public RectTransform HpFill => _hpFill;
            public TMP_Text      HpText => _hpText;
        }

        /****************************************
        *                Fields
        ****************************************/

        private const string VALUE_FORMAT = "{0:N0} / {1:N0}";
        private const string NO_HP_TEXT   = "-";

        [SerializeField] private Slot[] _slots           = null;
        [SerializeField] private Sprite _warriorBadge    = null;
        [SerializeField] private Sprite _sorcererBadge   = null;
        [SerializeField] private Sprite _summonerBadge   = null;
        [SerializeField] private Sprite _swordsmanBadge  = null;
        [SerializeField] private Sprite _mageknightBadge = null;
        [SerializeField] private Sprite _archerBadge     = null;

        /****************************************
        *              Properties
        ****************************************/

        public int SlotCount => (_slots == null) ? 0 : _slots.Length;

        /****************************************
        *                Methods
        ****************************************/

        private void Awake()
        {
            EnsureModernLayout();
        }

        /// <summary>파티 칸마다 어두운 바탕을 한 번만 깐다. 입력은 막지 않는다</summary>
        public void EnsureModernLayout()
        {
            if (_slots == null)
            {
                return;
            }

            for (int i = 0; i < _slots.Length; ++i)
            {
                var root = ModernDarkKit.Field<GameObject>(_slots[i], "_root");
                if (root != null)
                {
                    HudModernStyle.AddBackdrop(root.transform, UiModernDark.SPRITE_INSET, Color.white, 4f);
                }
            }
        }

        /// <summary> hasHp가 거짓이면 체력 막대를 비우고 수치 대신 '-'를 쓴다 </summary>
        public void SetMember(int index, string memberName, EPlayerClass playerClass, bool hasHp, int hp, int maxHp)
        {
            Slot slot = GetSlot(index);
            if (slot == null)
            {
                return;
            }

            if (!hasHp)
            {
                hp    = -1;
                maxHp = -1;
            }

            if ((slot.HasValue) && (slot.LastName == memberName) && (slot.LastClass == playerClass)
                && (slot.LastHp == hp) && (slot.LastMaxHp == maxHp))
            {
                return;
            }

            slot.HasValue  = true;
            slot.LastName  = memberName;
            slot.LastClass = playerClass;
            slot.LastHp    = hp;
            slot.LastMaxHp = maxHp;

            SetActive(slot.Root, true);
            if (slot.Name != null)
            {
                slot.Name.SetText(memberName ?? string.Empty);
            }

            Sprite badge = FindBadge(playerClass);
            if (slot.Badge != null)
            {
                slot.Badge.sprite = badge;
                slot.Badge.enabled = (badge != null);
            }

            ApplyHp(slot, hasHp, hp, maxHp);
        }

        public void ClearMember(int index)
        {
            Slot slot = GetSlot(index);
            if (slot == null)
            {
                return;
            }

            slot.HasValue = false;
            SetActive(slot.Root, false);
            if (slot.Badge != null)
            {
                slot.Badge.sprite = null;
                slot.Badge.enabled = false;
            }
        }

        private Slot GetSlot(int index)
        {
            if ((_slots == null) || (index < 0) || (index >= _slots.Length))
            {
                return null;
            }

            return _slots[index];
        }

        private Sprite FindBadge(EPlayerClass playerClass)
        {
            switch (playerClass)
            {
                case EPlayerClass.Warrior:
                    return _warriorBadge;
                case EPlayerClass.Sorcerer:
                    return _sorcererBadge;
                case EPlayerClass.Summoner:
                    return _summonerBadge;
                case EPlayerClass.Swordsman:
                    return _swordsmanBadge;
                case EPlayerClass.Mageknight:
                    return _mageknightBadge;
                case EPlayerClass.Archer:
                    return _archerBadge;
                default:
                    return null;
            }
        }

        // 9분할 채움 칸의 오른쪽 앵커를 줄인다. HudPlayerStatusView와 같은 방식이라 막대 끝 모양이 유지된다
        private static void ApplyHp(Slot slot, bool hasHp, int hp, int maxHp)
        {
            float ratio = ((hasHp) && (maxHp > 0)) ? Mathf.Clamp01((float)hp / maxHp) : 0f;
            if (slot.HpFill != null)
            {
                Vector2 anchorMax = slot.HpFill.anchorMax;
                anchorMax.x           = ratio;
                slot.HpFill.anchorMax = anchorMax;
            }

            if (slot.HpText != null)
            {
                slot.HpText.SetText(hasHp ? string.Format(CultureInfo.InvariantCulture, VALUE_FORMAT, hp, maxHp) : NO_HP_TEXT);
            }
        }

        private static void SetActive(GameObject target, bool active)
        {
            if ((target != null) && (target.activeSelf != active))
            {
                target.SetActive(active);
            }
        }
    }
}
