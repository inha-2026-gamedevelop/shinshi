// System
using System;
using System.Collections.Generic;
using System.Text;

// Unity
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 서드파티
using Mirror;
using TMPro;

// 프로젝트 내부
using Shinshi.Combat;

namespace Shinshi.UI
{
    /// <summary> 왼쪽 위 FPS와 핑, 플레이어 머리 위 이름을 그린다, 씬과 프리팹에 두지 않고 클라이언트 시작 때 스스로 만든다 </summary>
    public sealed class NetworkOverlayView : MonoBehaviour
    {
        // 씬, 프리팹을 고치지 않고 로비부터 마을까지 같은 표시를 쓰려고 DontDestroyOnLoad로 하나만 둔다.
        // 글꼴은 씬이 불러 둔 HUD 한글 글꼴을 우선 쓰고, 아직 없으면 OS 한글 글꼴로 만든 TMP 글꼴을 쓴다.
        // 이름표는 매 프레임 움직이므로 통계 글자와 다른 하위 캔버스에 두어 다시 그리는 범위를 나눈다

        /****************************************
        *                Fields
        ****************************************/

        private const float  REFRESH_INTERVAL_SECONDS = 0.5f;
        private const float  NAMEPLATE_HEIGHT         = 2.1f;
        private const float  NAMEPLATE_MAX_DISTANCE   = 40f;
        private const float  NAMEPLATE_MAX_DIST_SQR   = NAMEPLATE_MAX_DISTANCE * NAMEPLATE_MAX_DISTANCE;
        private const float  STATS_FONT_SIZE          = 20f;
        private const float  NAMEPLATE_FONT_SIZE      = 22f;
        private const float  TEXT_OUTLINE_WIDTH       = 0.2f;
        private const int    SORTING_ORDER            = 30000;
        private const float  REFERENCE_WIDTH          = 1920f;
        private const float  REFERENCE_HEIGHT         = 1080f;
        private const float  WIDTH_HEIGHT_MATCH       = 0.5f;
        private const double PING_GOOD_MS             = 80.0;
        private const double PING_WARN_MS             = 150.0;
        private const double MILLISECONDS_PER_SECOND  = 1000.0;
        private const string PROJECT_FONT_NAME        = "HudQuestKorean-Regular";
        private const KeyCode PREDICTION_TOGGLE_KEY   = KeyCode.F3;

