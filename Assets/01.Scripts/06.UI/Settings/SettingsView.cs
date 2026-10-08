// System
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 서드파티
using TMPro;

namespace Shinshi.UI
{
    /// <summary>그래픽과 오디오 값을 임시 편집하고 적용하거나 취소하는 설정 창</summary>
    public sealed class SettingsView : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string VSYNC_KEY     = "Graphics.VSync";
        public const string FRAME_CAP_KEY = "Graphics.FrameCap";

        private const int   NO_FRAME_CAP = -1;
        private const int   CATEGORY_GRAPHICS = 0;
        private const int   CATEGORY_AUDIO    = 1;
        private const int   CATEGORY_GAME     = 2;
        private const int   CATEGORY_COUNT    = 3;
        private const float PERCENT      = 100f;

        private static readonly int[] G_FrameCaps = { 30, 60, 120, 144, NO_FRAME_CAP };
        private static readonly FullScreenMode[] G_ScreenModes =
        {
            FullScreenMode.ExclusiveFullScreen,
            FullScreenMode.FullScreenWindow,
            FullScreenMode.Windowed,
        };

        [Header("분류")]
        [SerializeField] private GameObject _graphicsBody = null;
        [SerializeField] private GameObject _audioBody    = null;
        [SerializeField] private GameObject _gameBody     = null;
        [SerializeField] private Button[]   _graphicsNavigation = null;
        [SerializeField] private Button[]   _audioNavigation    = null;
        [SerializeField] private Button[]   _gameNavigation     = null;
        [SerializeField] private Image     _graphicsHighlight   = null;
        [SerializeField] private Image     _audioHighlight      = null;
        [SerializeField] private Image     _gameHighlight       = null;
        [SerializeField] private GameObject _graphicsIndicator  = null;
        [SerializeField] private GameObject _audioIndicator     = null;
        [SerializeField] private GameObject _gameIndicator      = null;
        [SerializeField] private TMP_Text[] _graphicsLabels     = null;
        [SerializeField] private TMP_Text[] _audioLabels        = null;
        [SerializeField] private TMP_Text[] _gameLabels         = null;
        [SerializeField] private TMP_Text _categoryTitle = null;
        [SerializeField] private TMP_Text _categoryDetail = null;

        [Header("그래픽")]
        [SerializeField] private TMP_Dropdown _screenMode = null;
        [SerializeField] private TMP_Dropdown _resolution = null;
        [SerializeField] private TMP_Dropdown _frameCap   = null;
        [SerializeField] private Toggle       _vsync      = null;
        [SerializeField] private TMP_Text     _vsyncState = null;
        [SerializeField] private CanvasGroup  _frameCapGroup = null;
        [SerializeField] private GameObject   _vsyncNotice   = null;

        [Header("오디오")]
        [SerializeField] private Slider   _master      = null;
        [SerializeField] private Slider   _bgm         = null;
        [SerializeField] private Slider   _sfx         = null;
        [SerializeField] private Toggle   _mute        = null;
        [SerializeField] private TMP_Text _masterValue = null;
        [SerializeField] private TMP_Text _bgmValue    = null;
        [SerializeField] private TMP_Text _sfxValue    = null;
        [SerializeField] private TMP_Text _muteState   = null;

        [Header("게임")]
        [SerializeField] private TMP_Dropdown _cursorSize = null;

        [Header("설명과 버튼")]
        [SerializeField] private TMP_Text _hintTitle = null;
        [SerializeField] private TMP_Text _hintBody  = null;
        [SerializeField] private TMP_Text _status    = null;
        [SerializeField] private Button _reset       = null;
        [SerializeField] private Button _resetAll    = null;
        [SerializeField] private Button _apply       = null;
        [SerializeField] private Button _confirm     = null;
        [SerializeField] private Button _cancel      = null;
        [SerializeField] private Button _close       = null;

        [Header("표현")]
        [SerializeField] private Color _selectedColor = new Color(0.2f, 0.178f, 0.135f, 1f);
        [SerializeField] private Color _idleColor     = new Color(0.08f, 0.09f, 0.1f, 1f);
        [SerializeField] private float _disabledAlpha = 0.4f;
        [SerializeField] private Color _activeTextColor = new Color(0.9f, 0.83f, 0.69f, 1f);
        [SerializeField] private Color _idleTextColor   = new Color(0.64f, 0.67f, 0.69f, 1f);

