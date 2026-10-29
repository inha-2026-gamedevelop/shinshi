// System
using System;

// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary> 캐릭터 상세 창이며 이름, 직업, 능력치, 부위별 착용 장비, 수련, 부적 요약을 보인다. 데이터는 바인더가 채운다 </summary>
    public sealed class CharDetailView : UiWindowView
    {
        // 부위 배열은 EEquipmentSlot 순서(무기, 머리, 몸통, 손, 발, 정기), 수련 배열은 ETrainingStat 순서(공격, 체력, 방어, 치명타, 기력)와 같게 연결한다

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private Button   _closeButton = null;
        [SerializeField] private TMP_Text _nameLabel   = null;
        [SerializeField] private TMP_Text _classLabel  = null;

        [Header("능력치")]
        [SerializeField] private TMP_Text _attackLabel  = null;
        [SerializeField] private TMP_Text _healthLabel  = null;
        [SerializeField] private TMP_Text _defenseLabel = null;
        [SerializeField] private TMP_Text _critLabel    = null;
        [SerializeField] private TMP_Text _energyLabel  = null;
        [SerializeField] private TMP_Text _speedLabel   = null;
        [SerializeField] private TMP_Text _lifeStealLabel   = null;
        [SerializeField] private TMP_Text _combatPowerLabel = null;

        [Header("장비 (부위 순서)")]
        [SerializeField] private TMP_Text[] _slotNameLabels    = null;
        [SerializeField] private TMP_Text[] _slotEnhanceLabels = null;

        [Header("수련, 부적")]
        [SerializeField] private TMP_Text[] _trainingLabels = null;
        [SerializeField] private TMP_Text   _talismanLabel  = null;

        [Header("미리보기")]
        [SerializeField] private GameObject _previewStage = null;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsOpen => gameObject.activeSelf;

        public event Action Closed;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            CharDetailModernLayout.Apply(this);

            if (_closeButton != null)
            {
                _closeButton.onClick.AddListener(OnCloseClicked);
            }
        }

        private void OnDestroy()
        {
            if (_closeButton != null)
            {
                _closeButton.onClick.RemoveListener(OnCloseClicked);
            }
        }

        // 단축키와 메뉴는 창의 활성 상태를 직접 바꾸므로 미리보기의 수명도 함께 맞춘다.
        protected override void OnEnable()
        {
            base.OnEnable();
            if (_previewStage != null)
            {
                _previewStage.SetActive(true);
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (_previewStage != null)
            {
                _previewStage.SetActive(false);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public void SetOpen(bool open)
        {
            gameObject.SetActive(open);
            if (_previewStage != null)
            {
                _previewStage.SetActive(open);
            }
        }

        public void SetIdentity(string characterName, string className)
        {
            SetText(_nameLabel, characterName);
            SetText(_classLabel, className);
        }

        /// <summary> 능력치를 계산하지 못했으면 빈 글자로 둔다 </summary>
        public void SetStats(string attack, string health, string defense, string crit, string energy, string speed)
        {
            SetText(_attackLabel, attack);
            SetText(_healthLabel, health);
            SetText(_defenseLabel, defense);
            SetText(_critLabel, crit);
            SetText(_energyLabel, energy);
            SetText(_speedLabel, speed);
        }

        // SetStats와 나눠 둔다. 인자를 늘리면 샌드박스와 미리보기의 기존 호출이 모두 깨진다
        public void SetLifeSteal(string lifeSteal)
        {
            SetText(_lifeStealLabel, lifeSteal);
        }

        /// <summary> 종합 전투력 글자, 계산하지 못했으면 빈 글자로 둔다 </summary>
        public void SetCombatPower(string combatPower)
        {
            SetText(_combatPowerLabel, combatPower);
        }

        public void SetSlot(int slot, string equipmentName, string enhancement)
        {
            if ((_slotNameLabels != null) && (slot >= 0) && (slot < _slotNameLabels.Length))
            {
                SetText(_slotNameLabels[slot], equipmentName);
            }

            if ((_slotEnhanceLabels != null) && (slot >= 0) && (slot < _slotEnhanceLabels.Length))
            {
                SetText(_slotEnhanceLabels[slot], enhancement);
            }
        }

        public void SetTraining(int stat, string text)
        {
            if ((_trainingLabels != null) && (stat >= 0) && (stat < _trainingLabels.Length))
            {
                SetText(_trainingLabels[stat], text);
            }
        }

        public void SetTalismanSummary(string text)
        {
            SetText(_talismanLabel, text);
        }

        private static void SetText(TMP_Text label, string text)
        {
            if (label != null)
            {
                label.text = text ?? string.Empty;
            }
        }

        private void OnCloseClicked()
        {
            SetOpen(false);
            Closed?.Invoke();
        }
    }
}