        private static readonly string[] KOREAN_FONT_FAMILIES = { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Noto Sans CJK KR" };

        private static readonly Vector2 STATS_OFFSET    = new Vector2(12f, -8f);
        private static readonly Vector2 STATS_SIZE      = new Vector2(640f, 60f);
        private static readonly Vector2 NAMEPLATE_SIZE  = new Vector2(300f, 40f);
        private static readonly Color   PING_GOOD_COLOR = new Color(0.55f, 1f, 0.55f);
        private static readonly Color   PING_WARN_COLOR = new Color(1f, 0.85f, 0.35f);
        private static readonly Color   PING_BAD_COLOR  = new Color(1f, 0.45f, 0.45f);

        private readonly Dictionary<uint, Nameplate> _nameplates  = new Dictionary<uint, Nameplate>();
        private readonly HashSet<uint>               _nonPlayers  = new HashSet<uint>();
        private readonly List<uint>                  _removeIds   = new List<uint>();
        private readonly StringBuilder               _builder     = new StringBuilder(128);
        private readonly Predicate<uint>             _isDespawned = IsDespawned;

        private TMP_FontAsset   _font;
        private bool            _hasProjectFont  = false;
        private RectTransform   _nameplateRoot;
        private TextMeshProUGUI _statsText;
        private float           _nextRefreshTime = 0f;
        private int             _frameCount      = 0;
        private float           _frameTime       = 0f;

        // 예측 진단값은 멀티 테스트용이다. 개발 빌드에서는 처음부터 보이고 배포 빌드에서는 F3으로 켠다
        private bool            _showsPrediction = false;

        /****************************************
        *              Unity Event
        ****************************************/

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateOnStart()
        {
#if !UNITY_SERVER
            if (Application.isBatchMode)
            {
                return;
            }

            var root = new GameObject(nameof(NetworkOverlayView));
            DontDestroyOnLoad(root);
            root.AddComponent<NetworkOverlayView>();
#endif
        }

        private void Awake()
        {
            // Unity API는 필드 초기화(생성자)에서 부를 수 없다
            _showsPrediction = Debug.isDebugBuild;

            _font = FindProjectFont();
            _hasProjectFont = _font != null;
            if (!_hasProjectFont)
            {
                _font = CreateSystemKoreanFont();
            }

            BuildCanvas();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void Update()
        {
            if (Input.GetKeyDown(PREDICTION_TOGGLE_KEY))
            {
                _showsPrediction = !_showsPrediction;
                _nextRefreshTime = 0f;
            }

            ++_frameCount;
            _frameTime += Time.unscaledDeltaTime;
            if (Time.unscaledTime >= _nextRefreshTime)
            {
                _nextRefreshTime = Time.unscaledTime + REFRESH_INTERVAL_SECONDS;
                RefreshStats();
            }

            UpdateNameplates();
        }

        /****************************************
        *                Methods
        ****************************************/

        // HUD가 있는 씬이 열리면 그 씬이 불러 둔 프로젝트 글꼴로 바꾼다. 한 번 바꾼 뒤에는 다시 찾지 않는다
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (_hasProjectFont)
            {
                return;
            }

            TMP_FontAsset projectFont = FindProjectFont();
            if (projectFont == null)
            {
                return;
            }

            _font           = projectFont;
            _hasProjectFont = true;
            _statsText.font = projectFont;
            foreach (KeyValuePair<uint, Nameplate> pair in _nameplates)
            {
                pair.Value.Text.font = projectFont;
            }
        }

        private static TMP_FontAsset FindProjectFont()
        {
            TMP_FontAsset[] loaded = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            for (int i = 0; i < loaded.Length; ++i)
            {
                if (string.Equals(loaded[i].name, PROJECT_FONT_NAME, StringComparison.Ordinal))
                {
                    return loaded[i];
                }
            }

            return null;
        }

        private static TMP_FontAsset CreateSystemKoreanFont()
        {
            for (int i = 0; i < KOREAN_FONT_FAMILIES.Length; ++i)
            {
                TMP_FontAsset font = TMP_FontAsset.CreateFontAsset(KOREAN_FONT_FAMILIES[i], "Regular");
                if (font != null)
                {
                    return font;
                }
            }

            return TMP_Settings.defaultFontAsset;
        }

        private void BuildCanvas()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SORTING_ORDER;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(REFERENCE_WIDTH, REFERENCE_HEIGHT);
            scaler.matchWidthOrHeight  = WIDTH_HEIGHT_MATCH;

            // 이름표를 먼저 만들어 통계 글자가 그 위에 그려지게 한다
            var nameplateObject = new GameObject("Nameplates", typeof(RectTransform));
            _nameplateRoot = (RectTransform)nameplateObject.transform;
            _nameplateRoot.SetParent(transform, false);
            _nameplateRoot.anchorMin = Vector2.zero;
            _nameplateRoot.anchorMax = Vector2.one;
            _nameplateRoot.offsetMin = Vector2.zero;
            _nameplateRoot.offsetMax = Vector2.zero;
            nameplateObject.AddComponent<Canvas>();

            _statsText = CreateText("Stats", transform, STATS_FONT_SIZE, TextAlignmentOptions.TopLeft);
            RectTransform rect = _statsText.rectTransform;
            rect.anchorMin        = new Vector2(0f, 1f);
            rect.anchorMax        = new Vector2(0f, 1f);
            rect.pivot            = new Vector2(0f, 1f);
            rect.anchoredPosition = STATS_OFFSET;
            rect.sizeDelta        = STATS_SIZE;
        }

        private TextMeshProUGUI CreateText(string objectName, Transform parent, float fontSize, TextAlignmentOptions alignment)
        {
            var textObject = new GameObject(objectName, typeof(RectTransform));
            textObject.transform.SetParent(parent, false);

            var text = textObject.AddComponent<TextMeshProUGUI>();
            if (_font != null)
            {
                text.font = _font;
            }

            text.fontSize         = fontSize;
            text.alignment        = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget    = false;
            text.outlineWidth     = TEXT_OUTLINE_WIDTH;
            text.outlineColor     = Color.black;
            return text;
        }

        // 0.5초마다 한 번만 문자열을 만든다. 매 프레임 만들면 할당이 쌓인다
        private void RefreshStats()
        {
            float fps = (_frameTime > 0f) ? (_frameCount / _frameTime) : 0f;
            _frameCount = 0;
            _frameTime  = 0f;

            _builder.Clear();
            _builder.Append("FPS ").Append(Mathf.RoundToInt(fps));

            Color color = Color.white;
            if ((NetworkClient.active) && (NetworkClient.isConnected) && (!NetworkServer.active))
            {
                double pingMs   = NetworkTime.rtt * MILLISECONDS_PER_SECOND;
                double jitterMs = Math.Sqrt(Math.Max(0.0, NetworkTime.rttVariance)) * MILLISECONDS_PER_SECOND;
                _builder.Append("   Ping ").Append((int)pingMs).Append("ms (±").Append((int)jitterMs).Append(')');
                color = (pingMs < PING_GOOD_MS) ? PING_GOOD_COLOR : ((pingMs < PING_WARN_MS) ? PING_WARN_COLOR : PING_BAD_COLOR);
                if (_showsPrediction)
                {
                    AppendPredictionStats();
                }
            }

            _statsText.color = color;
            _statsText.SetText(_builder);
        }

