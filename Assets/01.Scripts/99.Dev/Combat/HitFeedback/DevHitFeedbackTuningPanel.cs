// Unity
using UnityEngine;

namespace Shinshi.Dev
{
    /// <summary> 치명타 블러와 줌 펀치, 임팩트 프레임, 몬스터 피격 떨림, 다중 타격 흔들림, 히트스톱 상한, 남의 공격 등급, 카메라 구도, 치명타 확률, 무한 소환을 실행 중에 조절하는 시험용 패널 </summary>
    public sealed class DevHitFeedbackTuningPanel : MonoBehaviour
    {
        // 값은 디렉터와 스포너의 메모리 값만 바꾸고 프로필 에셋은 쓰지 않는다. 화면 오른쪽의 빈 자리에 놓고 머리띠를 끌어 옮길 수 있다.
        // 패널 위에서 누른 마우스가 공격과 이동으로 새지 않게 포인터가 패널 안에 있는지를 정적으로 알린다

        /****************************************
        *                Fields
        ****************************************/

        private const int   WINDOW_ID        = 71042;
        private const float REFERENCE_HEIGHT = 1080f;
        private const float MIN_SCALE        = 0.6f;
        private const float PANEL_WIDTH      = 300f;
        private const float PANEL_HEIGHT     = 550f; // 넘치는 항목은 스크롤한다
        private const float COLLAPSED_HEIGHT = 28f;
        private const float EDGE_MARGIN      = 24f;
        private const float START_Y          = 450f;
        private const float HEADER_BUTTON_WIDTH  = 48f;
        private const float HEADER_BUTTON_HEIGHT = 20f;
        private const float HEADER_BUTTON_TOP    = 3f;
        private const float HEADER_BUTTON_MARGIN = 4f;
        private const float SECTION_GAP      = 6f;
        private const float TOP_GAP          = 4f;
        private const float PREVIEW_BUTTON_HEIGHT = 26f;
        private const float RESET_BUTTON_HEIGHT   = 22f;
        private const float LABEL_WIDTH      = 80f;
        private const float VALUE_WIDTH      = 44f;
        private const float FULL_ROTATION    = 360f;
        private const float HALF_ROTATION    = 180f;

        // 슬라이더 범위
        private const float BLUR_SECONDS_MIN = 0.05f;
        private const float BLUR_SECONDS_MAX = 2f;
        private const float ZOOM_PUNCH_MAX   = 0.3f;
        private const float ZOOM_SECONDS_MIN = 0.05f;
        private const float ZOOM_SECONDS_MAX = 1.5f;
        private const float PITCH_MIN        = 20f;
        private const float PITCH_MAX        = 90f;
        private const float DISTANCE_MIN     = 8f;
        private const float DISTANCE_MAX     = 90f;
        private const float FOV_MIN          = 10f;
        private const float FOV_MAX          = 70f;
        private const float ORTHO_SIZE_MIN   = 2f;
        private const float ORTHO_SIZE_MAX   = 30f;
        private const float VIBRATE_MAX      = 3f; // 떨림 배율
        private const float SHAKE_BONUS_MAX  = 1f;
        private const float SHAKE_SCALE_MIN  = 1f;
        private const float SHAKE_SCALE_MAX  = 4f;
        private const float LAG_BUDGET_MIN   = 1f;  // 프레임
        private const float LAG_BUDGET_MAX   = 40f;
        private const float LAG_REFILL_MAX   = 120f; // 프레임/초
        private const float IMPACT_FRAMES_MIN   = 1f;
        private const float IMPACT_FRAMES_MAX   = 20f;
        private const float IMPACT_KILLS_MIN    = 2f;
        private const float IMPACT_KILLS_MAX    = 10f;
        private const float IMPACT_COOLDOWN_MAX = 2f; // 초
        private const float IMPACT_HOT_MAX      = 2f; // 밝기, 2면 강조색이 거의 나오지 않는다

