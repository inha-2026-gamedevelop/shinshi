// System
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

// Unity
using UnityEditor;
using UnityEngine;

// 서드파티
using Unity.Cinemachine;

// 프로젝트
using Shinshi.AI;

using Object = UnityEngine.Object;

namespace Shinshi.Editor
{
    // 전투용 2페이즈 전환 컷신을 플레이 모드에서 정식 PF_WolbaekBoss와 정식 PF_GameCamera로 돌려 본다. Mirror와 서버는 없고 서버 사건과 시각은 직접 넣는다.
    // 열린 편집 씬은 저장하거나 닫지 않는다. 플레이 모드에서 만든 객체와 끈 카메라는 플레이 모드가 끝나면 원래대로 돌아간다.
    // Time.captureFramerate로 프레임 간격을 1/20초로 고정해 같은 시각의 화면과 수치를 반복해서 얻는다. 서버 시계는 그 시간에 배속을 곱한 가상 시계다
    /// <summary> 월백 2페이즈 전환 전투용 컷신의 플레이 모드 검증 </summary>
    [InitializeOnLoad]
    public static class WolbaekPhase2TransitionCombatCheck
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string RUN_KEY         = "Shinshi.P2Transition.CombatCheck";
        private const string FOLDER          = "combat_check";
        private const string BOSS_PREFAB     = "Assets/02.Prefab/Boss/PF_WolbaekBoss.prefab";
        private const string CAMERA_PREFAB   = "Assets/02.Prefab/Camera/PF_GameCamera.prefab";
        private const string GROUND_MATERIAL = "Assets/00.Scenes/Dev_VFX/M_Dev_GroundDarkGrass.mat";
        private const int    CAPTURE_RATE    = 20;
        private const int    WIDTH           = 960;
        private const int    HEIGHT          = 540;
        private const int    DEPTH_BITS      = 24;
        private const int    JPG_QUALITY     = 88;
        private const float  GROUND_SCALE    = 12f;
        private const float  LIGHT_INTENSITY = 1.25f;
        private const float  PLAYER_DISTANCE = 6f;
        private const double WINDOW          = 9.6d;
        private const double START_SERVER    = 1000d;
        private const double PRE_ROLL        = 0.5d;
        private const double BLEND_TAIL      = 2.6d;
        private const double FAST            = 4d;

        private static readonly Vector3 G_PlaceA     = new Vector3(300f, 0f, 300f);
        private static readonly Vector3 G_PlaceB     = new Vector3(318f, 0f, 286f);
        private const float             YAW_A        = 135f;
        private const float             YAW_B        = -60f;
        private static readonly Vector3 G_LightEuler = new Vector3(38f, 210f, 0f);
        private static readonly int      OCCLUSION_CAMERA_ID = Shader.PropertyToID("_ShinshiOcclusionCamera");
        private static readonly double[] G_KeyTimes  = { 0d, 0.3d, 0.55d, 0.75d, 2.1d, 3.0d, 4.5d, 5.5d, 6.2d, 6.8d, 8.0d, 9.5d };

        /****************************************
        *                Fields
        ****************************************/

        private static readonly Stack<IEnumerator> _routines = new Stack<IEnumerator>();
        private static int                 _lastFrame;
        private static StringBuilder       _csv;
        private static StringBuilder       _summary;
        private static string              _dir;
        private static Texture2D           _frame;
        private static Camera              _camera;
        private static CinemachineBrain    _brain;
        private static CinemachineCamera   _follow;
        private static GameObject          _boss;
        private static BossView            _view;
        private static WolbaekPhase2TransitionPlayer _player;
        private static BossNetworkViewBinder _binder;
        private static WolbaekColorWardView _ward;
        private static Animator            _animator;
        private static Transform           _dummyPlayer;
        private static double              _clockBase;
        private static double              _clockAnchor;
        private static double              _clockSpeed = 1d;
        private static int                 _failures;
        private static int                 _startedCount;
        private static Camera              _fixedCamera;
        private static string              _shieldPrefabName;
        private static int                 _stoppedCount;
        private static readonly HashSet<WolbaekPhase2TransitionRig> _rigIds = new HashSet<WolbaekPhase2TransitionRig>();

        /****************************************
        *                Methods
        ****************************************/