        // 보정 거리가 이동 거리에 비해 크거나 스냅이 늘면 예측이 서버와 어긋나고 있다는 뜻이다. 멀티 테스트 진단용이다
        private void AppendPredictionStats()
        {
            NetworkIdentity local = NetworkClient.localPlayer;
            if ((local == null) || (!local.TryGetComponent(out PlayerPrediction prediction)))
            {
                return;
            }

            _builder.Append("\n이동 ").Append(Mathf.RoundToInt(prediction.TravelledDistance))
                .Append("m  보정 ").Append(prediction.CorrectedDistance.ToString("F1"))
                .Append("m  스냅 ").Append(prediction.SnapCount);
        }

        private void UpdateNameplates()
        {
            // 연결이 없을 때만 지운다. 씬 전환 중 카메라가 잠깐 없을 때는 숨기기만 한다
            if (!NetworkClient.active)
            {
                RemoveStaleNameplates(true);
                return;
            }

            Camera view = Camera.main;
            foreach (KeyValuePair<uint, NetworkIdentity> pair in NetworkClient.spawned)
            {
                DrawNameplate(pair.Key, pair.Value, view);
            }

            RemoveStaleNameplates(false);
        }

        private void DrawNameplate(uint netId, NetworkIdentity identity, Camera view)
        {
            if ((identity == null) || (_nonPlayers.Contains(netId)))
            {
                return;
            }

            if (!_nameplates.TryGetValue(netId, out Nameplate nameplate))
            {
                // 플레이어인지는 처음 한 번만 본다
                if (!identity.TryGetComponent(out PlayerCombatState state))
                {
                    _nonPlayers.Add(netId);
                    return;
                }

                nameplate = new Nameplate(state, CreateText("Nameplate", _nameplateRoot, NAMEPLATE_FONT_SIZE,
                    TextAlignmentOptions.Center));
                nameplate.Text.rectTransform.sizeDelta = NAMEPLATE_SIZE;
                _nameplates.Add(netId, nameplate);
            }

            string displayName = nameplate.State.DisplayName;
            if (!ReferenceEquals(displayName, nameplate.ShownName))
            {
                nameplate.ShownName = displayName;
                nameplate.Text.text = displayName;
            }

            bool    visible = (view != null) && (!string.IsNullOrEmpty(displayName));
            Vector3 screen  = Vector3.zero;
            if (visible)
            {
                Vector3 head = nameplate.State.transform.position + (Vector3.up * NAMEPLATE_HEIGHT);
                screen  = view.WorldToScreenPoint(head);
                visible = (screen.z > 0f) && ((head - view.transform.position).sqrMagnitude <= NAMEPLATE_MAX_DIST_SQR);
            }

            if (nameplate.Text.enabled != visible)
            {
                nameplate.Text.enabled = visible;
            }

            // 오버레이 캔버스는 화면 픽셀 좌표가 곧 월드 좌표다
            if (visible)
            {
                nameplate.Text.rectTransform.position = new Vector3(screen.x, screen.y, 0f);
            }
        }

        private void RemoveStaleNameplates(bool removeAll)
        {
            _removeIds.Clear();
            foreach (KeyValuePair<uint, Nameplate> pair in _nameplates)
            {
                if ((removeAll) || (pair.Value.State == null) || (!NetworkClient.spawned.ContainsKey(pair.Key)))
                {
                    _removeIds.Add(pair.Key);
                }
            }

            for (int i = 0; i < _removeIds.Count; ++i)
            {
                uint netId = _removeIds[i];
                Destroy(_nameplates[netId].Text.gameObject);
                _nameplates.Remove(netId);
            }

            // 끊기면 netId가 다시 쓰이므로 비플레이어 기록도 비운다
            if (removeAll)
            {
                _nonPlayers.Clear();
            }
            else if (_nonPlayers.Count > (NetworkClient.spawned.Count * 2))
            {
                // 적처럼 생겼다 사라지는 개체의 기록이 던전 동안 쌓이지 않게 가끔 정리한다
                _nonPlayers.RemoveWhere(_isDespawned);
            }
        }

        private static bool IsDespawned(uint netId)
        {
            return !NetworkClient.spawned.ContainsKey(netId);
        }

        /****************************************
        *               Records
        ****************************************/

        private sealed class Nameplate
        {
            public readonly PlayerCombatState State;
            public readonly TextMeshProUGUI   Text;
            public string                     ShownName;

            public Nameplate(PlayerCombatState state, TextMeshProUGUI text)
            {
                State     = state;
                Text      = text;
                ShownName = null;
            }
        }
    }
}