        private readonly List<Vector2Int> _resolutions = new List<Vector2Int>();
        private SoundVolume _savedAudio;
        private int  _savedMode       = 0;
        private int  _savedResolution = 0;
        private int  _savedFrameCap   = NO_FRAME_CAP;
        private int  _savedCursorSize = CursorScale.DEFAULT_INDEX;
        private bool _savedVsync      = true;
        private int  _category        = CATEGORY_GRAPHICS;
        private bool _ready           = false;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _screenMode.onValueChanged.AddListener(OnGraphicsChanged);
            _resolution.onValueChanged.AddListener(OnGraphicsChanged);
            _frameCap.onValueChanged.AddListener(OnGraphicsChanged);
            _vsync.onValueChanged.AddListener(OnVsyncChanged);
            _master.onValueChanged.AddListener(OnVolumeChanged);
            _bgm.onValueChanged.AddListener(OnVolumeChanged);
            _sfx.onValueChanged.AddListener(OnVolumeChanged);
            _mute.onValueChanged.AddListener(OnMuteChanged);
            _cursorSize.onValueChanged.AddListener(OnCursorSizeChanged);
            _reset.onClick.AddListener(ResetCategory);
            _resetAll.onClick.AddListener(ResetAll);
            _apply.onClick.AddListener(Apply);
            _confirm.onClick.AddListener(Confirm);
            _cancel.onClick.AddListener(Cancel);
            _close.onClick.AddListener(Cancel);
            foreach (Button button in _graphicsNavigation)
            {
                button.onClick.AddListener(ShowGraphics);
            }
            foreach (Button button in _audioNavigation)
            {
                button.onClick.AddListener(ShowAudio);
            }
            foreach (Button button in _gameNavigation)
            {
                button.onClick.AddListener(ShowGame);
            }
            _ready = true;
        }

        private void OnEnable()
        {
            if (!_ready)
            {
                return;
            }
            ReadCurrentValues();
            ShowGraphics();
        }

        private void OnDisable()
        {
            if (_ready)
            {
                _savedAudio.Preview();
                CursorScale.Preview(_savedCursorSize);
            }
        }

        // 모달이 열려 있을 때만 활성화된다. 닫힌 창은 입력을 폴링하지 않는다
        private void Update()
        {
            if ((_screenMode.IsExpanded) || (_resolution.IsExpanded) || (_frameCap.IsExpanded) || (_cursorSize.IsExpanded))
            {
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Cancel();
            }
            else if (Input.GetKeyDown(KeyCode.Q))
            {
                SelectCategory((_category + CATEGORY_COUNT - 1) % CATEGORY_COUNT);
            }
            else if (Input.GetKeyDown(KeyCode.E))
            {
                SelectCategory((_category + 1) % CATEGORY_COUNT);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ApplySavedPreferences()
        {
#if !UNITY_SERVER
            QualitySettings.vSyncCount = PlayerPrefs.GetInt(VSYNC_KEY, 1) != 0 ? 1 : 0;
            Application.targetFrameRate = ValidFrameCap(PlayerPrefs.GetInt(FRAME_CAP_KEY, NO_FRAME_CAP));
            SoundVolume.Load().Preview();
#endif
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary>새 편집 세션을 시작한다</summary>
        public void Show()
        {
            if (gameObject.activeSelf)
            {
                return;
            }
            gameObject.SetActive(true);
        }

        public void ShowGraphics() { SelectCategory(CATEGORY_GRAPHICS); }
        public void ShowAudio() { SelectCategory(CATEGORY_AUDIO); }
        public void ShowGame() { SelectCategory(CATEGORY_GAME); }

        /// <summary>마우스 또는 키보드로 선택한 항목의 설명을 표시한다</summary>
        public void ShowHint(string title, string description)
        {
            _hintTitle.text = title;
            _hintBody.text = description;
        }

        /// <summary>전체 편집값을 저장하고 창을 유지한다</summary>
        public void Apply()
        {
            if (!HasChanges())
            {
                return;
            }

            // 에디터 게임 뷰의 크기는 플레이어 화면 설정과 별개다. 실제 화면 변경은 플레이어에서만 한다
#if !UNITY_EDITOR
            Vector2Int size = _resolutions[_resolution.value];
            if ((_savedMode != _screenMode.value) || (_savedResolution != _resolution.value))
            {
                Screen.SetResolution(size.x, size.y, G_ScreenModes[_screenMode.value]);
            }
#endif
            QualitySettings.vSyncCount = _vsync.isOn ? 1 : 0;
            Application.targetFrameRate = G_FrameCaps[_frameCap.value];
            PlayerPrefs.SetInt(VSYNC_KEY, _vsync.isOn ? 1 : 0);
            PlayerPrefs.SetInt(FRAME_CAP_KEY, G_FrameCaps[_frameCap.value]);
            _savedAudio = CurrentAudio();
            _savedAudio.Save();
            _savedAudio.Preview();
            CursorScale.Save(_cursorSize.value);
            PlayerPrefs.Save();
            CaptureGraphicsBaseline();
            RefreshState();
            _status.text = "설정을 적용했습니다";
        }

        /// <summary>편집값을 적용하고 창을 닫는다</summary>
        public void Confirm()
        {
            Apply();
            gameObject.SetActive(false);
        }

        /// <summary>마지막 적용 이후의 편집과 오디오 미리보기를 취소한다</summary>
        public void Cancel() { gameObject.SetActive(false); }

        /// <summary>현재 분류를 기본값으로 편집한다. 저장은 적용 버튼에서 수행한다</summary>
        public void ResetCategory()
        {
            switch (_category)
            {
                case CATEGORY_AUDIO:
                    SetAudio(SoundVolume.Default);
                    OnVolumeChanged(0f);
                    break;
                case CATEGORY_GAME:
                    ResetGame();
                    break;
                default:
                    ResetGraphics();
                    break;
            }
        }

        /// <summary>그래픽, 오디오, 게임을 기본값으로 편집한다</summary>
        public void ResetAll()
        {
            ResetGraphics();
            ResetGame();
            SetAudio(SoundVolume.Default);
            OnVolumeChanged(0f);
        }

        private void ReadCurrentValues()
        {
            _resolutions.Clear();
            foreach (Resolution resolution in Screen.resolutions)
            {
                Vector2Int size = new Vector2Int(resolution.width, resolution.height);
                if (!_resolutions.Contains(size))
                {
                    _resolutions.Add(size);
                }
            }
            Vector2Int current = new Vector2Int(Screen.width, Screen.height);
            if (!_resolutions.Contains(current))
            {
                _resolutions.Add(current);
            }
            _resolutions.Sort((left, right) =>
            {
                int width = left.x.CompareTo(right.x);
                return (width != 0) ? width : left.y.CompareTo(right.y);
            });
            List<string> labels = new List<string>(_resolutions.Count);
            foreach (Vector2Int size in _resolutions)
            {
                labels.Add($"{size.x} x {size.y}");
            }
            _resolution.ClearOptions();
            _resolution.AddOptions(labels);
            _resolution.SetValueWithoutNotify(_resolutions.IndexOf(current));
            _resolution.interactable = _resolutions.Count > 1;
            int mode = System.Array.IndexOf(G_ScreenModes, Screen.fullScreenMode);
            _screenMode.SetValueWithoutNotify(Mathf.Max(0, mode));
            _vsync.SetIsOnWithoutNotify(PlayerPrefs.GetInt(VSYNC_KEY, 1) != 0);
            int frameCap = ValidFrameCap(PlayerPrefs.GetInt(FRAME_CAP_KEY, NO_FRAME_CAP));
            _frameCap.SetValueWithoutNotify(System.Array.IndexOf(G_FrameCaps, frameCap));
            _cursorSize.ClearOptions();
            List<string> cursorLabels = new List<string>(CursorScale.Count);
            for (int i = 0; i < CursorScale.Count; ++i)
            {
                cursorLabels.Add(CursorScale.Label(i));
            }
            _cursorSize.AddOptions(cursorLabels);
            _cursorSize.SetValueWithoutNotify(CursorScale.Load());
            _savedAudio = SoundVolume.Load();
            SetAudio(_savedAudio);
            _savedAudio.Preview();
            CaptureGraphicsBaseline();
            RefreshState();
        }

        private void CaptureGraphicsBaseline()
        {
            _savedMode       = _screenMode.value;
            _savedResolution = _resolution.value;
            _savedVsync      = _vsync.isOn;
            _savedFrameCap   = G_FrameCaps[_frameCap.value];
            _savedCursorSize = _cursorSize.value;
        }

        private void SelectCategory(int category)
        {
            _category = category;
            _graphicsBody.SetActive(category == CATEGORY_GRAPHICS);
            _audioBody.SetActive(category == CATEGORY_AUDIO);
            _gameBody.SetActive(category == CATEGORY_GAME);
            _graphicsHighlight.color = (category == CATEGORY_GRAPHICS) ? _selectedColor : _idleColor;
            _audioHighlight.color    = (category == CATEGORY_AUDIO)    ? _selectedColor : _idleColor;
            _gameHighlight.color     = (category == CATEGORY_GAME)     ? _selectedColor : _idleColor;
            _graphicsIndicator.SetActive(category == CATEGORY_GRAPHICS);
            _audioIndicator.SetActive(category == CATEGORY_AUDIO);
            _gameIndicator.SetActive(category == CATEGORY_GAME);
            PaintLabels(_graphicsLabels, category == CATEGORY_GRAPHICS);
            PaintLabels(_audioLabels, category == CATEGORY_AUDIO);
            PaintLabels(_gameLabels, category == CATEGORY_GAME);
            switch (category)
            {
                case CATEGORY_AUDIO:
                    _categoryTitle.text = "오디오";
                    _categoryDetail.text = "소리의 크기와 균형을 조절합니다";
                    break;
                case CATEGORY_GAME:
                    _categoryTitle.text = "게임";
                    _categoryDetail.text = "게임 플레이 중 보이는 요소를 조절합니다";
                    break;
                default:
                    _categoryTitle.text = "그래픽";
                    _categoryDetail.text = "내 화면에 맞는 플레이 환경을 선택합니다";
                    break;
            }
            ShowHint("설정 안내", "항목에 마우스를 올리면 자세한 설명을 확인할 수 있습니다.");
        }

        private void PaintLabels(TMP_Text[] labels, bool active)
        {
            foreach (TMP_Text label in labels)
            {
                label.color = active ? _activeTextColor : _idleTextColor;
            }
        }

        private void OnGraphicsChanged(int value) { RefreshState(); }
        private void OnVsyncChanged(bool value) { RefreshState(); }
        private void OnMuteChanged(bool value) { OnVolumeChanged(0f); }

        private void OnCursorSizeChanged(int value)
        {
            CursorScale.Preview(value);
            RefreshState();
        }

        private void OnVolumeChanged(float value)
        {
            CurrentAudio().Preview();
            RefreshState();
        }

        private SoundVolume CurrentAudio()
        {
            return new SoundVolume(_master.value, _bgm.value, _sfx.value, _mute.isOn);
        }

        private void SetAudio(SoundVolume volume)
        {
            _master.SetValueWithoutNotify(volume.Master);
            _bgm.SetValueWithoutNotify(volume.Bgm);
            _sfx.SetValueWithoutNotify(volume.Sfx);
            _mute.SetIsOnWithoutNotify(volume.Muted);
        }

        private void ResetGraphics()
        {
            _screenMode.SetValueWithoutNotify(_savedMode);
            _resolution.SetValueWithoutNotify(_savedResolution);
            _vsync.SetIsOnWithoutNotify(true);
            _frameCap.SetValueWithoutNotify(G_FrameCaps.Length - 1);
            RefreshState();
        }

        private void ResetGame()
        {
            _cursorSize.SetValueWithoutNotify(CursorScale.DEFAULT_INDEX);
            CursorScale.Preview(CursorScale.DEFAULT_INDEX);
            RefreshState();
        }

        private bool HasChanges()
        {
            return (_savedMode != _screenMode.value)
                || (_savedResolution != _resolution.value)
                || (_savedVsync != _vsync.isOn)
                || (_savedFrameCap != G_FrameCaps[_frameCap.value])
                || (_savedCursorSize != _cursorSize.value)
                || (!_savedAudio.Equals(CurrentAudio()));
        }

        private void RefreshState()
        {
            _frameCap.interactable = !_vsync.isOn;
            _frameCapGroup.alpha = _vsync.isOn ? _disabledAlpha : 1f;
            _vsyncNotice.SetActive(_vsync.isOn);
            _vsyncState.text = _vsync.isOn ? "켜기" : "끄기";
            _muteState.text = _mute.isOn ? "켜기" : "끄기";
            _masterValue.text = $"{Mathf.RoundToInt(_master.value * PERCENT)}%";
            _bgmValue.text = $"{Mathf.RoundToInt(_bgm.value * PERCENT)}%";
            _sfxValue.text = $"{Mathf.RoundToInt(_sfx.value * PERCENT)}%";
            bool dirty = HasChanges();
            _apply.interactable = dirty;
            _status.text = dirty ? "적용하지 않은 변경 사항이 있습니다" : "모든 설정이 저장되어 있습니다";
        }

        private static int ValidFrameCap(int value)
        {
            return System.Array.IndexOf(G_FrameCaps, value) >= 0 ? value : NO_FRAME_CAP;
        }
    }
}
