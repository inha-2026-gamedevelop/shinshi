// System
using System;
using System.Collections.Generic;
using System.Text;

// Unity
using UnityEngine;
using UnityEngine.UI;

// 서드파티
using Mirror;
using TMPro;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Data;

namespace Shinshi.UI
{
    /// <summary> 왼쪽 위 FPS와 핑, 플레이어 머리 위 이름을 그린다, 씬과 프리팹에 두지 않고 클라이언트 시작 때 스스로 만든다 </summary>
    public sealed class NetworkOverlayView : MonoBehaviour
    {
        // 씬, 프리팹을 고치지 않고 로비부터 마을까지 같은 표시를 쓰려고 DontDestroyOnLoad로 하나만 둔다.
        // 글꼴은 OS 한글 글꼴로 만든 이 표시 전용 TMP 글꼴을 끝까지 쓴다. 예전처럼 씬의 HUD 동적 글꼴로 바꾸면
        // 그 아틀라스가 씬 글자로 차서 다음 아틀라스로 넘어간 글리프를 첫 아틀라스 재질로 그려 빌드에서 글자가 깨졌다.
        // 이름표는 매 프레임 움직이므로 통계 글자와 다른 하위 캔버스에 두어 다시 그리는 범위를 나눈다

        /****************************************
        *                Fields
        ****************************************/

        // 통계는 항상 맨 위, 이름표는 HUD와 창(씬 UI 0~50, HUD 10, 설정 100)보다 아래에 그린다
        public const int SORTING_ORDER           = 30000;
        public const int NAMEPLATE_SORTING_ORDER = -10;

        private const float  REFRESH_INTERVAL_SECONDS = 0.5f;
        private const float  NAMEPLATE_HEIGHT         = 2.1f;
        private const float  NAMEPLATE_MAX_DISTANCE   = 40f;
        private const float  NAMEPLATE_MAX_DIST_SQR   = NAMEPLATE_MAX_DISTANCE * NAMEPLATE_MAX_DISTANCE;
        private const float  STATS_FONT_SIZE          = 20f;
        private const float  NAMEPLATE_FONT_SIZE      = 22f;
        private const float  STACK_FONT_SIZE          = 18f;
        private const float  STACK_OFFSET_Y           = 24f;
        private const float  TEXT_OUTLINE_WIDTH       = 0.2f;
        private const float  REFERENCE_WIDTH         = 1920f;
        private const float  REFERENCE_HEIGHT         = 1080f;
        private const float  WIDTH_HEIGHT_MATCH       = 0.5f;
        private const double PING_GOOD_MS             = 80.0;
        private const double PING_WARN_MS             = 150.0;
        private const double MILLISECONDS_PER_SECOND  = 1000.0;
        private const KeyCode PREDICTION_TOGGLE_KEY   = KeyCode.F3;

        private static readonly string[] KOREAN_FONT_FAMILIES = { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Noto Sans CJK KR" };

        private static readonly Vector2 STATS_OFFSET    = new Vector2(12f, -8f);
        private static readonly Vector2 STATS_SIZE      = new Vector2(640f, 60f);
        private static readonly Vector2 STATS_PADDING   = new Vector2(8f, 4f);
        private static readonly Color   STATS_BACK      = new Color(0f, 0f, 0f, 0.55f);
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
        private RectTransform   _nameplateRoot;
        private TextMeshProUGUI _statsText;
        private RectTransform   _statsBack;
        private float           _nextRefreshTime = 0f;
        private int             _frameCount      = 0;
        private float           _frameTime       = 0f;
        private float           _maxFrameTime    = 0f;

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

            _font = CreateSystemKoreanFont();
            BuildCanvas();
        }