        private const string FORMAT_ANGLE    = "0";
        private const string FORMAT_ONE      = "0.0";
        private const string FORMAT_TWO      = "0.00";
        private const string FORMAT_THREE    = "0.000";

        [SerializeField] private HitFeedbackDirector   _director;
        [SerializeField] private DevHitFeedbackSpawner _spawner;
        [SerializeField] private DevHitFeedbackPlayer  _player;

        private Rect _rect       = new Rect(0f, 0f, PANEL_WIDTH, PANEL_HEIGHT);
        private bool _isPlaced   = false;
        private bool _isFolded   = false;
        private float _scale     = 1f;
        private Vector2 _scroll  = Vector2.zero;

        // 카메라 구도: 처음 값을 기억해 두었다가 되돌린다
        private bool  _hasCamera     = false;
        private float _pitch         = 0f;
        private float _yaw           = 0f;
        private float _distance      = 0f;
        private float _zoom          = 0f;  // 시야각(원근) 또는 정사영 크기
        private float _startPitch    = 0f;
        private float _startYaw      = 0f;
        private float _startDistance = 0f;
        private float _startZoom     = 0f;

        /// <summary> 마우스 포인터가 패널 위에 있으면 참 </summary>
        public static bool IsPointerOver { get; private set; }

        /****************************************
        *              Unity Event
        ****************************************/

        private void Update()
        {
            _scale = Mathf.Max(MIN_SCALE, Screen.height / REFERENCE_HEIGHT);

            if (!_isPlaced)
            {
                _rect     = new Rect((Screen.width / _scale) - PANEL_WIDTH - EDGE_MARGIN, START_Y, PANEL_WIDTH, PANEL_HEIGHT);
                _isPlaced = true;
            }

            ReadCamera();

            // IMGUI는 위에서 아래로 좌표가 커지고 입력은 아래에서 위로 커진다
            Vector2 mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y) / _scale;
            Rect    area  = new Rect(_rect.x, _rect.y, _rect.width, _isFolded ? COLLAPSED_HEIGHT : _rect.height);
            IsPointerOver = area.Contains(mouse);
        }

        private void OnDisable()
        {
            IsPointerOver = false;
        }

        private void OnGUI()
        {
            if ((_director == null) || (_spawner == null) || (_player == null))
            {
                return;
            }

            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(_scale, _scale, 1f));

            _rect.height = _isFolded ? COLLAPSED_HEIGHT : PANEL_HEIGHT;
            _rect        = GUI.Window(WINDOW_ID, _rect, DrawWindow, "타격감 조절");

            // 끌다가 화면 밖으로 나가지 않게 붙잡아 둔다
            float maxX = Mathf.Max(0f, (Screen.width / _scale) - _rect.width);
            float maxY = Mathf.Max(0f, (Screen.height / _scale) - _rect.height);
            _rect.x    = Mathf.Clamp(_rect.x, 0f, maxX);
            _rect.y    = Mathf.Clamp(_rect.y, 0f, maxY);

