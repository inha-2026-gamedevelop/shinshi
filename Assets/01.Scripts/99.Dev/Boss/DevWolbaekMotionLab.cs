#if UNITY_EDITOR || DEVELOPMENT_BUILD
// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Data;

namespace Shinshi.Dev
{
    // 정식 월백 프리팹(PF_WolbaekBoss)을 씬에 놓고 패턴 모션을 서버 없이 하나씩 재생해 보는 개발용 확인 씬이다
    // 서버 사건을 받은 표현이 부르는 같은 경로(예고 표현기, BossView.BeginEventMotion)로 모션을 재생하므로 치마 리그 같은 프리팹 변경이 그대로 보인다
    // 기믹 모션은 다루지 않는다. 서버 시계는 이 도구가 직접 올린다
    /// <summary> 월백 1, 2페이즈 패턴 모션을 골라 재생하는 확인 도구, 화면 버튼으로 조작한다 </summary>
    public sealed class DevWolbaekMotionLab : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private struct Scheduled
        {
            public double Time;
            public Action Call;
        }

        private const int    SAMPLE_BOSS_ID    = 1;
        private const int    FIRST_CAST_ID     = 1000;
        private const float  DEFAULT_TELEGRAPH = 2.5f;   // 힘 순간이 가장 늦은 모션(빔 2.3초)보다 길게 잡아 모든 모션이 처음부터 재생되게 한다
        private const float  FAN_RADIUS        = 6f;
        private const float  FAN_ARC           = 100f;
        private const float  CIRCLE_RADIUS     = 5f;
        private const float  DASH_LENGTH       = 8f;
        private const float  DASH_WIDTH        = 2f;
        private const float  DASH_TELEGRAPH    = 0.8f;
        private const float  DASH_SECONDS      = 0.45f;
        private const float  DASH_TAIL_SECONDS = 0.6f;
        private const float  WARP_TELEGRAPH    = 1.5f;
        private const float  WARP_TAIL_SECONDS = 3f;
        private const float  RECOVER_SECONDS   = 1.5f;   // 판정 뒤 모션이 끝나길 기다리는 시간
        private const float  REPEAT_GAP        = 1f;
        private const float  MIN_TIME_SCALE    = 0.05f;
        private const float  MAX_TIME_SCALE    = 1f;

        private static readonly Vector3 G_Center  = Vector3.zero;
        private static readonly Vector3 G_Landing = new Vector3(8f, 0f, 4f);

        // 카메라 위치는 보스 중심 기준이다, 옆과 뒤와 위와 쿼터뷰
        private static readonly Vector3[] G_CameraOffsets =
        {
            new Vector3(0f, 2.2f, -7f),
            new Vector3(0f, 2.2f, 7f),
            new Vector3(0f, 9f, -1.5f),
            new Vector3(7f, 7f, -7f),
        };

        private static readonly string[] G_CameraNames = { "앞", "뒤", "위", "쿼터" };

        // 화면 조작판
        private const float UI_SCALE   = 1.3f;
        private const float PANEL_X    = 8f;
        private const float PANEL_Y    = 8f;
        private const float PANEL_WIDTH = 300f;
        private const float COLUMN_GAP = 4f;

        [SerializeField] private BossView _boss;
        [SerializeField] private Camera   _camera;

        private readonly List<Scheduled> _schedule = new List<Scheduled>();

        private BossTelegraphPresenter _presenter;
        private double                 _clock;
        private int                    _castId = FIRST_CAST_ID;
        private int                    _cameraIndex;
        private float                  _timeScale = MAX_TIME_SCALE;
        private bool                   _isRepeating;
        private bool                   _isDefeated;
        private string                 _currentName;
        private Action                 _currentPlay;
        private Vector2                _scroll;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Start()
        {
            _presenter = new GameObject("MotionLabPresenter").AddComponent<BossTelegraphPresenter>();
            _presenter.SetServerClock(() => _clock);
            _boss.Bind(_presenter, () => _clock);
            _boss.SetPose(G_Center, Vector3.forward);
            ApplyCamera();
        }

        private void OnDisable()
        {
            Time.timeScale = 1f;
        }

        private void Update()
        {
            _clock += Time.deltaTime;

            for (int i = _schedule.Count - 1; i >= 0; --i)
            {
                if (_clock >= _schedule[i].Time)
                {
                    Action call = _schedule[i].Call;
                    _schedule.RemoveAt(i);
                    call();
                }
            }
        }

