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
    /// 출정 준비 화면. 전투 종류와 난이도, 입장권, 장비와 부적 요약, 물약 지참량, 파티 준비 상태를 보여 주고
    /// 사용자의 선택은 이벤트로 알린다. 값의 판정과 저장은 서버가 하며 이 뷰는 받은 값을 그대로 그린다
    /// </summary>
    public sealed class SortiePrepView : MonoBehaviour
    {
        /****************************************
        *                Types
        ****************************************/

        [Serializable]
        private class MemberRow
        {
            /****************************************
            *                Fields
            ****************************************/

            [SerializeField] private GameObject _root       = null;
            [SerializeField] private TMP_Text   _nameLabel  = null;
            [SerializeField] private Image      _classIcon  = null;
            [SerializeField] private TMP_Text   _stateLabel = null;

            public GameObject Root       => _root;
            public TMP_Text   NameLabel  => _nameLabel;
            public Image      ClassIcon  => _classIcon;
            public TMP_Text   StateLabel => _stateLabel;
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

        // EPlayerClass 순서와 같아야 한다
        private static readonly string[] G_ClassIconIds = { "class_warrior", "class_sorcerer", "class_summoner" };

        [Header("전장")]
        [SerializeField] private Button     _waveModeButton   = null;
        [SerializeField] private Button     _bossModeButton   = null;
        [SerializeField] private GameObject _waveModeSelected = null;
        [SerializeField] private GameObject _bossModeSelected = null;
        [SerializeField] private TMP_Text   _regionLabel      = null;
        [SerializeField] private Button     _easyButton       = null;
        [SerializeField] private Button     _hardButton       = null;
        [SerializeField] private GameObject _easySelected     = null;
        [SerializeField] private GameObject _hardSelected     = null;
        [SerializeField] private GameObject _difficultyRoot   = null;
        [SerializeField] private TMP_Text   _ticketLabel      = null;

        [Header("요약")]
        [SerializeField] private ItemSlotGridView _equipmentGrid = null;
        [SerializeField] private ItemSlotGridView _talismanGrid  = null;

        [Header("물약 지참량 (소형, 대형 순서)")]
        [SerializeField] private Button[]   _potionMinusButtons = null;
        [SerializeField] private Button[]   _potionPlusButtons  = null;
        [SerializeField] private TMP_Text[] _potionLabels       = null;

        [Header("파티")]
        [SerializeField] private MemberRow[]     _members = null;
        [SerializeField] private UiIconLibrarySO _library = null;

        [Header("하단")]
        [SerializeField] private Button   _readyButton = null;
        [SerializeField] private TMP_Text _readyLabel  = null;
        [SerializeField] private Button   _startButton = null;
        [SerializeField] private TMP_Text _startLabel  = null;
        [SerializeField] private Button   _leaveButton = null;
        [SerializeField] private TMP_Text _noticeLabel = null;

        private ESortieMode     _mode       = ESortieMode.Wave;
        private EBossDifficulty _difficulty = EBossDifficulty.Easy;

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
            AddListener(PotionButton(_potionMinusButtons, EPotionSize.Small), OnSmallMinus);
            AddListener(PotionButton(_potionPlusButtons, EPotionSize.Small), OnSmallPlus);
            AddListener(PotionButton(_potionMinusButtons, EPotionSize.Large), OnLargeMinus);
            AddListener(PotionButton(_potionPlusButtons, EPotionSize.Large), OnLargePlus);
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
            RemoveListener(PotionButton(_potionMinusButtons, EPotionSize.Small), OnSmallMinus);
            RemoveListener(PotionButton(_potionPlusButtons, EPotionSize.Small), OnSmallPlus);
            RemoveListener(PotionButton(_potionMinusButtons, EPotionSize.Large), OnLargeMinus);
            RemoveListener(PotionButton(_potionPlusButtons, EPotionSize.Large), OnLargePlus);
        }

        /****************************************
        *                Methods
        ****************************************/

        // 고른 버튼을 비활성으로 바꾸면 "쓸 수 없음"과 구분되지 않고 색만으로 상태를 알리게 되므로
        // 버튼은 그대로 두고 선택 틀을 따로 켠다
        public void SetMode(ESortieMode mode)
        {
            _mode = mode;
            bool boss = (mode == ESortieMode.Boss);

            if (_difficultyRoot != null)
            {
                _difficultyRoot.SetActive(boss);
            }

            if (_ticketLabel != null)
            {
                _ticketLabel.gameObject.SetActive(boss);
            }

            SetActive(_waveModeSelected, !boss);
            SetActive(_bossModeSelected, boss);
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
            _difficulty = difficulty;
            SetActive(_easySelected, difficulty == EBossDifficulty.Easy);
            SetActive(_hardSelected, difficulty == EBossDifficulty.Hard);
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
                if (_members[i] == null)
                {
                    continue;
                }

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
                int classIndex = (int)data.PlayerClass;
                bool known = (classIndex >= 0) && (classIndex < G_ClassIconIds.Length);
                Sprite icon = known ? _library.FindIcon(G_ClassIconIds[classIndex]) : null;
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

        private static Button PotionButton(Button[] buttons, EPotionSize size)
        {
            int index = (int)size;

            if ((buttons == null) || (index >= buttons.Length))
            {
                return null;
            }

            return buttons[index];
        }

        private void OnSmallMinus() { ChangePotion((int)EPotionSize.Small, -1); }
        private void OnSmallPlus()  { ChangePotion((int)EPotionSize.Small, 1); }
        private void OnLargeMinus() { ChangePotion((int)EPotionSize.Large, -1); }
        private void OnLargePlus()  { ChangePotion((int)EPotionSize.Large, 1); }

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

        // 이미 고른 것을 다시 누르면 알리지 않는다. 알리면 받는 쪽이 선택 변경으로 보고 준비를 풀어 버린다
        private void RaiseMode(ESortieMode mode)
        {
            if (mode == _mode)
            {
                return;
            }

            Action<ESortieMode> handler = ModeChanged;

            if (handler != null)
            {
                handler(mode);
            }
        }

        private void RaiseDifficulty(EBossDifficulty difficulty)
        {
            if (difficulty == _difficulty)
            {
                return;
            }

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

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null)
            {
                target.SetActive(active);
            }
        }
    }
}