            GUI.matrix = previous;
        }

        /****************************************
        *                Methods
        ****************************************/

        private void DrawWindow(int id)
        {
            if (GUI.Button(new Rect(_rect.width - HEADER_BUTTON_WIDTH - HEADER_BUTTON_MARGIN, HEADER_BUTTON_TOP, HEADER_BUTTON_WIDTH, HEADER_BUTTON_HEIGHT), _isFolded ? "펼치기" : "접기"))
            {
                _isFolded = !_isFolded;
            }

            if (!_isFolded)
            {
                GUILayout.Space(TOP_GAP);
                _scroll = GUILayout.BeginScrollView(_scroll);

                GUILayout.Label("치명타 블러");
                _director.CritBlurIntensity = DrawSlider("세기", _director.CritBlurIntensity, 0f, 1f, FORMAT_TWO);
                _director.CritBlurSeconds   = DrawSlider("지속(초)", _director.CritBlurSeconds, BLUR_SECONDS_MIN, BLUR_SECONDS_MAX, FORMAT_TWO);

                GUILayout.Space(SECTION_GAP);
                GUILayout.Label("치명타 줌 펀치");
                _director.CritZoomPunch   = DrawSlider("당김 비율", _director.CritZoomPunch, 0f, ZOOM_PUNCH_MAX, FORMAT_THREE);
                _director.CritZoomSeconds = DrawSlider("지속(초)", _director.CritZoomSeconds, ZOOM_SECONDS_MIN, ZOOM_SECONDS_MAX, FORMAT_TWO);

                GUILayout.Space(SECTION_GAP);
                if (GUILayout.Button("치명타 연출 미리보기", GUILayout.Height(PREVIEW_BUTTON_HEIGHT)))
                {
                    _director.PreviewCritical();
                }

                GUILayout.Space(SECTION_GAP);
                GUILayout.Label("임팩트 프레임 (치명타, 한 번에 여러 마리 처치)");
                _director.UseImpactFrame   = GUILayout.Toggle(_director.UseImpactFrame, " 켜기");
                _director.ImpactSoft       = GUILayout.Toggle(_director.ImpactSoft, " 부드럽게(회색 정지, 서서히 풀림)");
                _director.ImpactFrames     = Mathf.RoundToInt(DrawSlider("프레임 수", _director.ImpactFrames,
                    IMPACT_FRAMES_MIN, IMPACT_FRAMES_MAX, FORMAT_ANGLE));
                _director.ImpactHotThreshold = DrawSlider("색 남김 기준", _director.ImpactHotThreshold, 0f, IMPACT_HOT_MAX, FORMAT_TWO);
                if (_director.ImpactSoft)
                {
                    _director.ImpactDim = DrawSlider("배경 밝기", _director.ImpactDim, 0f, 1f, FORMAT_TWO);
                }
                else
                {
                    _director.ImpactInvert    = GUILayout.Toggle(_director.ImpactInvert, " 배경 반전(밝은 바닥을 검게)");
                    _director.ImpactThreshold = DrawSlider("배경 기준", _director.ImpactThreshold, 0f, 1f, FORMAT_TWO);
                }
                _director.ImpactMultiKills = Mathf.RoundToInt(DrawSlider("다중 처치 수", _director.ImpactMultiKills,
                    IMPACT_KILLS_MIN, IMPACT_KILLS_MAX, FORMAT_ANGLE));
                _director.ImpactCooldown   = DrawSlider("재사용(초)", _director.ImpactCooldown, 0f, IMPACT_COOLDOWN_MAX, FORMAT_TWO);

                GUILayout.Space(SECTION_GAP);
                GUILayout.Label("몬스터 피격 떨림 (렉프레임 동안, 맞은 방향)");
                _director.VibrateScale = DrawSlider("배율", _director.VibrateScale, 0f, VIBRATE_MAX, FORMAT_TWO);

                GUILayout.Space(SECTION_GAP);
                GUILayout.Label("다중 타격 흔들림 (같은 프레임)");
                _director.MultiHitShakeBonus = DrawSlider("마리당 추가", _director.MultiHitShakeBonus, 0f, SHAKE_BONUS_MAX, FORMAT_TWO);
                _director.MultiHitShakeMax   = DrawSlider("최대 배율", _director.MultiHitShakeMax, SHAKE_SCALE_MIN, SHAKE_SCALE_MAX, FORMAT_TWO);

                GUILayout.Space(SECTION_GAP);
                GUILayout.Label("내 캐릭터 히트스톱 상한");
                _director.HitLagBudgetFrames    = Mathf.RoundToInt(DrawSlider("예산(프레임)", _director.HitLagBudgetFrames,
                    LAG_BUDGET_MIN, LAG_BUDGET_MAX, FORMAT_ANGLE));
                _director.HitLagRefillPerSecond = DrawSlider("회복(초당)", _director.HitLagRefillPerSecond, 0f, LAG_REFILL_MAX, FORMAT_ANGLE);

                GUILayout.Space(SECTION_GAP);
                GUILayout.Label("남의 공격 (파티원 타격)");
                _spawner.PlayHitsAsRemote  = GUILayout.Toggle(_spawner.PlayHitsAsRemote, " 내 타격을 남의 공격으로 재생");
                _director.RemoteEffectScale = DrawSlider("떨림, 불똥", _director.RemoteEffectScale, 0f, 1f, FORMAT_TWO);

                DrawCameraSection();

                GUILayout.Space(SECTION_GAP);
                _spawner.DevCritChance = DrawSlider("치명타 확률", _spawner.DevCritChance, 0f, 1f, FORMAT_TWO);
                _spawner.InfiniteSpawn = GUILayout.Toggle(_spawner.InfiniteSpawn, " 몬스터 무한 소환");
                GUILayout.EndScrollView();
            }

            GUI.DragWindow(new Rect(0f, 0f, _rect.width - HEADER_BUTTON_WIDTH - (HEADER_BUTTON_MARGIN * 2f), COLLAPSED_HEIGHT));
        }

        // 카메라가 준비되면 처음 구도를 읽어 둔다
        private void ReadCamera()
        {
            if ((_hasCamera) || (_player == null) || (_player.OutputCamera == null))
            {
                return;
            }

            Camera cam = _player.OutputCamera;
            _pitch    = NormalizeAngle(cam.transform.eulerAngles.x);
            _yaw      = NormalizeAngle(cam.transform.eulerAngles.y);
            _distance = _player.CameraDistance;
            _zoom     = cam.orthographic ? cam.orthographicSize : cam.fieldOfView;

            _startPitch    = _pitch;
            _startYaw      = _yaw;
            _startDistance = _distance;
            _startZoom     = _zoom;
            _hasCamera     = true;
        }

        private void DrawCameraSection()
        {
            if (!_hasCamera)
            {
                return;
            }

            bool isOrtho = _player.OutputCamera.orthographic;

            GUILayout.Space(SECTION_GAP);
            GUILayout.Label("카메라 구도");
            float pitch    = DrawSlider("각도", _pitch, PITCH_MIN, PITCH_MAX, FORMAT_ANGLE);
            float yaw      = DrawSlider("회전", _yaw, -HALF_ROTATION, HALF_ROTATION, FORMAT_ANGLE);
            float distance = DrawSlider("거리", _distance, DISTANCE_MIN, DISTANCE_MAX, FORMAT_ONE);
            float zoom     = isOrtho ? DrawSlider("화면 크기", _zoom, ORTHO_SIZE_MIN, ORTHO_SIZE_MAX, FORMAT_ONE)
                : DrawSlider("시야각", _zoom, FOV_MIN, FOV_MAX, FORMAT_ONE);

            if (GUILayout.Button("구도 되돌리기", GUILayout.Height(RESET_BUTTON_HEIGHT)))
            {
                pitch    = _startPitch;
                yaw      = _startYaw;
                distance = _startDistance;
                zoom     = _startZoom;
            }

            if ((!Mathf.Approximately(pitch, _pitch)) || (!Mathf.Approximately(yaw, _yaw)) || (!Mathf.Approximately(distance, _distance)))
            {
                _player.SetCameraRig(pitch, yaw, distance);
            }

            if (!Mathf.Approximately(zoom, _zoom))
            {
                _player.FeedbackCamera.SetBaseZoom(zoom);
            }

            _pitch    = pitch;
            _yaw      = yaw;
            _distance = distance;
            _zoom     = zoom;
        }

        // 0에서 360으로 읽히는 각도를 -180에서 180으로 바꾼다
        private static float NormalizeAngle(float angle)
        {
            return (angle > HALF_ROTATION) ? (angle - FULL_ROTATION) : angle;
        }

        private static float DrawSlider(string label, float value, float min, float max, string format)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(LABEL_WIDTH));
            value = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.Label(value.ToString(format), GUILayout.Width(VALUE_WIDTH));
            GUILayout.EndHorizontal();
            return value;
        }
    }
}