        private void OnGUI()
        {
            GUI.matrix = Matrix4x4.Scale(new Vector3(UI_SCALE, UI_SCALE, 1f));
            GUILayout.BeginArea(new Rect(PANEL_X, PANEL_Y, PANEL_WIDTH, (Screen.height / UI_SCALE) - (PANEL_Y * 2f)), GUI.skin.box);
            _scroll = GUILayout.BeginScrollView(_scroll);

            GUILayout.Label(_currentName == null ? "월백 모션 확인 (기믹 제외)" : $"재생: {_currentName}");

            DrawCommon();
            DrawSection("1페이즈 (서버 예고 경로)");
            DrawPattern("P01 돌진", PlayDash);
            DrawPattern("P02 부채꼴", () => PlayFan("wolbaek_p02"));
            DrawPattern("P03 원형", () => PlayCircle("wolbaek_p03"));
            DrawPattern("P04 던지기", () => PlayCircle("wolbaek_p04"));
            DrawPattern("P05 장판", () => PlayCircle("wolbaek_p05"));
            DrawPattern("P06 빔", () => PlayCircle("wolbaek_p06"));

            DrawSection("2페이즈 (서버 예고 경로)");
            DrawPattern("P04 장판", () => PlayCircle(BossPatternCatalogSO.WOLBAEK_P2_P04_ID));
            DrawPattern("P05 3연발 던지기", () => PlayCircle(BossPatternCatalogSO.WOLBAEK_P2_P05_ID));
            DrawPattern("P06 원형", () => PlayCircle(BossPatternCatalogSO.WOLBAEK_P2_P06_ID));
            DrawPattern("P07 워프", PlayWarp);

            DrawSection("2페이즈 (서버 사건 경로)");
            DrawEvent("별 회전", EWolbaekEventMotion.StarRotating);
            DrawEvent("별 단계", EWolbaekEventMotion.StarStage);
            DrawEvent("빔 쌍", EWolbaekEventMotion.BeamPair);
            DrawEvent("격자 빔", EWolbaekEventMotion.GridBeam);
            DrawEvent("반달 휩쓸기", EWolbaekEventMotion.SweepHalfMoon);
            DrawEvent("연쇄 부채꼴", EWolbaekEventMotion.ChainFan);
            DrawEvent("추적 장판", EWolbaekEventMotion.TrackingArea);
            DrawEvent("난사", EWolbaekEventMotion.Barrage);
            DrawEvent("원형 폭발", EWolbaekEventMotion.CircleBurst);

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /****************************************
        *                Methods
        ****************************************/

        private void DrawCommon()
        {
            GUILayout.BeginHorizontal();
            for (int i = 0; i < G_CameraNames.Length; ++i)
            {
                if (GUILayout.Button(G_CameraNames[i]))
                {
                    _cameraIndex = i;
                    ApplyCamera();
                }
            }

            GUILayout.EndHorizontal();

            GUILayout.Label($"속도 {_timeScale:F2}배");
            float scale = GUILayout.HorizontalSlider(_timeScale, MIN_TIME_SCALE, MAX_TIME_SCALE);
            if (!Mathf.Approximately(scale, _timeScale))
            {
                _timeScale = scale;
                Time.timeScale = scale;
            }

            _isRepeating = GUILayout.Toggle(_isRepeating, "끝나면 반복");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("대기로 되돌리기"))
            {
                ResetBoss();
            }

            if (GUILayout.Button(_isDefeated ? "일으키기" : "쓰러짐"))
            {
                _isDefeated = !_isDefeated;
                ResetBoss();
                _boss.SetDefeated(_isDefeated);
            }

            GUILayout.EndHorizontal();
        }

        private static void DrawSection(string title)
        {
            GUILayout.Space(COLUMN_GAP * 2f);
            GUILayout.Label($"[{title}]");
        }

        private void DrawPattern(string label, Action play)
        {
            if (GUILayout.Button(label))
            {
                Run(label, play);
            }
        }

        private void DrawEvent(string label, EWolbaekEventMotion kind)
        {
            if (GUILayout.Button(label))
            {
                Run(label, () => PlayEvent(kind));
            }
        }

        private void ApplyCamera()
        {
            if (_camera == null)
            {
                return;
            }

            Vector3 target = G_Center + (Vector3.up * 1.5f);
            _camera.transform.position = G_Center + G_CameraOffsets[_cameraIndex];
            _camera.transform.rotation = Quaternion.LookRotation(target - _camera.transform.position, Vector3.up);
        }