        private void Update()
        {
            if (Input.GetKeyDown(PREDICTION_TOGGLE_KEY))
            {
                _showsPrediction = !_showsPrediction;
                _nextRefreshTime = 0f;
            }

            float deltaTime = Time.unscaledDeltaTime;
            ++_frameCount;
            _frameTime += deltaTime;
            if (deltaTime > _maxFrameTime)
            {
                _maxFrameTime = deltaTime;
            }

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

            var nameplateObject = new GameObject("Nameplates", typeof(RectTransform));
            _nameplateRoot = (RectTransform)nameplateObject.transform;
            _nameplateRoot.SetParent(transform, false);
            _nameplateRoot.anchorMin = Vector2.zero;
            _nameplateRoot.anchorMax = Vector2.one;
            _nameplateRoot.offsetMin = Vector2.zero;
            _nameplateRoot.offsetMax = Vector2.zero;
            ConfigureNameplateCanvas(nameplateObject.AddComponent<Canvas>());

            // 밝은 하늘, UI 위에서도 읽히게 글자 크기에 맞춘 반투명 배경을 깐다
            var backObject = new GameObject("StatsBack", typeof(RectTransform));
            _statsBack = (RectTransform)backObject.transform;
            _statsBack.SetParent(transform, false);
            _statsBack.anchorMin        = new Vector2(0f, 1f);
            _statsBack.anchorMax        = new Vector2(0f, 1f);
            _statsBack.pivot            = new Vector2(0f, 1f);
            _statsBack.anchoredPosition = STATS_OFFSET + new Vector2(-STATS_PADDING.x, STATS_PADDING.y);
            var back = backObject.AddComponent<Image>();
            back.color         = STATS_BACK;
            back.raycastTarget = false;

            _statsText = CreateText("Stats", transform, STATS_FONT_SIZE, TextAlignmentOptions.TopLeft);
            RectTransform rect = _statsText.rectTransform;
            rect.anchorMin        = new Vector2(0f, 1f);
            rect.anchorMax        = new Vector2(0f, 1f);
            rect.pivot            = new Vector2(0f, 1f);
            rect.anchoredPosition = STATS_OFFSET;
            rect.sizeDelta        = STATS_SIZE;
        }

        // 부모 캔버스의 정렬값을 상속하면 이름표가 모든 창 위에 그려지므로 독립 정렬로 낮춘다
        public static void ConfigureNameplateCanvas(Canvas canvas)
        {
            canvas.overrideSorting = true;
            canvas.sortingOrder    = NAMEPLATE_SORTING_ORDER;
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
            float fps          = (_frameTime > 0f) ? (_frameCount / _frameTime) : 0f;
            int   maxFrameMs   = Mathf.RoundToInt(_maxFrameTime * (float)MILLISECONDS_PER_SECOND);
            _frameCount   = 0;
            _frameTime    = 0f;
            _maxFrameTime = 0f;

            // 평균 FPS로는 순간 끊김이 묻히므로 갱신 구간에서 가장 길었던 프레임도 함께 보인다
            _builder.Clear();
            _builder.Append("FPS ").Append(Mathf.RoundToInt(fps)).Append("  최대 ").Append(maxFrameMs).Append("ms");

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

            Vector2 textSize = _statsText.GetPreferredValues();
            _statsBack.sizeDelta = textSize + (STATS_PADDING * 2f);
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

            DrawStacks(nameplate, visible, screen);
        }

        // 머리 위에는 체력바가 없어 이름 바로 위에 마검사, 궁수 스택 줄을 하나 더 그린다. 글자는 값이 바뀔 때만 다시 만든다
        private void DrawStacks(Nameplate nameplate, bool nameVisible, Vector3 screen)
        {
            PlayerCombatState state   = nameplate.State;
            int               max     = GameDB.IsInitialized ? GameDB.Skills.ClassStackMax : 0;
            ClassStackDisplay display = ClassStackHudRules.Build(state.PlayerClass, state.ClassStacks, max, state.ArcherElement);
            bool              visible = (nameVisible) && (display.IsVisible);
            if (nameplate.StackText == null)
            {
                if (!visible)
                {
                    return;
                }

                nameplate.StackText = CreateText("NameplateStacks", _nameplateRoot, STACK_FONT_SIZE, TextAlignmentOptions.Center);
                nameplate.StackText.rectTransform.sizeDelta = NAMEPLATE_SIZE;
            }

            if (!display.SameAs(in nameplate.ShownStacks))
            {
                nameplate.ShownStacks     = display;
                nameplate.StackText.color = ClassStackHudRules.GetColor(in display);
                nameplate.StackText.text  = ClassStackHudRules.FormatNameplate(in display);
            }

            if (nameplate.StackText.enabled != visible)
            {
                nameplate.StackText.enabled = visible;
            }

            // 캔버스 배율을 곱해야 해상도가 달라도 이름과의 간격이 같다
            if (visible)
            {
                float offset = STACK_OFFSET_Y * _nameplateRoot.lossyScale.y;
                nameplate.StackText.rectTransform.position = new Vector3(screen.x, screen.y + offset, 0f);
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
                uint      netId   = _removeIds[i];
                Nameplate removed = _nameplates[netId];
                Destroy(removed.Text.gameObject);
                if (removed.StackText != null)
                {
                    Destroy(removed.StackText.gameObject);
                }
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
            public TextMeshProUGUI            StackText;
            public ClassStackDisplay          ShownStacks;

            public Nameplate(PlayerCombatState state, TextMeshProUGUI text)
            {
                State       = state;
                Text        = text;
                ShownName   = null;
                StackText   = null;
                ShownStacks = ClassStackDisplay.Hidden;
            }
        }
    }
}