        static WolbaekPhase2TransitionCombatCheck()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem(WolbaekPhase2TransitionAssets.MENU_ROOT + "6. Verify Combat Cutscene (Play Mode)")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[P2Combat] 플레이 모드가 아닐 때 실행한다");
                return;
            }

            SessionState.SetBool(RUN_KEY, true);
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if ((change != PlayModeStateChange.EnteredPlayMode) || (!SessionState.GetBool(RUN_KEY, false)))
            {
                return;
            }

            SessionState.SetBool(RUN_KEY, false);
            _routines.Clear();
            _routines.Push(Scenarios());
            _lastFrame = -1;
            EditorApplication.update += Step;
        }

        private static void Step()
        {
            if (!EditorApplication.isPlaying)
            {
                Finish(false);
                return;
            }

            if (Time.frameCount == _lastFrame)
            {
                return;
            }

            _lastFrame = Time.frameCount;
            bool hasNext;
            try
            {
                hasNext = Advance();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Fail("예외 " + exception.GetType().Name + " " + exception.Message);
                hasNext = false;
            }

            if (!hasNext)
            {
                Finish(true);
            }
        }

        private static void Finish(bool exitPlayMode)
        {
            EditorApplication.update -= Step;
            Time.captureFramerate = 0;
            CinemachineCore.UniformDeltaTimeOverride = -1f;
            if ((_csv != null) && (_dir != null))
            {
                File.WriteAllText(Path.Combine(_dir, "combat_check.csv"), _csv.ToString());
                _summary.AppendLine(_failures == 0 ? "RESULT PASS" : $"RESULT FAIL {_failures}");
                File.WriteAllText(Path.Combine(_dir, "summary.txt"), _summary.ToString());
                Debug.Log($"[P2Combat] 검증 끝, 실패 {_failures}건, 기록 {_dir}");
            }

            if (_frame != null)
            {
                Object.Destroy(_frame);
                _frame = null;
            }

            _csv = null;
            _routines.Clear();
            if (exitPlayMode && EditorApplication.isPlaying)
            {
                EditorApplication.ExitPlaymode();
            }
        }

        // 안쪽 시나리오(IEnumerator를 돌려준 경우)를 같은 프레임에 이어서 돌리고, null을 만나면 한 프레임 쉰다
        private static bool Advance()
        {
            while (_routines.Count > 0)
            {
                IEnumerator top = _routines.Peek();
                if (!top.MoveNext())
                {
                    _routines.Pop();
                    continue;
                }

                if (top.Current is IEnumerator nested)
                {
                    _routines.Push(nested);
                    continue;
                }

                return true;
            }

            return false;
        }

        /****************************************
        *               Scenarios
        ****************************************/

        private static IEnumerator Scenarios()
        {
            Setup();
            yield return null;
            yield return null;

            if (_failures > 0)
            {
                yield break;
            }

            IEnumerator[] steps =
            {
                Normal(), LateJoin(), AlreadyEnded(), ScheduledThenUnbind(), DeathMidway(), ResetMidway(), CombatEndMidway(),
                DisableMidway(), ReuseAtOtherPlace(), LengthMismatch(), OrderInversion(), BinderRestore(), ShieldHandoverNormal(G_PlaceA, YAW_A, "shield_a"), ShieldHandoverFailure(),
                ShieldHandoverEnded(), ShieldHandoverCueFirst(), ShieldHandoverLate(0.3d), ShieldHandoverLate(1.2d), ShieldHandoverInterrupt(0), ShieldHandoverInterrupt(1),
                ShieldHandoverInterrupt(2), ShieldHandoverNormal(G_PlaceB, YAW_B, "shield_b"),
                ShieldContinuity(G_PlaceA, YAW_A, "shield_seq_a", 0d, 90), ShieldContinuity(G_PlaceA, YAW_A, "shield_seq_late", 0.3d, 91),
                ShieldContinuity(G_PlaceB, YAW_B, "shield_seq_b", 0d, 92), DestroyMidway()
            };

            for (int i = 0; i < steps.Length; ++i)
            {
                yield return steps[i];
            }
        }

        // 정상 경로: 시작 전 전투 카메라, 9.6초 재생, 끝난 뒤 전투 카메라로 돌아오는 섞기까지 매 프레임 캡처한다.
        // 재생 도중 같은 사건 재수신, 오래된 번호, 다른 전투 번호, 모션 재생기의 덮어쓰기 시도를 넣는다
        private static IEnumerator Normal()
        {
            const string name = "normal";
            Place(G_PlaceA, YAW_A);
            _startedCount = 0;
            _stoppedCount = 0;
            _player.ResetSession(1);
            SetClock(START_SERVER - PRE_ROLL, 1d);
            Vector3 modelPosition = _animator.transform.localPosition;
            Quaternion modelRotation = _animator.transform.localRotation;
            WolbaekPhase2TransitionCue cue = new WolbaekPhase2TransitionCue(1, 1, START_SERVER, START_SERVER + WINDOW);
            bool isSent = false;
            bool isDuplicateSent = false;
            bool isInterferenceSent = false;
            int frame = 0;

            while (Now() < (START_SERVER + WINDOW + BLEND_TAIL))
            {
                double now = Now();
                if ((!isSent) && (now >= START_SERVER))
                {
                    isSent = true;
                    Expect(name, "first", _player.Receive(in cue, now) == EWolbaekPhase2TransitionDecision.Play);
                }

                if ((isSent) && (!isDuplicateSent) && (now >= START_SERVER + 1d))
                {
                    isDuplicateSent = true;
                    Expect(name, "duplicate event", _player.Receive(in cue, now) == EWolbaekPhase2TransitionDecision.Duplicate);
                    Expect(name, "duplicate restore via binder", _binder.OnPhase2TransitionRestored(in cue) == EWolbaekPhase2TransitionDecision.Duplicate);
                    WolbaekPhase2TransitionCue older = new WolbaekPhase2TransitionCue(1, 0, START_SERVER, START_SERVER + WINDOW);
                    Expect(name, "invalid id", _player.Receive(in older, now) == EWolbaekPhase2TransitionDecision.Invalid);
                    WolbaekPhase2TransitionCue other = new WolbaekPhase2TransitionCue(99, 2, START_SERVER, START_SERVER + WINDOW);
                    Expect(name, "other combat", _player.Receive(in other, now) == EWolbaekPhase2TransitionDecision.OtherSession);
                    Expect(name, "still one playback", _player.IsPlaying && (_player.TransitionId == 1));
                }

                // 색 결계 그로기 해제와 패턴 모션이 같은 Animator에 Idle을 넣는 상황
                if ((isSent) && (!isInterferenceSent) && (now >= START_SERVER + 4d))
                {
                    isInterferenceSent = true;
                    _view.PlayGimmickMotion("Idle", 1f, 0f);
                    Expect(name, "animator speed untouched", Mathf.Approximately(_animator.speed, 1f));
                }

                TrackRig();
                Record(name, now);
                Capture($"{name}/frame_{frame:0000}");
                ++frame;
                yield return null;
            }

            Expect(name, "completed", _player.LastStop == EWolbaekPhase2TransitionStop.Completed);
            Expect(name, "control returned", !_view.IsExternallyControlled);
            Expect(name, "model restored", (_animator.transform.localPosition - modelPosition).sqrMagnitude < 1e-8f
                && Quaternion.Angle(_animator.transform.localRotation, modelRotation) < 0.01f);
            Expect(name, "rig released", _player.ActiveRig == null);
            Expect(name, "back to combat camera", ReferenceEquals(_brain.ActiveVirtualCamera, _follow) && (_brain.ActiveBlend == null));
            Expect(name, "single rig instance", _rigIds.Count == 1);
            Expect(name, "no leftover vfx", CountCutsceneVfx() == 0);
            ExpectMotionRestored(name, true);
            Expect(name, "started event once", _startedCount == 1);
            Expect(name, "stopped event once", _stoppedCount == 1);
        }

        // 재접속처럼 새 세션에서 서버 시작 5초 뒤에 받는다. 처음 평가한 시각이 5초여야 하고, 이어서 정상 종료해야 한다
        private static IEnumerator LateJoin()
        {
            const string name = "late";
            _player.ResetSession(2);
            SetClock(START_SERVER + 5d, 1d);
            WolbaekPhase2TransitionCue cue = new WolbaekPhase2TransitionCue(2, 1, START_SERVER, START_SERVER + WINDOW);
            Expect(name, "decision play", _player.Receive(in cue, Now()) == EWolbaekPhase2TransitionDecision.Play);
            Expect(name, "starts at server elapsed", Math.Abs(_player.PlaybackTime - 5d) < 0.001d);
            yield return null;
            Record(name, Now());
            Capture($"{name}/at_5.0");

            SetClock(Now(), FAST);
            while (_player.IsPlaying)
            {
                Record(name, Now());
                yield return null;
            }

            Expect(name, "completed", _player.LastStop == EWolbaekPhase2TransitionStop.Completed);
            yield return WaitRelease();
        }

        private static IEnumerator AlreadyEnded()
        {
            const string name = "ended";
            _player.ResetSession(3);
            SetClock(START_SERVER + WINDOW + 0.4d, 1d);
            WolbaekPhase2TransitionCue cue = new WolbaekPhase2TransitionCue(3, 1, START_SERVER, START_SERVER + WINDOW);
            Expect(name, "decision ended", _player.Receive(in cue, Now()) == EWolbaekPhase2TransitionDecision.Ended);
            for (int i = 0; i < 3; ++i)
            {
                Expect(name, "no playback", (!_player.IsPlaying) && (_player.ActiveRig == null) && (!_view.IsExternallyControlled));
                yield return null;
            }

            Expect(name, "same id later ignored", _player.Receive(in cue, Now()) == EWolbaekPhase2TransitionDecision.Duplicate);
        }

        // 서버 시작이 0.3초 미래면 그 시각에 시작한다. 1초에 연결 해제(Unbind)하면 장비와 제어권이 바로 정리되고 다음 세션 전까지 사건을 받지 않는다
        private static IEnumerator ScheduledThenUnbind()
        {
            const string name = "scheduled_unbind";
            _player.ResetSession(4);
            SetClock(START_SERVER - 0.3d, 1d);
            WolbaekPhase2TransitionCue cue = new WolbaekPhase2TransitionCue(4, 1, START_SERVER, START_SERVER + WINDOW);
            Expect(name, "decision scheduled", _player.Receive(in cue, Now()) == EWolbaekPhase2TransitionDecision.Scheduled);
            Expect(name, "waits", _player.IsPending && (_player.ActiveRig == null));
            while (Now() < START_SERVER + 1d)
            {
                if (Now() >= START_SERVER + 0.06d)
                {
                    Expect(name, "started at server start", _player.IsPlaying);
                }

                yield return null;
            }

            yield return InterruptChecks(name, () => _binder.Unbind(), EWolbaekPhase2TransitionStop.Unbound);
            ExpectMotionRestored(name, false);
            WolbaekPhase2TransitionCue next = new WolbaekPhase2TransitionCue(4, 2, Now(), Now() + WINDOW);
            Expect(name, "ignored after unbind", _player.Receive(in next, Now()) == EWolbaekPhase2TransitionDecision.OtherSession);
        }

        // 떠 있는 4초에 처치: 컷신 장비가 바로 사라지고 모델 자세가 돌아온 뒤 처치 모션이 시작돼야 한다
        private static IEnumerator DeathMidway()
        {
            const string name = "death";
            yield return StartFresh(name, 5, 4d);
            Capture($"{name}/before");
            yield return InterruptChecks(name, () => _view.SetDefeated(true), EWolbaekPhase2TransitionStop.Revoked);
            for (int i = 0; i < CAPTURE_RATE; ++i)
            {
                yield return null;
            }

            AnimatorStateInfo state = _animator.GetCurrentAnimatorStateInfo(0);
            AnimatorStateInfo next = _animator.GetNextAnimatorStateInfo(0);
            // 카메라 복귀를 기다리는 동안(약 3.7초) 쓰러짐 3초가 끝나 정지 자세로 넘어가 있을 수 있다. 정지 자세는 처치 모션을 거쳐야만 들어간다
            bool isDeath = state.IsName(WolbaekMotionCatalog.DEATH_STATE) || next.IsName(WolbaekMotionCatalog.DEATH_STATE)
                || state.IsName(WolbaekMotionCatalog.DEATH_HOLD_STATE) || next.IsName(WolbaekMotionCatalog.DEATH_HOLD_STATE);
            Expect(name, "death motion played (fall or hold state)", isDeath);
            _summary.AppendLine($"{name} animatorState fall={state.IsName(WolbaekMotionCatalog.DEATH_STATE)} hold={state.IsName(WolbaekMotionCatalog.DEATH_HOLD_STATE)} normalized={state.normalizedTime:F2}");
            Record(name, Now());
            Capture($"{name}/after_1s");

            // 같은 보스를 다시 쓰기 위해 되살리고 Reset한다
            _view.SetDefeated(false);
            _view.ResetView();
            Place(G_PlaceA, YAW_A);
            yield return null;
        }

        private static IEnumerator ResetMidway()
        {
            const string name = "reset";
            yield return StartFresh(name, 6, 2d);
            yield return InterruptChecks(name, () => _view.ResetView(), EWolbaekPhase2TransitionStop.Revoked);
            ExpectMotionRestored(name, false);
            Place(G_PlaceA, YAW_A);
        }

        private static IEnumerator CombatEndMidway()
        {
            const string name = "combat_end";
            yield return StartFresh(name, 7, 2d);
            yield return InterruptChecks(name, () => _view.SetFlowState(EBossFlowState.Ended), EWolbaekPhase2TransitionStop.Revoked);
            ExpectMotionRestored(name, true);
            _view.SetFlowState(EBossFlowState.Combat);
        }

        private static IEnumerator DisableMidway()
        {
            const string name = "disable";
            yield return StartFresh(name, 8, 2d);
            yield return InterruptChecks(name, () => _player.enabled = false, EWolbaekPhase2TransitionStop.Disabled);
            ExpectMotionRestored(name, true);
            _player.enabled = true;
        }

        // 다른 위치와 방향에서 같은 보스 인스턴스로 다시 재생한다. 장비 기준 좌표의 머리 위치가 첫 재생과 같아야 한다
        private static IEnumerator ReuseAtOtherPlace()
        {
            const string name = "reuse_b";
            Place(G_PlaceB, YAW_B);
            yield return null;
            _player.ResetSession(9);
            SetClock(START_SERVER - 0.1d, 1d);
            WolbaekPhase2TransitionCue cue = new WolbaekPhase2TransitionCue(9, 1, START_SERVER, START_SERVER + WINDOW);
            bool isSent = false;
            int key = 0;
            while (Now() < START_SERVER + WINDOW + BLEND_TAIL)
            {
                double now = Now();
                if ((!isSent) && (now >= START_SERVER))
                {
                    isSent = true;
                    Expect(name, "decision play", _player.Receive(in cue, now) == EWolbaekPhase2TransitionDecision.Play);
                }

                Record(name, now);
                if ((key < G_KeyTimes.Length) && (now >= START_SERVER + G_KeyTimes[key] - 1e-6d))
                {
                    Capture($"{name}/key_{G_KeyTimes[key]:0.00}");
                    ++key;
                }

                yield return null;
            }

            Expect(name, "completed", _player.LastStop == EWolbaekPhase2TransitionStop.Completed);
            Expect(name, "back to combat camera", ReferenceEquals(_brain.ActiveVirtualCamera, _follow) && (_brain.ActiveBlend == null));
            Place(G_PlaceA, YAW_A);
        }

        // 서버 구간이 Timeline(9.6초)보다 짧으면 서버 종료 시각에 자르고, 길면 마지막 장면을 서버 종료까지 유지한다. 두 경우 모두 경고를 남긴다
        private static IEnumerator LengthMismatch()
        {
            const string name = "length";
            Place(G_PlaceA, YAW_A);
            double[] windows = { 8.0d, 11.0d };
            for (int w = 0; w < windows.Length; ++w)
            {
                _player.ResetSession(20 + w);
                SetClock(START_SERVER, FAST);
                WolbaekPhase2TransitionCue cue = new WolbaekPhase2TransitionCue(20 + w, 1, START_SERVER, START_SERVER + windows[w]);
                Expect(name, $"window {windows[w]} play", _player.Receive(in cue, Now()) == EWolbaekPhase2TransitionDecision.Play);
                double maxPlayback = 0d;
                double endedAt = -1d;
                while (Now() < START_SERVER + windows[w] + 1d)
                {
                    if (_player.IsPlaying)
                    {
                        maxPlayback = Math.Max(maxPlayback, _player.PlaybackTime);
                    }
                    else if (endedAt < 0d)
                    {
                        endedAt = Now() - START_SERVER;
                    }

                    yield return null;
                }

                _summary.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} window={1:F1} maxPlayback={2:F3} endedAt={3:F3} stop={4}",
                    name, windows[w], maxPlayback, endedAt, _player.LastStop));
                Expect(name, $"window {windows[w]} completed at server end", (_player.LastStop == EWolbaekPhase2TransitionStop.Completed)
                    && (endedAt >= windows[w] - 1e-6d) && (endedAt <= windows[w] + (FAST / CAPTURE_RATE) + 1e-6d));
                Expect(name, $"window {windows[w]} playback within timeline", maxPlayback <= Math.Min(windows[w], WINDOW) + 1e-6d);
                ExpectMotionRestored(name, true);
            }
        }

        // 수신 순서 역전: (a) 색 결계 성공과 PhaseState 전환이 먼저 오고 전환 구간 사건이 0.2초 늦게 온다, (b) 전환 구간 사건이 먼저 오고 성공과 PhaseState가 0.3초 뒤에 온다.
        // 두 경우 모두 컷신은 한 번만 시작하고, 뒤에 온 그로기 해제(Idle 요청)가 컷신 자세를 덮지 않아야 한다
        private static IEnumerator OrderInversion()
        {
            const string name = "order";
            for (int order = 0; order < 2; ++order)
            {
                Place(G_PlaceA, YAW_A);
                yield return null;
                int combat = 30 + order;
                _player.ResetSession(combat);
                SetClock(START_SERVER - 0.5d, 1d);
                _ward.SetServerClock(Now);
                WolbaekWardStart start = new WolbaekWardStart(combat, 1, Now(), _boss.transform.position, 5f, 8f, 3f, 3f, 4f, WardMembers());
                _ward.StartWard(in start, Now());
                while (Now() < START_SERVER)
                {
                    yield return null;
                }

                _startedCount = 0;
                _stoppedCount = 0;
                WolbaekPhase2TransitionCue cue = new WolbaekPhase2TransitionCue(combat, 1, START_SERVER, START_SERVER + WINDOW);
                double cueAt = (order == 0) ? START_SERVER + 0.2d : START_SERVER;
                double wardAt = (order == 0) ? START_SERVER : START_SERVER + 0.3d;
                bool isCueSent = false;
                bool isWardSent = false;
                double firstPlayback = -1d;
                while (Now() < START_SERVER + 5d)
                {
                    double now = Now();
                    if ((!isWardSent) && (now >= wardAt))
                    {
                        isWardSent = true;
                        _ward.Succeeded(1);
                        _ward.PhaseTransitionStarted();
                    }

                    if ((!isCueSent) && (now >= cueAt))
                    {
                        isCueSent = true;
                        _player.Receive(in cue, now);
                        firstPlayback = _player.PlaybackTime;
                        _binder.OnPhase2TransitionRestored(in cue);
                    }

                    yield return null;
                }

                Transform head = _animator.GetBoneTransform(HumanBodyBones.Head);
                float headLocalY = _player.ActiveRig.transform.InverseTransformPoint(head.position).y;
                _summary.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} case={1} firstPlayback={2:F3} headLocalYAt5={3:F4}", name, order, firstPlayback, headLocalY));
                Expect(name, $"case {order} started once", (_startedCount == 1) && _player.IsPlaying);
                Expect(name, $"case {order} first playback follows server elapsed", Math.Abs(firstPlayback - (cueAt - START_SERVER)) < 0.001d);
                Expect(name, $"case {order} pose not overwritten by groggy release", Math.Abs(headLocalY - 3.332f) < 0.01f);
                _player.Stop(EWolbaekPhase2TransitionStop.Disabled);
                _ward.Clear();
                yield return null;
            }
        }

        // 접속 복원 입력을 바인더 경로(OnPhase2TransitionRestored, 바인더의 서버 시계)로 넣는다. 중간 접속은 경과 시간부터, 끝난 뒤 접속은 재생하지 않는다
        private static IEnumerator BinderRestore()
        {
            const string name = "binder_restore";
            Func<double> binderClock = (Func<double>)Delegate.CreateDelegate(typeof(Func<double>),
                typeof(BossNetworkViewBinder).GetMethod("ReadServerTime", BindingFlags.NonPublic | BindingFlags.Static));
            _player.SetServerClock(binderClock);
            _player.ResetSession(40);
            double now = binderClock();
            WolbaekPhase2TransitionCue mid = new WolbaekPhase2TransitionCue(40, 1, now - 3d, now - 3d + WINDOW);
            Expect(name, "mid join plays", _binder.OnPhase2TransitionRestored(in mid) == EWolbaekPhase2TransitionDecision.Play);
            Expect(name, "mid join starts at elapsed", Math.Abs(_player.PlaybackTime - 3d) < 0.05d);
            yield return null;
            Expect(name, "mid join keeps running", _player.IsPlaying);
            _player.Stop(EWolbaekPhase2TransitionStop.Disabled);

            _player.ResetSession(41);
            now = binderClock();
            WolbaekPhase2TransitionCue over = new WolbaekPhase2TransitionCue(41, 1, now - 12d, now - 12d + WINDOW);
            Expect(name, "join after end not played", _binder.OnPhase2TransitionRestored(in over) == EWolbaekPhase2TransitionDecision.Ended);
            yield return null;
            Expect(name, "no rig after ended restore", (_player.ActiveRig == null) && (!_view.IsExternallyControlled));
            _player.SetServerClock(Now);
        }

        // 씬 이동처럼 보스 오브젝트가 통째로 사라지는 경우. 장비, VFX, 카메라 덮어쓰기가 남지 않아야 한다. 마지막 시나리오다
        private static IEnumerator DestroyMidway()
        {
            const string name = "destroy";
            Place(G_PlaceA, YAW_A);
            yield return StartFresh(name, 50, 2d);
            WolbaekPhase2TransitionRig rig = _player.ActiveRig;
            Expect(name, "was playing", rig != null);
            Object.Destroy(_boss);
            yield return null;
            yield return null;
            Expect(name, "rig destroyed with boss", rig == null);
            Expect(name, "no orphan vfx", CountOrphanCutsceneVfx() == 0);
            double until = Now() + BLEND_TAIL;
            while (Now() < until)
            {
                yield return null;
            }

            Expect(name, "back to combat camera", ReferenceEquals(_brain.ActiveVirtualCamera, _follow) && (_brain.ActiveBlend == null));
        }

        private static WolbaekWardMember[] WardMembers()
        {
            return new[]
            {
                new WolbaekWardMember(11, EWolbaekWardColor.Red, 0),
                new WolbaekWardMember(12, EWolbaekWardColor.Red, 0),
                new WolbaekWardMember(13, EWolbaekWardColor.Blue, 1),
                new WolbaekWardMember(14, EWolbaekWardColor.Blue, 1)
            };
        }

        /****************************************
        *            Shield Handover
        ****************************************/

        private static readonly double[] G_ShieldKeys = { 0d, 0.3d, 0.55d, 0.733d, 0.767d };
        private const float CM01_FOV = 34f;
        private static readonly Vector3 G_Cm01Local = new Vector3(2.7f, 0.55f, 3.3f); // 시안 CM_01 위치, 장비 비율을 곱해 같은 구도로 놓는다

        // 정상 성공과 같은 서버 틱에 전환이 시작되는 경우. 인계 직전, 직후, 0/0.3/0.55/0.733/0.767초를 CM_01 위치에 고정한 같은 카메라로 그린다
        private static IEnumerator ShieldHandoverNormal(Vector3 place, float yaw, string name)
        {
            yield return PrepareWard(place, yaw, 60 + (int)Mathf.Abs(yaw));
            AimFixedCamera();
            while (Now() < START_SERVER - 0.05d)
            {
                yield return null;
            }

            ShieldRow(name, "pre");
            CaptureFixed($"{name}/fixed_pre");
            while (Now() < START_SERVER)
            {
                yield return null;
            }

            _ward.Succeeded(1);
            GameObject official = OfficialShield();
            WolbaekPhase2TransitionCue cue = Cue(60 + (int)Mathf.Abs(yaw), START_SERVER);
            Expect(name, "decision play", _player.Receive(in cue, Now()) == EWolbaekPhase2TransitionDecision.Play);
            Expect(name, "handed fading shield", _player.LastShieldHandover == EWolbaekShieldHandover.FadingShield);
            ShieldRow(name, "post");
            CaptureFixed($"{name}/fixed_post");
            Expect(name, "same official instance handed", (official != null) && ReferenceEquals(_player.HandedShield, official));
            Expect(name, "one shield right after handover", (CountOfficialShields() == 1) && (CountCutsceneShieldParticles() == 0));

            for (int i = 0; i < G_ShieldKeys.Length; ++i)
            {
                yield return SeekKey(G_ShieldKeys[i]);
                string label = $"t{G_ShieldKeys[i]:0.000}";
                ShieldRow(name, label);
                CaptureFixed($"{name}/fixed_{label}");
                Expect(name, label + " never two shields", (CountOfficialShields() <= 1) && (CountCutsceneShieldParticles() == 0));
            }

            Expect(name, "transparent before clip end (0.733)", _lastShieldMaxAlphaAt0733 == 0);
            Expect(name, "gone after clip end (0.767)", (CountOfficialShields() == 0) && (_player.HandedShield == null) && (CountCutsceneShieldParticles() == 0));
            _player.Stop(EWolbaekPhase2TransitionStop.Disabled);
            _ward.Clear();
            yield return null;
        }

        // 장비가 없어 재생을 시작하지 못하면 보호막을 넘기지 않고 정식 2.5초 소멸이 그대로 이어져야 한다
        private static IEnumerator ShieldHandoverFailure()
        {
            const string name = "shield_fail";
            yield return PrepareWard(G_PlaceA, YAW_A, 70);
            while (Now() < START_SERVER)
            {
                yield return null;
            }

            object rigPrefab = GetPrivate(_player, "_rigPrefab");
            SetPrivate(_player, "_rigPrefab", null);
            _ward.Succeeded(1);
            WolbaekPhase2TransitionCue cue = Cue(70, START_SERVER);
            _player.Receive(in cue, Now());
            Expect(name, "not playing", (!_player.IsPlaying) && (_player.LastStop == EWolbaekPhase2TransitionStop.Unavailable));
            Expect(name, "no handover", (_player.LastShieldHandover == EWolbaekShieldHandover.None) && _ward.HasFadingShield);
            yield return ExpectOfficialFadeRunsOut(name);
            SetPrivate(_player, "_rigPrefab", rigPrefab);
            _ward.Clear();
        }

        // 받은 순간 전환 구간이 이미 끝났으면 컷신도 인계도 없고 정식 소멸이 그대로다
        private static IEnumerator ShieldHandoverEnded()
        {
            const string name = "shield_ended";
            yield return PrepareWard(G_PlaceA, YAW_A, 71);
            while (Now() < START_SERVER)
            {
                yield return null;
            }

            _ward.Succeeded(1);
            WolbaekPhase2TransitionCue cue = new WolbaekPhase2TransitionCue(71, 1, START_SERVER - 10d, START_SERVER - 0.4d);
            Expect(name, "decision ended", _player.Receive(in cue, Now()) == EWolbaekPhase2TransitionDecision.Ended);
            Expect(name, "official still fading", _ward.HasFadingShield && (CountOfficialShields() == 1));
            yield return ExpectOfficialFadeRunsOut(name);
            _ward.Clear();
        }

        // 전환 사건이 먼저 오고 성공 사건이 0.3초 뒤에 온다. 늦은 성공, 중복 성공, 지난 시전, 같은 시전 재시작, 중복 전환 사건이 보호막을 만들지 않아야 한다
        private static IEnumerator ShieldHandoverCueFirst()
        {
            const string name = "shield_cue_first";
            yield return PrepareWard(G_PlaceA, YAW_A, 72);
            while (Now() < START_SERVER)
            {
                yield return null;
            }

            GameObject official = OfficialShield();
            WolbaekPhase2TransitionCue cue = Cue(72, START_SERVER);
            _player.Receive(in cue, Now());
            Expect(name, "handed active shield", _player.LastShieldHandover == EWolbaekShieldHandover.ActiveShield);
            Expect(name, "same official instance handed", (official != null) && ReferenceEquals(_player.HandedShield, official));
            Expect(name, "one shield", (CountOfficialShields() == 1) && (CountCutsceneShieldParticles() == 0));
            while (Now() < START_SERVER + 0.3d)
            {
                yield return null;
            }

            _ward.Succeeded(1);
            _ward.PhaseTransitionStarted();
            _ward.Succeeded(1);
            _ward.Succeeded(0);
            WolbaekWardStart again = new WolbaekWardStart(72, 1, Now(), _boss.transform.position, 5f, 8f, 3f, 3f, 4f, WardMembers());
            Expect(name, "same cast restart rejected", !_ward.StartWard(in again, Now()));
            Expect(name, "duplicate cue ignored", _player.Receive(in cue, Now()) == EWolbaekPhase2TransitionDecision.Duplicate);
            yield return null;
            Expect(name, "late success made no shield", (CountOfficialShields() == 1) && ReferenceEquals(_player.HandedShield, official) && (!_ward.HasFadingShield)
                && (CountCutsceneShieldParticles() == 0));
            ShieldRow(name, "after_late_success");
            yield return SeekKey(0.767d);
            Expect(name, "no shield after 0.767", (CountOfficialShields() == 0) && (_player.HandedShield == null) && (CountCutsceneShieldParticles() == 0));
            _player.Stop(EWolbaekPhase2TransitionStop.Disabled);
            _ward.Clear();
            yield return null;
        }

        // 성공은 제때 왔고 전환 사건이 늦게 왔다. 0.75초 전이면 경과 시각의 컷신 보호막으로 넘기고, 지난 뒤면 보호막을 다시 보이지 않는다
        private static IEnumerator ShieldHandoverLate(double delay)
        {
            string name = $"shield_late_{delay:0.0}";
            int combat = 73 + (int)(delay * 10d);
            yield return PrepareWard(G_PlaceA, YAW_A, combat);
            AimFixedCamera();
            while (Now() < START_SERVER)
            {
                yield return null;
            }

            _ward.Succeeded(1);
            while (Now() < START_SERVER + delay - 0.05d)
            {
                yield return null;
            }

            ShieldRow(name, "pre");
            CaptureFixed($"{name}/fixed_pre");
            yield return null;
            GameObject official = OfficialShield();
            OfficialShieldStats(out _, out _, out int alphaBefore, out _);
            WolbaekPhase2TransitionCue cue = Cue(combat, START_SERVER);
            _player.Receive(in cue, Now());
            OfficialShieldStats(out _, out _, out int alphaAfter, out _);
            Expect(name, "starts at server elapsed", Math.Abs(_player.PlaybackTime - delay) < 0.06d);
            Expect(name, "handed fading shield", _player.LastShieldHandover == EWolbaekShieldHandover.FadingShield);
            ShieldRow(name, "post");
            CaptureFixed($"{name}/fixed_post");
            if (delay < 0.75d)
            {
                Expect(name, "same official instance continues", ReferenceEquals(_player.HandedShield, official) && (CountOfficialShields() == 1)
                    && (CountCutsceneShieldParticles() == 0));
                Expect(name, $"brightness continues at handover ({alphaBefore}->{alphaAfter})", (alphaBefore > 0) && (Mathf.Abs(alphaAfter - alphaBefore) <= 1));
            }
            else
            {
                Expect(name, "official removed", (CountOfficialShields() == 0) && (_player.HandedShield == null));
                Expect(name, "no shield replayed after 0.75", CountCutsceneShieldParticles() <= 0);
            }

            _player.Stop(EWolbaekPhase2TransitionStop.Disabled);
            _ward.Clear();
            yield return null;
        }

        // 인계 뒤 0.3초에 사망, Reset, 언바인드. 두 보호막과 소유 참조가 모두 정리되고 처치 모션은 이어져야 한다
        private static IEnumerator ShieldHandoverInterrupt(int kind)
        {
            string[] names = { "shield_death", "shield_reset", "shield_unbind" };
            string name = names[kind];
            yield return PrepareWard(G_PlaceA, YAW_A, 80 + kind);
            while (Now() < START_SERVER)
            {
                yield return null;
            }

            _ward.Succeeded(1);
            WolbaekPhase2TransitionCue cue = Cue(80 + kind, START_SERVER);
            _player.Receive(in cue, Now());
            while (Now() < START_SERVER + 0.3d)
            {
                yield return null;
            }

            Expect(name, "handed and playing", _player.IsPlaying && (_player.LastShieldHandover == EWolbaekShieldHandover.FadingShield));
            if (kind == 0)
            {
                _view.SetDefeated(true);
            }
            else if (kind == 1)
            {
                _view.ResetView();
            }
            else
            {
                _binder.Unbind();
            }

            yield return null;
            Expect(name, "no shields", (CountOfficialShields() == 0) && (_player.ActiveRig == null) && (_player.HandedShield == null));
            Expect(name, "references cleared", (!_ward.HasFadingShield) && (_ward.HandoverCastId == 0));
            if (kind == 0)
            {
                for (int i = 0; i < CAPTURE_RATE / 2; ++i)
                {
                    yield return null;
                }

                AnimatorStateInfo state = _animator.GetCurrentAnimatorStateInfo(0);
                AnimatorStateInfo next = _animator.GetNextAnimatorStateInfo(0);
                Expect(name, "death motion continues", state.IsName(WolbaekMotionCatalog.DEATH_STATE) || next.IsName(WolbaekMotionCatalog.DEATH_STATE));
                _view.SetDefeated(false);
                _view.ResetView();
            }

            Place(G_PlaceA, YAW_A);
            yield return null;
        }

        private static IEnumerator PrepareWard(Vector3 place, float yaw, int combat)
        {
            Place(place, yaw);
            yield return null;
            _player.ResetSession(combat);
            _ward.ResetNetworkSession();
            SetClock(START_SERVER - 1.2d, 1d);
            _ward.SetServerClock(Now);
            WolbaekWardStart start = new WolbaekWardStart(combat, 1, Now(), _boss.transform.position, 5f, 8f, 3f, 3f, 4f, WardMembers());
            Expect("ward", $"started combat {combat}", _ward.StartWard(in start, Now()));
        }

        private static WolbaekPhase2TransitionCue Cue(int combat, double start)
        {
            return new WolbaekPhase2TransitionCue(combat, 1, start, start + WINDOW);
        }

        // 정확한 시각을 그리려고 서버 시계를 그 시각으로 맞추고 컷신을 직접 진행한다. 다음 프레임부터는 그 시각에서 이어 간다
        private static IEnumerator SeekKey(double key)
        {
            while (Now() < START_SERVER + key - (1d / CAPTURE_RATE))
            {
                yield return null;
            }

            SetClock(START_SERVER + key, 1d);
            _player.Tick(Now());
            _player.ReapplyPose();
        }

        private static IEnumerator ExpectOfficialFadeRunsOut(string name)
        {
            while (Now() < START_SERVER + 1.2d)
            {
                yield return null;
            }

            Expect(name, "official fading at 1.2s", (CountOfficialShields() == 1) && (VisibleOfficialParticles() > 0));
            while (Now() < START_SERVER + 2.6d)
            {
                yield return null;
            }

            Expect(name, "official removed after 2.5s", CountOfficialShields() == 0);
        }

        private static int _lastShieldMaxAlphaAt0733 = -1;

        private static void ShieldRow(string scenario, string label)
        {
            int cutsceneParticles = CountCutsceneShieldParticles();
            int cutsceneMaxAlpha = CutsceneShieldMaxAlpha();
            bool hasOfficial = OfficialShieldStats(out Vector3 center, out float size, out int officialMaxAlpha, out long officialAlphaSum);
            if (label == "t0.733")
            {
                _lastShieldMaxAlphaAt0733 = Mathf.Max(hasOfficial ? officialMaxAlpha : 0, (cutsceneParticles > 0) ? cutsceneMaxAlpha : 0);
            }

            _summary.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0} {1} elapsed={2:F3} officialShields={3} officialVisible={4} officialCenterY={5:F3} officialScale={6:F3} officialMaxAlpha={7} officialAlphaSum={8} handed={9} cutsceneParticles={10} cutsceneMaxAlpha={11} handover={12}",
                scenario, label, Now() - START_SERVER, CountOfficialShields(), VisibleOfficialParticles(),
                hasOfficial ? center.y - _boss.transform.position.y : -1f, hasOfficial ? size : -1f, hasOfficial ? officialMaxAlpha : -1, hasOfficial ? officialAlphaSum : -1,
                _player.HandedShield != null, cutsceneParticles, cutsceneMaxAlpha, _player.LastShieldHandover));
        }

        // 장비 밖 루트의 정식 보호막 인스턴스(인계 전에는 색 결계, 인계 뒤에는 컷신이 가진 같은 인스턴스)
        private static GameObject OfficialShield()
        {
            foreach (ParticleSystem system in Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude))
            {
                if ((system.transform.parent == null) && system.gameObject.name.StartsWith(_shieldPrefabName))
                {
                    return system.gameObject;
                }
            }

            return null;
        }

        // 보스 기준 높이, 월드 크기, 살아 있는 파티클의 최대 알파와 알파 합(밝기 대용)
        private static bool OfficialShieldStats(out Vector3 center, out float size, out int maxAlpha, out long alphaSum)
        {
            center = Vector3.zero;
            size = 0f;
            maxAlpha = 0;
            alphaSum = 0;
            GameObject shield = OfficialShield();
            if (shield == null)
            {
                return false;
            }

            center = shield.transform.position;
            size = shield.transform.lossyScale.y;
            ParticleSystem.Particle[] buffer = new ParticleSystem.Particle[1024];
            foreach (ParticleSystem system in shield.GetComponentsInChildren<ParticleSystem>(false))
            {
                int alive = system.GetParticles(buffer);
                for (int i = 0; i < alive; ++i)
                {
                    maxAlpha = Mathf.Max(maxAlpha, buffer[i].startColor.a);
                    alphaSum += buffer[i].startColor.a;
                }
            }

            return true;
        }

        // 같은 고정 카메라(CM_01 위치)로 인계 0.25초 전부터 0.8초까지 매 프레임 그리고 보호막 상태를 기록한다.
        // 프레임 사이 위치, 크기, 밝기 변화와 화면 차이를 오프라인으로 비교해 인계 프레임이 튀지 않는지 본다
        private static IEnumerator ShieldContinuity(Vector3 place, float yaw, string name, double delay, int combat)
        {
            yield return PrepareWard(place, yaw, combat);
            AimFixedCamera();
            while (Now() < START_SERVER + delay - 0.25d)
            {
                yield return null;
            }

            bool succeeded = false;
            bool received = false;
            int frame = 0;
            int maxShields = 0;
            bool emptyBeforeFade = false;
            bool jumped = false;
            Vector3 lastCenter = Vector3.zero;
            float lastSize = 0f;
            bool hadLast = false;
            GameObject first = null;
            while (Now() < START_SERVER + 0.8d)
            {
                double now = Now();
                string evt = "";
                if ((!succeeded) && (now >= START_SERVER))
                {
                    _ward.Succeeded(1);
                    succeeded = true;
                    evt += "success;";
                }

                if ((!received) && (now >= START_SERVER + delay))
                {
                    WolbaekPhase2TransitionCue cue = Cue(combat, START_SERVER);
                    _player.Receive(in cue, now);
                    received = true;
                    evt += "handover;";
                }

                bool hasOfficial = OfficialShieldStats(out Vector3 center, out float size, out int maxAlpha, out long alphaSum);
                int own = CountCutsceneShieldParticles();
                int shields = CountOfficialShields() + ((own > 0) ? 1 : 0);
                maxShields = Mathf.Max(maxShields, shields);
                double t = now - START_SERVER;
                if ((shields == 0) && (t < 0.7d - 0.06d))
                {
                    emptyBeforeFade = true;
                }

                if (hasOfficial)
                {
                    first = first ?? OfficialShield();
                    if (hadLast && (((center - lastCenter).magnitude > 0.001f) || (Mathf.Abs(size - lastSize) > 0.001f)))
                    {
                        jumped = true;
                    }

                    lastCenter = center;
                    lastSize = size;
                    hadLast = true;
                }

                _summary.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0} frame={1:000} t={2:F3} event={3} shields={4} sameInstance={5} centerY={6:F3} scale={7:F3} maxAlpha={8} alphaSum={9} ownParticles={10} handed={11}",
                    name, frame, t, (evt.Length > 0) ? evt : "-", shields, hasOfficial && ReferenceEquals(first, OfficialShield()),
                    hasOfficial ? center.y - _boss.transform.position.y : -1f, hasOfficial ? size : -1f, hasOfficial ? maxAlpha : -1, hasOfficial ? alphaSum : -1,
                    own, _player.HandedShield != null));
                CaptureFixed($"{name}/seq_{frame:000}");
                ++frame;
                yield return null;
            }

            Expect(name, $"never two shields (max {maxShields})", maxShields <= 1);
            Expect(name, "no empty frame before fade end", !emptyBeforeFade);
            Expect(name, "position and size never jump", !jumped);
            Expect(name, "gone after 0.75", (CountOfficialShields() == 0) && (_player.HandedShield == null) && (CountCutsceneShieldParticles() == 0));
            _player.Stop(EWolbaekPhase2TransitionStop.Disabled);
            _ward.Clear();
            yield return null;
        }

        private static void AimFixedCamera()
        {
            Transform hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            float scale = _animator.transform.lossyScale.y;
            Vector3 position = _boss.transform.position + (_boss.transform.rotation * (G_Cm01Local * scale));
            _fixedCamera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(hips.position - position));
            _fixedCamera.fieldOfView = CM01_FOV;
        }

        private static void CaptureFixed(string relative)
        {
            string path = Path.Combine(_dir, relative + ".jpg");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            PreviewStage.RenderInto(_fixedCamera, _frame, 0, 0, WIDTH, HEIGHT, DEPTH_BITS);
            File.WriteAllBytes(path, _frame.EncodeToJPG(JPG_QUALITY));
        }

        // 장비 밖 루트에 있는 정식 보호막 인스턴스 수, 비활성은 세지 않는다
        private static int CountOfficialShields()
        {
            int count = 0;
            foreach (ParticleSystem system in Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude))
            {
                if ((system.transform.parent == null) && system.gameObject.name.StartsWith(_shieldPrefabName))
                {
                    ++count;
                }
            }

            return count;
        }

        private static int VisibleOfficialParticles()
        {
            int count = 0;
            ParticleSystem.Particle[] buffer = new ParticleSystem.Particle[512];
            foreach (ParticleSystem root in Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude))
            {
                if ((root.transform.parent != null) || (!root.gameObject.name.StartsWith(_shieldPrefabName)))
                {
                    continue;
                }

                foreach (ParticleSystem system in root.GetComponentsInChildren<ParticleSystem>(false))
                {
                    int alive = system.GetParticles(buffer);
                    for (int i = 0; i < alive; ++i)
                    {
                        if (buffer[i].startColor.a > 0)
                        {
                            ++count;
                        }
                    }
                }
            }

            return count;
        }

        private static int CutsceneShieldMaxAlpha()
        {
            Transform root = CutsceneShieldRoot();
            if (root == null)
            {
                return -1;
            }

            int max = 0;
            ParticleSystem.Particle[] buffer = new ParticleSystem.Particle[512];
            foreach (ParticleSystem system in root.GetComponentsInChildren<ParticleSystem>(false))
            {
                int alive = system.GetParticles(buffer);
                for (int i = 0; i < alive; ++i)
                {
                    max = Mathf.Max(max, buffer[i].startColor.a);
                }
            }

            return max;
        }

        private static Transform CutsceneShieldRoot()
        {
            WolbaekPhase2TransitionRig rig = _player.ActiveRig;
            return (rig != null) ? rig.transform.Find("ShieldRoot (보호막 크기 조절)/ShieldFadeRoot (보호막 인스턴스만, 투명도 트랙 바인딩)") : null;
        }

        private static object GetPrivate(object target, string field)
        {
            return target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
        }

        /****************************************
        *            Scenario Helpers
        ****************************************/

        private static IEnumerator StartFresh(string name, int combat, double until)
        {
            _player.ResetSession(combat);
            SetClock(START_SERVER, 1d);
            WolbaekPhase2TransitionCue cue = new WolbaekPhase2TransitionCue(combat, 1, START_SERVER, START_SERVER + WINDOW);
            Expect(name, "decision play", _player.Receive(in cue, Now()) == EWolbaekPhase2TransitionDecision.Play);
            while (Now() < START_SERVER + until)
            {
                yield return null;
            }

            Record(name, Now());
        }

        // 중단 호출 직후 같은 프레임에 장비, 제어권, 모델 자세가 정리되고 다음 프레임에 VFX 인스턴스가 남지 않는지 본다
        private static IEnumerator InterruptChecks(string name, Action interrupt, EWolbaekPhase2TransitionStop expected)
        {
            WolbaekPhase2TransitionRig rig = _player.ActiveRig;
            Expect(name, "was playing", _player.IsPlaying && (rig != null) && _view.IsExternallyControlled);
            Vector3 lifted = _animator.transform.localPosition;
            interrupt();
            Expect(name, "stopped now", !_player.IsPlaying);
            Expect(name, "stop reason " + expected, _player.LastStop == expected);
            Expect(name, "control released", !_view.IsExternallyControlled);
            Expect(name, "model local pose restored", _animator.transform.localPosition.sqrMagnitude < 1e-8f);
            Expect(name, "no lingering rig", _player.ActiveRig == null);
            _summary.AppendLine($"{name} modelLocalBefore={Fmt(lifted)} after={Fmt(_animator.transform.localPosition)}");
            yield return null;
            Expect(name, "rig destroyed", rig == null);
            Expect(name, "no leftover vfx", CountCutsceneVfx() == 0);
            Record(name, Now());
            for (int i = 0; i < 3; ++i)
            {
                yield return null;
            }

            Expect(name, "camera override released", !(_brain.ActiveVirtualCamera is CinemachineCamera live) || (live == _follow) || (_brain.ActiveBlend != null));

            // 브레인 기본 섞기가 끝난 뒤 전투 카메라가 다시 화면을 잡는지 본다
            double until = Now() + BLEND_TAIL;
            while (Now() < until)
            {
                yield return null;
            }

            Expect(name, "back to combat camera", ReferenceEquals(_brain.ActiveVirtualCamera, _follow) && (_brain.ActiveBlend == null));
            Record(name, Now());
            Capture($"{name}/after_return");
        }

        // 제어권을 돌려받은 뒤 정상 재생 속도이고, 처치가 아니면 Idle 상태로 돌아왔는지 본다
        private static void ExpectMotionRestored(string name, bool expectIdle)
        {
            Expect(name, "animator speed 1", Mathf.Approximately(_animator.speed, 1f));
            Expect(name, "view not controlled", !_view.IsExternallyControlled);
            if (!expectIdle)
            {
                return;
            }

            AnimatorStateInfo state = _animator.GetCurrentAnimatorStateInfo(0);
            AnimatorStateInfo next = _animator.GetNextAnimatorStateInfo(0);
            Expect(name, "animator back to Idle", state.IsName("Idle") || next.IsName("Idle"));
        }

        private static IEnumerator WaitRelease()
        {
            SetClock(Now(), 1d);
            double until = Now() + BLEND_TAIL;
            while (Now() < until)
            {
                yield return null;
            }
        }

        /****************************************
        *                 Setup
        ****************************************/

        private static void Setup()
        {
            _failures = 0;
            _rigIds.Clear();
            _csv = new StringBuilder();
            _summary = new StringBuilder();
            _csv.AppendLine("scenario,frame,serverNow,elapsed,state,playback,lastStop,controlled,rigScale,liveCamera,blend,vfx,headLX,headLY,headLZ,hipsLX,hipsLY,hipsLZ,headVX,headVY,modelLY,rigYawDelta,rigPosDelta,occlusionOn,occlusionCamDelta");
            _dir = Path.GetDirectoryName(WolbaekPhase2TransitionAssets.RecordPath(FOLDER + "/.keep"));
            foreach (string old in Directory.GetFiles(_dir, "*.jpg", SearchOption.AllDirectories))
            {
                File.Delete(old);
            }

            Time.captureFramerate = CAPTURE_RATE;
            CinemachineCore.UniformDeltaTimeOverride = 1f / CAPTURE_RATE;
            _frame = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGB24, false);

            IsolateFromOpenScene();
            CreateStage();

            GameObject cameraPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CAMERA_PREFAB);
            GameObject bossPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BOSS_PREFAB);
            GameObject rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WolbaekPhase2TransitionBuilder.CombatRigPrefabPath);
            if ((cameraPrefab == null) || (bossPrefab == null) || (rigPrefab == null))
            {
                Fail("프리팹 없음 camera=" + (cameraPrefab != null) + " boss=" + (bossPrefab != null) + " rig=" + (rigPrefab != null));
                return;
            }

            GameObject cameraRoot = Object.Instantiate(cameraPrefab);
            _brain = cameraRoot.GetComponentInChildren<CinemachineBrain>(true);
            _camera = _brain.GetComponent<Camera>();
            _follow = cameraRoot.GetComponentInChildren<CinemachineCamera>(true);

            _boss = Object.Instantiate(bossPrefab);
            _view = _boss.GetComponent<BossView>();
            _ward = _boss.GetComponent<WolbaekColorWardView>();
            _animator = _view.ModelAnimator;
            _view.Bind(null, Now);
            _player = _boss.AddComponent<WolbaekPhase2TransitionPlayer>();
            SetPrivate(_player, "_rigPrefab", rigPrefab.GetComponent<WolbaekPhase2TransitionRig>());
            _player.SetServerClock(Now);
            _player.Started += cue => ++_startedCount;
            _player.Stopped += reason => ++_stoppedCount;

            // 정식 바인더를 같은 보스에 붙여 복원 입력과 연결 해제 경로를 쓴다. 복제 상태가 없으므로 Update는 아무것도 하지 않는다
            _binder = _boss.AddComponent<BossNetworkViewBinder>();
            SetPrivate(_binder, "_view", _view);
            SetPrivate(_binder, "_phase2Transition", _player);

            _fixedCamera = new GameObject("CombatCheck Fixed Camera").AddComponent<Camera>();
            _fixedCamera.enabled = false;
            _fixedCamera.nearClipPlane = 0.05f;
            _fixedCamera.farClipPlane = 300f;
            GameObject shieldPrefab = (GameObject)typeof(WolbaekColorWardView).GetField("_shieldPrefab", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_ward);
            _shieldPrefabName = (shieldPrefab != null) ? shieldPrefab.name : "none";

            _dummyPlayer = new GameObject("CombatCheck Player Anchor").transform;
            _follow.Target.TrackingTarget = _dummyPlayer;
            Place(G_PlaceA, YAW_A);

            _summary.AppendLine($"brain={_brain.name} blend={_brain.DefaultBlend.Style}/{_brain.DefaultBlend.Time} follow={_follow.name} priority={_follow.Priority.Value}");
            MeasureModelSizes();
            _summary.AppendLine($"bossAnimator={_animator.name} lossyScale={_animator.transform.lossyScale.y:F3} controller={(_animator.runtimeAnimatorController != null ? _animator.runtimeAnimatorController.name : "none")}");
        }

        // 열린 씬의 카메라, 브레인, 가상 카메라가 이 검증의 브레인 선택과 화면에 섞이지 않게 플레이 모드 동안만 끈다
        private static void IsolateFromOpenScene()
        {
            int count = 0;
            foreach (CinemachineBrain brain in Object.FindObjectsByType<CinemachineBrain>(FindObjectsInactive.Exclude))
            {
                brain.enabled = false;
                ++count;
            }

            foreach (Camera camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude))
            {
                camera.enabled = false;
                ++count;
            }

            foreach (CinemachineVirtualCameraBase virtualCamera in Object.FindObjectsByType<CinemachineVirtualCameraBase>(FindObjectsInactive.Exclude))
            {
                virtualCamera.enabled = false;
                ++count;
            }

            _summary.AppendLine($"isolated components in open scene={count}");
        }

        private static void CreateStage()
        {
            GameObject lightObject = new GameObject("CombatCheck Light");
            lightObject.transform.rotation = Quaternion.Euler(G_LightEuler);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = LIGHT_INTENSITY;
            light.shadows = LightShadows.Soft;

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "CombatCheck Ground";
            ground.transform.position = (G_PlaceA + G_PlaceB) * 0.5f;
            ground.transform.localScale = Vector3.one * GROUND_SCALE;
            Material material = AssetDatabase.LoadAssetAtPath<Material>(GROUND_MATERIAL);
            if (material != null)
            {
                ground.GetComponent<MeshRenderer>().sharedMaterial = material;
            }
        }

        private static void Place(Vector3 position, float yaw)
        {
            Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            _boss.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward));
            _view.SetPose(position, forward);
            _view.SnapToServerPosition();
            _dummyPlayer.position = position + (forward * PLAYER_DISTANCE);
        }

        /****************************************
        *              Measurement
        ****************************************/

        // 피벗 크기만 믿지 않고 실제 메시 크기를 잰다. 정식 보스와 시안 모델 프리팹(같은 GUID)을 각각 바인드 자세 메시 경계로 재고,
        // 휴머노이드 크기(humanScale x 월드 크기)도 함께 남긴다. 장비 비율(AppliedScale)은 재생 중 기록과 비교한다
        private static void MeasureModelSizes()
        {
            GameObject modelPrefab = WolbaekPhase2TransitionAssets.LoadByGuid<GameObject>(WolbaekPhase2TransitionAssets.MODEL_PREFAB_GUID);
            if (modelPrefab == null)
            {
                Fail("시안 모델 프리팹 없음");
                return;
            }

            GameObject preview = Object.Instantiate(modelPrefab, G_PlaceA + (Vector3.right * 40f), Quaternion.identity);
            Animator previewAnimator = preview.GetComponentInChildren<Animator>(true);
            float officialHeight = BindHeight(_boss);
            float previewHeight = BindHeight(preview);
            float officialHuman = _animator.humanScale * _animator.transform.lossyScale.y;
            float previewHuman = (previewAnimator != null) ? previewAnimator.humanScale * previewAnimator.transform.lossyScale.y : 0f;
            _summary.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "modelSize officialMeshHeight={0:F3} previewMeshHeight={1:F3} meshRatio={2:F3} officialHuman={3:F3} previewHuman={4:F3} humanRatio={5:F3} officialAnimatorScale={6:F3} previewAnimatorScale={7:F3}",
                officialHeight, previewHeight, (previewHeight > 0f) ? officialHeight / previewHeight : 0f, officialHuman, previewHuman,
                (previewHuman > 0f) ? officialHuman / previewHuman : 0f, _animator.transform.lossyScale.y,
                (previewAnimator != null) ? previewAnimator.transform.lossyScale.y : 0f));
            Object.Destroy(preview);
        }

        private static float BindHeight(GameObject root)
        {
            float min = float.MaxValue;
            float max = float.MinValue;
            foreach (SkinnedMeshRenderer skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin.sharedMesh == null)
                {
                    continue;
                }

                Bounds bounds = skin.sharedMesh.bounds;
                Matrix4x4 matrix = skin.transform.localToWorldMatrix;
                for (int i = 0; i < 8; ++i)
                {
                    Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(((i & 1) == 0) ? -1f : 1f, ((i & 2) == 0) ? -1f : 1f, ((i & 4) == 0) ? -1f : 1f));
                    float y = matrix.MultiplyPoint3x4(corner).y;
                    min = Mathf.Min(min, y);
                    max = Mathf.Max(max, y);
                }
            }

            return (max > min) ? max - min : 0f;
        }

        private static void TrackRig()
        {
            if (_player.ActiveRig != null)
            {
                _rigIds.Add(_player.ActiveRig);
            }
        }

        private static void Record(string scenario, double now)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            WolbaekPhase2TransitionRig rig = _player.ActiveRig;
            Transform head = _animator.GetBoneTransform(HumanBodyBones.Head);
            Transform hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            Vector3 headLocal = (rig != null) ? rig.transform.InverseTransformPoint(head.position) : Vector3.zero;
            Vector3 hipsLocal = (rig != null) ? rig.transform.InverseTransformPoint(hips.position) : Vector3.zero;
            _camera.aspect = (float)WIDTH / HEIGHT;
            Vector3 headView = _camera.WorldToViewportPoint(head.position);
            float yawDelta = (rig != null) ? Quaternion.Angle(rig.transform.rotation, _boss.transform.rotation) : -1f;
            float posDelta = (rig != null) ? Vector3.Distance(rig.transform.position, _boss.transform.position) : -1f;
            string state = _player.IsPlaying ? "playing" : (_player.IsPending ? "pending" : "idle");
            ICinemachineCamera live = _brain.ActiveVirtualCamera;

            // 기존 가림 투명화(OcclusionFadeDriver)가 마지막으로 쓴 전역값, 컷신 카메라 위치로 플레이어 쪽 가림을 계산하는지 본다
            Vector4 occlusion = Shader.GetGlobalVector(OCCLUSION_CAMERA_ID);
            float occlusionDelta = Vector3.Distance(new Vector3(occlusion.x, occlusion.y, occlusion.z), _camera.transform.position);
            _csv.AppendLine(string.Format(c,
                "{0},{1},{2:F3},{3:F3},{4},{5:F3},{6},{7},{8:F3},{9},{10},{11},{12:F4},{13:F4},{14:F4},{15:F4},{16:F4},{17:F4},{18:F4},{19:F4},{20:F4},{21:F2},{22:F4},{23:F0},{24:F3}",
                scenario, Time.frameCount, now, now - START_SERVER, state, _player.PlaybackTime, _player.LastStop, _view.IsExternallyControlled ? 1 : 0,
                (rig != null) ? rig.AppliedScale : 0f, (live != null) ? live.Name : "none", (_brain.ActiveBlend != null) ? 1 : 0, CountCutsceneVfx(),
                headLocal.x, headLocal.y, headLocal.z, hipsLocal.x, hipsLocal.y, hipsLocal.z, headView.x, headView.y,
                _animator.transform.localPosition.y, yawDelta, posDelta, occlusion.w, occlusionDelta));
        }

        private static int CountCutsceneVfx()
        {
            WolbaekPhase2TransitionRig rig = _player.ActiveRig;
            if (rig == null)
            {
                return CountOrphanCutsceneVfx();
            }

            int count = 0;
            foreach (ParticleSystem system in rig.GetComponentsInChildren<ParticleSystem>(false))
            {
                if ((system.transform.parent != null) && (system.transform.parent.GetComponent<ParticleSystem>() == null))
                {
                    ++count;
                }
            }

            return count + CountOrphanCutsceneVfx();
        }

        // 컷신 VFX 원본 이름을 가진 인스턴스가 장비 밖에 남았는지 센다
        private static int CountOrphanCutsceneVfx()
        {
            int count = 0;
            foreach (ParticleSystem system in Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude))
            {
                if (system.transform.parent != null)
                {
                    continue;
                }

                string objectName = system.gameObject.name;
                if (objectName.StartsWith("SB_Rune_Fall") || objectName.StartsWith("SB_Vortex") || objectName.StartsWith("SB_Shockwave"))
                {
                    ++count;
                }
            }

            return count;
        }

        private static int CountOfficialShieldParticles()
        {
            int count = 0;
            foreach (WolbaekShieldFade fade in Object.FindObjectsByType<WolbaekShieldFade>(FindObjectsInactive.Exclude))
            {
                foreach (ParticleSystem system in fade.GetComponentsInChildren<ParticleSystem>(false))
                {
                    count += system.particleCount;
                }
            }

            return count;
        }

        private static int CountCutsceneShieldParticles()
        {
            WolbaekPhase2TransitionRig rig = _player.ActiveRig;
            if (rig == null)
            {
                return 0;
            }

            int count = 0;
            Transform shieldRoot = rig.transform.Find("ShieldRoot (보호막 크기 조절)/ShieldFadeRoot (보호막 인스턴스만, 투명도 트랙 바인딩)");
            if (shieldRoot == null)
            {
                return -1;
            }

            foreach (ParticleSystem system in shieldRoot.GetComponentsInChildren<ParticleSystem>(false))
            {
                count += system.particleCount;
            }

            return count;
        }

        private static void Capture(string relative)
        {
            string path = Path.Combine(_dir, relative + ".jpg");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            PreviewStage.RenderInto(_camera, _frame, 0, 0, WIDTH, HEIGHT, DEPTH_BITS);
            File.WriteAllBytes(path, _frame.EncodeToJPG(JPG_QUALITY));
        }

        /****************************************
        *                 Utility
        ****************************************/

        private static double Now()
        {
            return _clockBase + ((Time.timeAsDouble - _clockAnchor) * _clockSpeed);
        }

        private static void SetClock(double serverNow, double speed)
        {
            _clockBase = serverNow;
            _clockAnchor = Time.timeAsDouble;
            _clockSpeed = speed;
        }

        private static void Expect(string scenario, string check, bool isPassed)
        {
            string line = $"{(isPassed ? "PASS" : "FAIL")} {scenario}: {check}";
            if (_summary != null)
            {
                _summary.AppendLine(line);
            }

            if (!isPassed)
            {
                ++_failures;
                Debug.LogWarning("[P2Combat] " + line);
            }
        }

        private static void Fail(string message)
        {
            ++_failures;
            if (_summary != null)
            {
                _summary.AppendLine("FAIL setup: " + message);
            }

            Debug.LogError("[P2Combat] " + message);
        }

        private static void SetPrivate(object target, string field, object value)
        {
            FieldInfo info = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            info.SetValue(target, value);
        }

        private static string Fmt(Vector3 value)
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:F4},{1:F4},{2:F4})", value.x, value.y, value.z);
        }
    }
}
