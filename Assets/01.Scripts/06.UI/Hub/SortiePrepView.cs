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
    /// 출정 준비 화면. 고른 던전의 그림과 이름, 난이도, 파티 준비 상태만 보여 주고
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

            // 행 배경은 늘 두고, 사람이 있을 때 켜는 것과 빈 자리 표시를 서로 반대로 켠다
            [SerializeField] private GameObject[] _filledParts = null;
            [SerializeField] private GameObject   _emptyRoot   = null;
            [SerializeField] private TMP_Text     _nameLabel   = null;
            [SerializeField] private TMP_Text     _powerLabel  = null;
            [SerializeField] private Image        _classIcon   = null;
            [SerializeField] private GameObject   _leaderIcon  = null;
            [SerializeField] private GameObject   _readyFx     = null;
            [SerializeField] private GameObject   _problemTint = null;
            [SerializeField] private TMP_Text     _stateLabel  = null;

            public GameObject[] FilledParts => _filledParts;
            public GameObject   EmptyRoot   => _emptyRoot;
            public TMP_Text     NameLabel   => _nameLabel;
            public TMP_Text     PowerLabel  => _powerLabel;
            public Image        ClassIcon   => _classIcon;
            public GameObject   LeaderIcon  => _leaderIcon;
            public GameObject   ReadyFx     => _readyFx;
            public GameObject   ProblemTint => _problemTint;
            public TMP_Text     StateLabel  => _stateLabel;
        }

        /****************************************
        *                Fields
        ****************************************/

        private const string READY_COUNT_FORMAT = "준비 {0}/{1}";
        private const string COUNTDOWN_FORMAT   = "{0}초 뒤 출정";
        private const string START_TEXT         = "출정";
        private const string READY_TEXT         = "준비";
        private const string UNREADY_TEXT       = "준비 해제";
        private const string READY_STATE        = "준비 완료";
        private const string WAIT_STATE         = "대기 중";
        private const string OFFLINE_STATE      = "접속 끊김";
        private const string LACK_STATE         = "조건 부족";

        // 윗줄은 "직업 Lv.레벨 닉네임"으로 쓰고 직업과 레벨은 닉네임보다 옅게 칠한다
        private const string TITLE_FORMAT  = "<color=#99A3B2>{0}</color>  <color=#D6C496>Lv.{1}</color>  {2}";
        private const string POWER_FORMAT  = "전투력 {0:N0}";
        private const string UNKNOWN_LEVEL = "-";
        private const string UNKNOWN_POWER = "전투력 -";

        // EPlayerClass 순서와 같아야 한다
        private static readonly string[] G_ClassIconIds = { "class_warrior", "class_sorcerer", "class_summoner" };
        private static readonly string[] G_ClassNames   = { "무사", "도술사", "신령사", "검사", "마검사", "궁수" };

        // EBossDifficulty 순서와 같아야 한다
        private static readonly string[] G_DifficultyNames = { "쉬움", "어려움" };

        [Header("던전")]
        [SerializeField] private Image      _dungeonArt          = null;
        [SerializeField] private GameObject _artPlaceholder      = null;
        [SerializeField] private TMP_Text   _dungeonNameLabel    = null;
        [SerializeField] private TMP_Text   _dungeonSummaryLabel = null;

        [Header("난이도 (점과 색은 EBossDifficulty 순서)")]
        [SerializeField] private Button     _prevDifficultyButton = null;
        [SerializeField] private Button     _nextDifficultyButton = null;
        [SerializeField] private TMP_Text   _difficultyLabel      = null;
        [SerializeField] private Image[]    _difficultyDots       = null;
        [SerializeField] private Color[]    _difficultyColors     = null;
        [SerializeField] private Color      _dotOffColor          = new Color(1f, 1f, 1f, 0.3f);
        [SerializeField] private GameObject _hardFx               = null;

        [Header("파티")]
        [SerializeField] private MemberRow[]     _members         = null;
        [SerializeField] private TMP_Text        _readyCountLabel = null;
        [SerializeField] private UiIconLibrarySO _library         = null;

        [Header("파티 상태 글자 색")]
        // 준비 완료 행은 오른쪽이 진한 초록이라 글자는 그 위에서 읽히게 밝게 둔다
        [SerializeField] private Color _readyColor = new Color(0.72f, 0.96f, 0.75f);
        [SerializeField] private Color _waitColor  = new Color(0.55f, 0.58f, 0.63f);
        [SerializeField] private Color _warnColor  = new Color(0.78f, 0.36f, 0.32f);

        [Header("하단")]
        [SerializeField] private Button   _readyButton = null;
        [SerializeField] private TMP_Text _readyLabel  = null;
        [SerializeField] private Button   _startButton = null;
        [SerializeField] private TMP_Text _startLabel  = null;
        [SerializeField] private Button   _leaveButton = null;
        [SerializeField] private Button   _closeButton = null;
        [SerializeField] private TMP_Text _noticeLabel = null;

        private EBossDifficulty _difficulty = EBossDifficulty.Easy;

        private TMP_InputField _inviteInput  = null;
        private Button         _inviteButton = null;

        /****************************************
        *              Properties
        ****************************************/

        public event Action<EBossDifficulty> DifficultyChanged;
        public event Action                  ReadyClicked;
        public event Action                  StartClicked;
        public event Action                  LeaveClicked;
        public event Action<string>          InviteClicked;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            EnsureModernLayout();
        }

        private void OnEnable()
        {
            AddListener(_prevDifficultyButton, OnPrevDifficulty);
            AddListener(_nextDifficultyButton, OnNextDifficulty);
            AddListener(_readyButton, OnReady);
            AddListener(_startButton, OnStart);
            AddListener(_leaveButton, OnLeave);
            AddListener(_closeButton, OnLeave);
        }

        private void OnDisable()
        {
            RemoveListener(_prevDifficultyButton, OnPrevDifficulty);
            RemoveListener(_nextDifficultyButton, OnNextDifficulty);
            RemoveListener(_readyButton, OnReady);
            RemoveListener(_startButton, OnStart);
            RemoveListener(_leaveButton, OnLeave);
            RemoveListener(_closeButton, OnLeave);
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>어두운 공통 배치를 한 번만 적용한다. 여러 번 불러도 같다</summary>
        public void EnsureModernLayout()
        {
            SortiePrepModernLayout.Apply(this);
        }

        /// <summary>그림이 없으면 자리표시를 켠다</summary>
        public void SetDungeon(string dungeonName, string summary, Sprite art)
        {
            if (_dungeonNameLabel != null)
            {
                _dungeonNameLabel.text = dungeonName;
            }

            if (_dungeonSummaryLabel != null)
            {
                _dungeonSummaryLabel.text = summary;
            }

            if (art == null)
            {
                ModernDarkUiCatalog catalog = ModernDarkUiCatalog.Load();
                art = (catalog != null) ? catalog.Background : null;
            }

            bool hasArt = (art != null);

            if (_dungeonArt != null)
            {
                _dungeonArt.sprite        = art;
                _dungeonArt.enabled       = hasArt;
                _dungeonArt.raycastTarget = false;
            }

            SetActive(_artPlaceholder, !hasArt);
        }

        /// <summary>이름과 점, 이름 색을 맞추고 어려움이면 안개 연출을 켠다</summary>
        public void SetDifficulty(EBossDifficulty difficulty)
        {
            _difficulty = difficulty;
            int index = (int)difficulty;

            if (_difficultyLabel != null)
            {
                bool known = (index >= 0) && (index < G_DifficultyNames.Length);
                _difficultyLabel.text = known ? G_DifficultyNames[index] : string.Empty;

                if ((_difficultyColors != null) && (index >= 0) && (index < _difficultyColors.Length))
                {
                    _difficultyLabel.color = _difficultyColors[index];
                }
            }

            if (_difficultyDots != null)
            {
                for (int i = 0; i < _difficultyDots.Length; ++i)
                {
                    if (_difficultyDots[i] == null)
                    {
                        continue;
                    }

                    bool on = (i == index);
                    bool hasColor = (_difficultyColors != null) && (i < _difficultyColors.Length);
                    _difficultyDots[i].color = on ? (hasColor ? _difficultyColors[i] : Color.white) : _dotOffColor;
                }
            }

            SetActive(_hardFx, difficulty == EBossDifficulty.Hard);
        }

        /// <summary>난이도가 없는 방(카오스 던전)에서는 난이도 이름, 점, 화살표를 숨긴다</summary>
        public void SetDifficultyVisible(bool visible)
        {
            SetActive(_prevDifficultyButton != null ? _prevDifficultyButton.gameObject : null, visible);
            SetActive(_nextDifficultyButton != null ? _nextDifficultyButton.gameObject : null, visible);
            SetActive(_difficultyLabel != null ? _difficultyLabel.gameObject : null, visible);

            if (_difficultyDots != null)
            {
                for (int i = 0; i < _difficultyDots.Length; ++i)
                {
                    SetActive(_difficultyDots[i] != null ? _difficultyDots[i].gameObject : null, visible);
                }
            }

            if (!visible)
            {
                SetActive(_hardFx, false);
            }
        }

        /// <summary>준비 인원은 방장을 뺀 파티원 기준으로 센다</summary>
        public void SetMembers(IReadOnlyList<SortieMemberData> members)
        {
            if (_members == null)
            {
                return;
            }

            int readyCount = 0;
            int guestCount = 0;

            for (int i = 0; i < _members.Length; ++i)
            {
                if (_members[i] == null)
                {
                    continue;
                }

                bool has = (members != null) && (i < members.Count) && !members[i].IsEmpty;

                SetActive(_members[i].FilledParts, has);
                SetActive(_members[i].EmptyRoot, !has);

                if (!has)
                {
                    if (_members[i].ClassIcon != null)
                    {
                        _members[i].ClassIcon.sprite = null;
                        _members[i].ClassIcon.enabled = false;
                    }

                    SetActive(_members[i].LeaderIcon, false);
                    SetActive(_members[i].ReadyFx, false);
                    SetActive(_members[i].ProblemTint, false);
                    continue;
                }

                ApplyMember(_members[i], members[i]);

                if (members[i].IsLeader)
                {
                    continue;
                }

                ++guestCount;

                if (members[i].IsReady)
                {
                    ++readyCount;
                }
            }

            if (_readyCountLabel != null)
            {
                _readyCountLabel.text = string.Format(READY_COUNT_FORMAT, readyCount, guestCount);
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

        /// <summary>방이 없는 동안 준비와 나가기를 막는다. 방을 만드는 중이면 난이도 단추도 막아 중복 요청을 막는다</summary>
        public void SetRoomControls(bool inRoom, bool pending)
        {
            bool difficultyEnabled = inRoom || !pending;
            SetInteractable(_readyButton, inRoom);
            SetInteractable(_leaveButton, inRoom);
            SetInteractable(_prevDifficultyButton, difficultyEnabled);
            SetInteractable(_nextDifficultyButton, difficultyEnabled);
        }

        /// <summary>저장된 안내 글자가 없는 원본이면 배치가 만든 글자를 대신 쓴다. 이미 있으면 바꾸지 않는다</summary>
        public void AdoptNoticeLabel(TMP_Text label)
        {
            if ((_noticeLabel == null) && (label != null))
            {
                _noticeLabel = label;
            }
        }

        /// <summary>배치가 실행 중에 만든 초대 입력칸과 단추를 연결한다. 한 번만 연결한다</summary>
        public void AdoptInviteRow(TMP_InputField input, Button button)
        {
            if ((_inviteInput != null) || (input == null) || (button == null))
            {
                return;
            }

            _inviteInput  = input;
            _inviteButton = button;
            _inviteButton.onClick.AddListener(OnInvite);
            _inviteInput.onSubmit.AddListener(OnInviteSubmit);
        }

        /// <summary>방장이 아니거나 방이 없으면 숨긴다. 응답을 기다리는 동안은 누를 수 없게 막는다</summary>
        public void SetInviteState(bool visible, bool interactable)
        {
            if (_inviteInput == null)
            {
                return;
            }

            _inviteInput.gameObject.SetActive(visible);
            _inviteButton.gameObject.SetActive(visible);
            _inviteInput.interactable  = interactable;
            _inviteButton.interactable = interactable;
        }

        public void ClearInviteInput()
        {
            if (_inviteInput != null)
            {
                _inviteInput.text = string.Empty;
            }
        }

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

        // 방장은 글자 대신 왕관으로 알린다. 행 칠은 준비 완료(일렁이는 초록)와 문제 상태(빨강)에만 쓴다
        private void ApplyMember(MemberRow row, SortieMemberData data)
        {
            int  classIndex = (int)data.PlayerClass;
            bool knownClass = (classIndex >= 0) && (classIndex < G_ClassNames.Length);

            if (row.NameLabel != null)
            {
                string className = knownClass ? G_ClassNames[classIndex] : string.Empty;
                string level     = (data.Level > 0) ? data.Level.ToString() : UNKNOWN_LEVEL;
                row.NameLabel.text = string.Format(TITLE_FORMAT, className, level, data.Name);
            }

            if (row.PowerLabel != null)
            {
                row.PowerLabel.text = (data.CombatPower > 0) ? string.Format(POWER_FORMAT, data.CombatPower) : UNKNOWN_POWER;
            }

            if (row.ClassIcon != null)
            {
                // 이름과 아이콘의 지원 범위는 다르다. 신규 직업 그림이 없어도 이름을 보이며 이전 그림은 지운다
                Sprite icon = ((_library != null) && (classIndex >= 0) && (classIndex < G_ClassIconIds.Length))
                    ? _library.FindIcon(G_ClassIconIds[classIndex]) : null;
                row.ClassIcon.sprite  = icon;
                row.ClassIcon.enabled = (icon != null);
            }

            bool problem = !data.IsConnected || !data.MeetsRequirements;
            bool leader  = data.IsLeader && !problem;
            bool ready   = !data.IsLeader && data.IsReady && !problem;

            SetActive(row.LeaderIcon, leader);
            SetActive(row.ReadyFx, ready);
            SetActive(row.ProblemTint, problem);

            if (row.StateLabel != null)
            {
                row.StateLabel.text  = leader ? string.Empty : ResolveState(data);
                row.StateLabel.color = ResolveColor(data);
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

            return data.IsReady ? READY_STATE : WAIT_STATE;
        }

        private Color ResolveColor(SortieMemberData data)
        {
            if (!data.IsConnected || !data.MeetsRequirements)
            {
                return _warnColor;
            }

            return data.IsReady ? _readyColor : _waitColor;
        }

        private void OnPrevDifficulty() { StepDifficulty(-1); }
        private void OnNextDifficulty() { StepDifficulty(1); }

        // 끝에서 넘기면 반대쪽 끝으로 돈다
        private void StepDifficulty(int step)
        {
            int count = G_DifficultyNames.Length;
            int next  = ((((int)_difficulty + step) % count) + count) % count;
            RaiseDifficulty((EBossDifficulty)next);
        }

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

        private void OnInvite()
        {
            InviteClicked?.Invoke(_inviteInput.text);
        }

        private void OnInviteSubmit(string text)
        {
            InviteClicked?.Invoke(text);
        }

        private void OnLeave()
        {
            Action handler = LeaveClicked;

            if (handler != null)
            {
                handler();
            }
        }

        // 이미 고른 것을 다시 고르면 알리지 않는다. 알리면 받는 쪽이 선택 변경으로 보고 준비를 풀어 버린다
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

        private static void SetActive(GameObject[] targets, bool active)
        {
            if (targets == null)
            {
                return;
            }

            for (int i = 0; i < targets.Length; ++i)
            {
                SetActive(targets[i], active);
            }
        }
    }
}