        // 진행 중인 재생을 정리하고 새 재생을 시작한다, 반복이 켜져 있으면 끝난 뒤 같은 모션을 다시 건다
        private void Run(string label, Action play)
        {
            ResetBoss();
            _currentName = label;
            _currentPlay = play;
            play();
        }

        private void ResetBoss()
        {
            _schedule.Clear();
            _presenter.Cancel(_castId);
            _boss.EndEventMotion(_castId, true);
            _boss.EndServerCast(_castId);
            _boss.SetPose(G_Center, Vector3.forward);
            _boss.SnapToServerPosition();
            _currentName = null;
            _currentPlay = null;
        }

        private void At(float seconds, Action call)
        {
            _schedule.Add(new Scheduled { Time = _clock + seconds, Call = call });
        }

        // 한 번 재생이 끝나는 시각에 부른다, 반복이 켜져 있으면 같은 모션을 다시 재생한다
        private void EndAt(float seconds)
        {
            Action replay = _currentPlay;
            string label = _currentName;

            At(seconds, () =>
            {
                if (_isRepeating && (replay != null))
                {
                    At(REPEAT_GAP, () => Run(label, replay));
                }
            });
        }

        private int NextCast()
        {
            return ++_castId;
        }

        private void PlayFan(string patternId)
        {
            int cast = NextCast();
            _presenter.BeginTelegraph(BossTelegraphStart.Fan(SAMPLE_BOSS_ID, cast, patternId, G_Center, Vector3.forward,
                FAN_RADIUS, FAN_ARC, _clock, DEFAULT_TELEGRAPH));
            FinishPattern(cast, DEFAULT_TELEGRAPH);
        }

        private void PlayCircle(string patternId)
        {
            int cast = NextCast();
            _presenter.BeginTelegraph(BossTelegraphStart.Circle(SAMPLE_BOSS_ID, cast, patternId, G_Center, CIRCLE_RADIUS,
                _clock, DEFAULT_TELEGRAPH));
            FinishPattern(cast, DEFAULT_TELEGRAPH);
        }

        private void FinishPattern(int cast, float telegraphSeconds)
        {
            At(telegraphSeconds, () => _presenter.Strike(cast, _clock));
            At(telegraphSeconds + RECOVER_SECONDS, () => _presenter.Finish(cast));
            EndAt(telegraphSeconds + RECOVER_SECONDS);
        }

        private void PlayDash()
        {
            int cast = NextCast();
            float speed = DASH_LENGTH / DASH_SECONDS;
            _presenter.BeginTelegraph(BossTelegraphStart.Dash(SAMPLE_BOSS_ID, cast, "wolbaek_p01", G_Center, Vector3.forward,
                DASH_LENGTH, DASH_WIDTH, _clock, DASH_TELEGRAPH));

            At(DASH_TELEGRAPH, () => _presenter.BeginDash(cast, DASH_LENGTH, DASH_SECONDS, speed, _clock));
            At(DASH_TELEGRAPH + DASH_SECONDS + DASH_TAIL_SECONDS, () =>
            {
                _presenter.Finish(cast);
                _boss.SetPose(G_Center, Vector3.forward);
            });
            EndAt(DASH_TELEGRAPH + DASH_SECONDS + DASH_TAIL_SECONDS);
        }

        // 서버는 시전 시작에 보스 위치를 착지 위치로 옮기므로 같은 순서로 부른다
        private void PlayWarp()
        {
            int cast = NextCast();
            _presenter.BeginTelegraph(BossTelegraphStart.Circle(SAMPLE_BOSS_ID, cast, BossPatternCatalogSO.WOLBAEK_P2_P07_ID,
                G_Landing, CIRCLE_RADIUS, _clock, WARP_TELEGRAPH));
            _boss.SetPose(G_Landing, Vector3.forward);

            At(WARP_TELEGRAPH, () => _presenter.Strike(cast, _clock));
            At(WARP_TAIL_SECONDS, () =>
            {
                _presenter.Finish(cast);
                _boss.SetPose(G_Center, Vector3.forward);
                _boss.SnapToServerPosition();
            });
            EndAt(WARP_TAIL_SECONDS);
        }

        private void PlayEvent(EWolbaekEventMotion kind)
        {
            int cast = NextCast();
            _boss.BeginEventMotion(cast, kind, _clock, DEFAULT_TELEGRAPH);
            At(DEFAULT_TELEGRAPH + RECOVER_SECONDS, () => _boss.EndEventMotion(cast, false));
            EndAt(DEFAULT_TELEGRAPH + RECOVER_SECONDS);
        }
    }
}
#endif
