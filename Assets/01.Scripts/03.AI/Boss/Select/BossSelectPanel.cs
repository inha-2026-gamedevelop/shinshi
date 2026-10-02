// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.UI;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.AI
{
    /// <summary> 보스와 난이도를 고르고 닫는 선택 창. 고른 값을 기록만 하고 보스방 입장은 하지 않는다 </summary>
    public sealed class BossSelectPanel : MonoBehaviour
    {
        // 목록은 카탈로그의 보스 정의를 읽어 만든다. 표시 이름은 아직 현지화 키를 풀 곳이 없어 이 컴포넌트의 표를 쓴다
        // 한글 폰트가 프로젝트에 없어 임시로 OS 폰트를 쓴다. 정식 폰트가 들어오면 이 폴백을 지운다

        /****************************************
        *                Types
        ****************************************/

        [Serializable]
        private struct BossLabel
        {
            public string BossId;
            public string DisplayName;
        }

        private sealed class BossRow
        {
            public string BossId;
            public Button Button;
            public Image  Background;
        }

        /****************************************
        *                Fields
        ****************************************/

        private const KeyCode KEY_CLOSE = KeyCode.Escape;

        private const string SUMMARY_NONE_FORMAT     = "보스를 선택해 주세요";
        private const string SUMMARY_BOSS_FORMAT     = "{0} - 난이도를 선택해 주세요";
        private const string SUMMARY_SELECTED_FORMAT = "선택: {0}, {1}";

        private const string LABEL_EASY = "이지";
        private const string LABEL_HARD = "하드";

        private const string ROW_NAME_PREFIX = "BossRow_";

        private static readonly string[] G_OsFontNames = { "Malgun Gothic", "Apple SD Gothic Neo", "NanumGothic" };
        private const int OS_FONT_SIZE = 24;

        [Header("창")]
        [SerializeField] private GameObject _window = null;
        [SerializeField] private GameObject _prompt = null;
        [SerializeField] private Text       _summaryText = null;
        [SerializeField] private Button     _closeButton = null;

        [Header("보스 목록")]
        [SerializeField] private RectTransform _bossListRoot = null;
        [SerializeField] private Button        _bossButtonTemplate = null;
        [SerializeField] private BossLabel[]   _bossLabels = null;

        // EBossDifficulty 값 순서(Easy, Hard)대로 둔다
        [Header("난이도")]
        [SerializeField] private Button[] _difficultyButtons = null;

        [Header("선택 표시")]
        [SerializeField] private Color _normalColor   = Color.white;
        [SerializeField] private Color _selectedColor = new Color(1f, 0.85f, 0.4f, 1f);

        private readonly List<BossRow> _rows = new List<BossRow>();

        private string          _selectedBossId = null;
        private EBossDifficulty _difficulty     = EBossDifficulty.Easy;
        private bool            _hasBoss        = false;
        private bool            _hasDifficulty  = false;
        private bool            _isBuilt        = false;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsOpen => (_window != null) && (_window.activeSelf);

        /// <summary> 보스와 난이도를 모두 골랐는지 </summary>
        public bool HasSelection => (_hasBoss) && (_hasDifficulty);

        public string          SelectedBossId     => _selectedBossId;
        public EBossDifficulty SelectedDifficulty => _difficulty;

        /// <summary> 보스와 난이도 선택이 확정될 때 알린다. 실제 입장 연결은 이 알림을 받는 쪽이 맡는다 </summary>
        public event Action<string, EBossDifficulty> SelectionChanged;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if ((_window == null) || (_bossListRoot == null) || (_bossButtonTemplate == null) || (_difficultyButtons == null))
            {
                Debug.LogError("[BossSelectPanel] 창 참조가 비어 있다", this);
                enabled = false;
                return;
            }

            _bossButtonTemplate.gameObject.SetActive(false);
            _window.SetActive(false);
            SetPromptVisible(false);

            for (int i = 0; i < _difficultyButtons.Length; ++i)
            {
                int index = i;
                _difficultyButtons[i].onClick.AddListener(() => SelectDifficulty((EBossDifficulty)index));
            }

            if (_closeButton != null)
            {
                _closeButton.onClick.AddListener(Close);
            }

            ApplyOsFont();
        }

        private void Update()
        {
            if ((IsOpen) && (Input.GetKeyDown(KEY_CLOSE)))
            {
                Close();
            }
        }

        private void OnDestroy()
        {
            if (_closeButton != null)
            {
                _closeButton.onClick.RemoveListener(Close);
            }

            for (int i = 0; i < _difficultyButtons.Length; ++i)
            {
                _difficultyButtons[i].onClick.RemoveAllListeners();
            }

            for (int i = 0; i < _rows.Count; ++i)
            {
                _rows[i].Button.onClick.RemoveAllListeners();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        public void Open()
        {
            if (!enabled)
            {
                return;
            }

            EnsureBuilt();
            RefreshView();
            _window.SetActive(true);
            SetPromptVisible(false);
        }

        public void Close()
        {
            if (!enabled)
            {
                return;
            }

            _window.SetActive(false);
        }

        /// <summary> NPC 가까이에 있을 때 "F" 안내를 켜고 끈다 </summary>
        public void SetPromptVisible(bool visible)
        {
            if (_prompt != null)
            {
                _prompt.SetActive(visible);
            }
        }

        private void SelectBoss(string bossId)
        {
            if (_selectedBossId != bossId)
            {
                _hasDifficulty = false;
            }

            _selectedBossId = bossId;
            _hasBoss        = true;
            RefreshView();
        }

        private void SelectDifficulty(EBossDifficulty difficulty)
        {
            if (!_hasBoss)
            {
                return;
            }

            _difficulty    = difficulty;
            _hasDifficulty = true;
            RefreshView();
            SelectionChanged?.Invoke(_selectedBossId, _difficulty);
        }

        private void EnsureBuilt()
        {
            if (_isBuilt)
            {
                return;
            }

            if (!GameDB.IsInitialized)
            {
                Debug.LogError("[BossSelectPanel] GameDB가 초기화되지 않아 보스 목록을 만들 수 없다", this);
                return;
            }

            IReadOnlyList<BossPatternCatalogSO.BossDefinition> bosses = GameDB.BossPatterns.Bosses;
            for (int i = 0; i < bosses.Count; ++i)
            {
                AddRow(bosses[i].DefinitionId);
            }

            ApplyOsFont();
            _isBuilt = true;
        }

        private void AddRow(string bossId)
        {
            Button button = Instantiate(_bossButtonTemplate, _bossListRoot);
            button.name = ROW_NAME_PREFIX + bossId;
            button.gameObject.SetActive(true);

            Text label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.text = FindDisplayName(bossId);
            }

            button.onClick.AddListener(() => SelectBoss(bossId));

            _rows.Add(new BossRow
            {
                BossId     = bossId,
                Button     = button,
                Background = button.GetComponent<Image>()
            });
        }

        private string FindDisplayName(string bossId)
        {
            if (_bossLabels != null)
            {
                for (int i = 0; i < _bossLabels.Length; ++i)
                {
                    if (_bossLabels[i].BossId == bossId)
                    {
                        return _bossLabels[i].DisplayName;
                    }
                }
            }

            return bossId;
        }

        private void RefreshView()
        {
            for (int i = 0; i < _rows.Count; ++i)
            {
                bool isSelected = (_hasBoss) && (_rows[i].BossId == _selectedBossId);
                if (_rows[i].Background != null)
                {
                    _rows[i].Background.color = isSelected ? _selectedColor : _normalColor;
                }
            }

            for (int i = 0; i < _difficultyButtons.Length; ++i)
            {
                bool isSelected = (_hasDifficulty) && ((int)_difficulty == i);
                _difficultyButtons[i].interactable = _hasBoss;

                if (_difficultyButtons[i].TryGetComponent(out Image background))
                {
                    background.color = isSelected ? _selectedColor : _normalColor;
                }
            }

            if (_summaryText == null)
            {
                return;
            }

            if (!_hasBoss)
            {
                _summaryText.text = SUMMARY_NONE_FORMAT;
            }
            else if (!_hasDifficulty)
            {
                _summaryText.text = string.Format(SUMMARY_BOSS_FORMAT, FindDisplayName(_selectedBossId));
            }
            else
            {
                _summaryText.text = string.Format(SUMMARY_SELECTED_FORMAT, FindDisplayName(_selectedBossId), DifficultyLabel(_difficulty));
            }
        }

        private static string DifficultyLabel(EBossDifficulty difficulty)
        {
            return (difficulty == EBossDifficulty.Hard) ? LABEL_HARD : LABEL_EASY;
        }

        private void ApplyOsFont()
        {
            Font font = Font.CreateDynamicFontFromOSFont(G_OsFontNames, OS_FONT_SIZE);
            Text[] texts = GetComponentsInChildren<Text>(true);
            for (int i = 0; i < texts.Length; ++i)
            {
                texts[i].font = font;
            }
        }
    }
}
