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
    /// 출정 준비 화면. 전투 종류와 난이도, 입장권, 장비·부적 요약, 물약 지참량, 파티 준비 상태를 보여 주고
    /// 사용자의 선택은 이벤트로 알린다. 값의 판정과 저장은 서버가 하며 이 뷰는 받은 값을 그대로 그린다
    /// </summary>
    public sealed class SortiePrepView : MonoBehaviour
    {
        /****************************************
        *                Types
        ****************************************/

        [Serializable]
        private struct MemberRow
        {
            public GameObject Root;
            public TMP_Text   NameLabel;
            public Image      ClassIcon;
            public TMP_Text   StateLabel;
        }

        /****************************************
        *                Fields
        ****************************************/

        private const int    POTION_KIND_COUNT = 2;
        private const string TICKET_FORMAT     = "보스 입장권 {0}/{1}";
        private const string POTION_FORMAT     = "{0} / {1}";
        private const string COUNTDOWN_FORMAT  = "{0}초 뒤 출정";
        private const string START_TEXT        = "출정";
        private const string READY_TEXT        = "준비";
        private const string UNREADY_TEXT      = "준비 해제";
        private const string LEADER_TEXT       = "방장";
        private const string READY_STATE       = "준비 완료";
        private const string WAIT_STATE        = "대기 중";
        private const string OFFLINE_STATE     = "접속 끊김";
        private const string LACK_STATE        = "조건 부족";
        private const string CLASS_ID_PREFIX   = "class_";

        [Header("전장")]
        [SerializeField] private Button   _waveModeButton = null;
        [SerializeField] private Button   _bossModeButton = null;
        [SerializeField] private TMP_Text _regionLabel    = null;
        [SerializeField] private Button   _easyButton     = null;
        [SerializeField] private Button   _hardButton     = null;
        [SerializeField] private GameObject _difficultyRoot = null;
        [SerializeField] private TMP_Text _ticketLabel    = null;

        [Header("요약")]
        [SerializeField] private ItemSlotGridView _equipmentGrid = null;
        [SerializeField] private ItemSlotGridView _talismanGrid  = null;

        [Header("물약 지참량 (소형, 대형 순서)")]
        [SerializeField] private Button[]   _potionMinusButtons = null;
        [SerializeField] private Button[]   _potionPlusButtons  = null;
        [SerializeField] private TMP_Text[] _potionLabels       = null;

        [Header("파티")]
        [SerializeField] private MemberRow[]    _members = null;
        [SerializeField] private UiIconLibrarySO _library = null;

        [Header("하단")]
        [SerializeField] private Button   _readyButton    = null;
        [SerializeField] private TMP_Text _readyLabel     = null;
        [SerializeField] private Button   _startButton    = null;
        [SerializeField] private TMP_Text _startLabel     = null;
        [SerializeField] private Button   _leaveButton    = null;
        [SerializeField] private TMP_Text _noticeLabel    = null;

        private readonly int[] _carry     = new int[POTION_KIND_COUNT];
        private readonly int[] _available = new int[POTION_KIND_COUNT];

        /****************************************
        *              Properties
        ****************************************/

        public event Action<ESortieMode>      ModeChanged;
        public event Action<EBossDifficulty>  DifficultyChanged;
        public event Action<EPotionSize, int> PotionChanged;
        public event Action                   ReadyClicked;
        public event Action                   StartClicked;
        public event Action                   LeaveClicked;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            AddListener(_waveModeButton, OnWaveMode);
            AddListener(_bossModeButton, OnBossMode);
            AddListener(_easyButton, OnEasy);
            AddListener(_hardButton, OnHard);
            AddListener(_readyButton, OnReady);
            AddListener(_startButton, OnStart);
            AddListener(_leaveButton, OnLeave);
            AddPotionListeners();
        }

        private void OnDisable()
        {
            RemoveListener(_waveModeButton, OnWaveMode);
            RemoveListener(_bossModeButton, OnBossMode);
            RemoveListener(_easyButton, OnEasy);
            RemoveListener(_hardButton, OnHard);
            RemoveListener(_readyButton, OnReady);
            RemoveListener(_startButton, OnStart);
            RemoveListener(_leaveButton, OnLeave);
            RemovePotionListeners();
        }

        /****************************************
        *                Methods
        ****************************************/

        public void SetMode(ESortieMode mode)
        {
            bool boss = (mode == ESortieMode.Boss);

            if (_difficultyRoot != null)
            {
                _difficultyRoot.SetActive(boss);
            }

            if (_ticketLabel != null)
            {
                _ticketLabel.gameObject.SetActive(boss);
            }

            SetInteractable(_waveModeButton, boss);
            SetInteractable(_bossModeButton, !boss);
        }

        public void SetRegion(string regionName)
        {
            if (_regionLabel != null)
            {
                _regionLabel.text = regionName;
            }
        }

        public void SetDifficulty(EBossDifficulty difficulty)
        {
            SetInteractable(_easyButton, difficulty != EBossDifficulty.Easy);
            SetInteractable(_hardButton, difficulty != EBossDifficulty.Hard);
        }

        public void SetTicket(int have, int need)
        {
            if (_ticketLabel != null)
            {
                _ticketLabel.text = string.Format(TICKET_FORMAT, have, need);
            }
        }

        public void SetEquipment(IReadOnlyList<ItemSlotData> items)
        {
            if (_equipmentGrid != null)
            {
                _equipmentGrid.SetItems(items);
            }
        }

        public void SetTalismans(IReadOnlyList<ItemSlotData> items)
        {
            if (_talismanGrid != null)
            {
                _talismanGrid.SetItems(items);
            }
        }

        /// <summary>지참량은 보유 재고 안으로 제한하며 줄이기와 늘리기 버튼의 사용 여부를 함께 맞춘다</summary>
        public void SetPotion(EPotionSize size, int carry, int available)
        {
            int index = (int)size;

            if ((index < 0) || (index >= POTION_KIND_COUNT))
            {
                return;
            }

            _available[index] = Mathf.Max(0, available);
            _carry[index]     = Mathf.Clamp(carry, 0, _available[index]);

            if ((_potionLabels != null) && (index < _potionLabels.Length) && (_potionLabels[index] != null))
            {
                _potionLabels[index].text = string.Format(POTION_FORMAT, _carry[index], _available[index]);
            }

            if ((_potionMinusButtons != null) && (index < _potionMinusButtons.Length))
            {
                SetInteractable(_potionMinusButtons[index], _carry[index] > 0);
            }

            if ((_potionPlusButtons != null) && (index < _potionPlusButtons.Length))
            {
                SetInteractable(_potionPlusButtons[index], _carry[index] < _available[index]);
            }
        }

        public void SetMembers(IReadOnlyList<SortieMemberData> members)
        {
            if (_members == null)
            {
                return;
            }

            for (int i = 0; i < _members.Length; ++i)
            {
                bool has = (members != null) && (i < members.Count) && !members[i].IsEmpty;

                if (_members[i].Root != null)
                {
                    _members[i].Root.SetActive(has);
                }

                if (has)
                {
                    ApplyMember(_members[i], members[i]);
                }
            }
        }

        /// <summary>방장은 출정 버튼, 나머지는 준비 버튼을 쓴다. 준비 상태에 따라 준비 버튼 글자가 바뀐다</summary>
        public void SetLocalState(bool isLeader, bool isReady)
        {
            if (_readyLabel != null)
            {
                _readyLabel.text = isReady ? UNREADY_TEXT : READY_TEXT;
            }

            if (_startButton != null)
            {
                _startButton.gameObject.SetActive(isLeader);
            }

            if (_readyButton != null)
            {
                _readyButton.gameObject.SetActive(!isLeader);
            }
        }

        public void SetStartEnabled(bool enabled) { SetInteractable(_startButton, enabled); }

        /// <summary>0 이하이면 출정 글자로 되돌린다</summary>
        public void SetCountdown(int seconds)
        {
            if (_startLabel != null)
            {
                _startLabel.text = (seconds > 0) ? string.Format(COUNTDOWN_FORMAT, seconds) : START_TEXT;
            }
        }

        public void SetNotice(string text)
        {
            if (_noticeLabel != null)
            {
                _noticeLabel.text = text;
            }
        }

        public void SetNotice(ERejectReason reason) { SetNotice(RejectText.Describe(reason)); }

        private void ApplyMember(MemberRow row, SortieMemberData data)
        {
            if (row.NameLabel != null)
            {
                row.NameLabel.text = data.Name;
            }

            if ((row.ClassIcon != null) && (_library != null))
            {
                Sprite icon = _library.FindIcon(CLASS_ID_PREFIX + data.PlayerClass.ToString().ToLowerInvariant());
                row.ClassIcon.sprite  = icon;
                row.ClassIcon.enabled = (icon != null);
            }

            if (row.StateLabel != null)
            {
                row.StateLabel.text = ResolveState(data);
            }
        }

        private static string ResolveState(SortieMemberData data)
        {
            if (!data.IsConnected)
            {
                return OFFLINE_STATE;
            }

            if (!data.MeetsRequirements)
            {
                return LACK_STATE;
            }

            if (data.IsLeader)
            {
                return LEADER_TEXT;
            }

            return data.IsReady ? READY_STATE : WAIT_STATE;
        }

        private void AddPotionListeners()
        {
            for (int i = 0; i < POTION_KIND_COUNT; ++i)
            {
                int index = i;

                if ((_potionMinusButtons != null) && (index < _potionMinusButtons.Length) && (_potionMinusButtons[index] != null))
                {
                    _potionMinusButtons[index].onClick.AddListener(() => ChangePotion(index, -1));
                }

                if ((_potionPlusButtons != null) && (index < _potionPlusButtons.Length) && (_potionPlusButtons[index] != null))
                {
                    _potionPlusButtons[index].onClick.AddListener(() => ChangePotion(index, 1));
                }
            }
        }

        private void RemovePotionListeners()
        {
            for (int i = 0; i < POTION_KIND_COUNT; ++i)
            {
                if ((_potionMinusButtons != null) && (i < _potionMinusButtons.Length) && (_potionMinusButtons[i] != null))
                {
                    _potionMinusButtons[i].onClick.RemoveAllListeners();
                }

                if ((_potionPlusButtons != null) && (i < _potionPlusButtons.Length) && (_potionPlusButtons[i] != null))
                {
                    _potionPlusButtons[i].onClick.RemoveAllListeners();
                }
            }
        }

        private void ChangePotion(int index, int delta)
        {
            int next = Mathf.Clamp(_carry[index] + delta, 0, _available[index]);

            if (next == _carry[index])
            {
                return;
            }

            Action<EPotionSize, int> handler = PotionChanged;

            if (handler != null)
            {
                handler((EPotionSize)index, next);
            }
        }

        private void OnWaveMode() { RaiseMode(ESortieMode.Wave); }
        private void OnBossMode() { RaiseMode(ESortieMode.Boss); }
        private void OnEasy()     { RaiseDifficulty(EBossDifficulty.Easy); }
        private void OnHard()     { RaiseDifficulty(EBossDifficulty.Hard); }

        private void OnReady()
        {
            Action handler = ReadyClicked;

            if (handler != null)
            {
                handler();
            }
        }

        private void OnStart()
        {
            Action handler = StartClicked;

            if (handler != null)
            {
                handler();
            }
        }

        private void OnLeave()
        {
            Action handler = LeaveClicked;

            if (handler != null)
            {
                handler();
            }
        }

        private void RaiseMode(ESortieMode mode)
        {
            Action<ESortieMode> handler = ModeChanged;

            if (handler != null)
            {
                handler(mode);
            }
        }

        private void RaiseDifficulty(EBossDifficulty difficulty)
        {
            Action<EBossDifficulty> handler = DifficultyChanged;

            if (handler != null)
            {
                handler(difficulty);
            }
        }

        private static void AddListener(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null)
            {
                button.onClick.AddListener(action);
            }
        }

        private static void RemoveListener(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null)
            {
                button.onClick.RemoveListener(action);
            }
        }

        private static void SetInteractable(Button button, bool interactable)
        {
            if (button != null)
            {
                button.interactable = interactable;
            }
        }
    }
}
