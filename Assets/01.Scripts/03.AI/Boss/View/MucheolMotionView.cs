// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.AI
{
    // 무철 표현 프리팹(PF_MucheolBoss)의 몸 동작을 서버 사건으로 재생한다. 사건을 받아 이 컴포넌트를 부르는 일은 네트워크 수신부가 한다.
    // 시각은 모두 서버 시각이다. 로컬 타이머로 판정, 기믹 완료, 그로기 시작을 정하지 않는다.
    // 위치와 방향, 돌진 거리는 서버 권위 흐름(BossView.SetPose)이 맡고 이 컴포넌트는 Animator 상태와 재생 속도만 바꾼다.
    // 루트 모션을 쓰지 않으므로 Animator의 Apply Root Motion은 꺼 둔다
    /// <summary> 무철 패턴 모션 표현, 서버 시전 사건을 컨트롤러 상태 재생으로 바꾼다 </summary>
    [DisallowMultipleComponent]
    public sealed class MucheolMotionView : ClientView
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int   NO_CAST            = -1;
        private const int   NO_DASH            = -1;
        private const float EARLY_STOP_EPSILON = 0.05f; // m, 정의 거리보다 이만큼 짧아야 일찍 멈춘 돌진으로 본다

        // 초, 서버 정지 시각과 미리 잡은 정지 시각이 서버 한 틱 안이면 같은 정지로 본다. 다시 끊으면 섞기가 한 번 더 보인다
        private const double STOP_TIME_TOLERANCE = 1.0 / Constants.Network.SERVER_TICK_RATE;

        private const float SHIFT_SMOOTH_SECONDS = 0.08f; // 초, 재생 중 모델 이동이 튀지 않게 따라가는 시간
        private const float SHIFT_RETURN_SECONDS = 0.5f;  // 초, 모션이 끝나 Idle로 돌아갈 때 원위치로 천천히 돌아오는 시간
        private const float SERVER_MOVE_DISTANCE = 0.15f; // m, 클립 재생 중 보스 위치가 이만큼 움직이면 서버가 옮기는 것으로 본다

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private Animator _animator; // 무철 모델의 휴머노이드 Animator, AC_Mucheol 컨트롤러를 가진다
        [SerializeField] private bool     _applyBodyShift = true; // 발 위치에 맞춰 모델을 옮긴다, 끄면 모델은 보스 위치에 고정된다

        private readonly List<MucheolMotionTrack> _tracks  = new List<MucheolMotionTrack>();
        private readonly List<double>             _strikes = new List<double>();

        private MucheolMotionDirector _director;
        private Func<double>          _serverClock;
        private int                   _dashCastId = NO_CAST;
        private int                   _dashCount;
        private int                   _runIndex   = NO_DASH;
        private MucheolMotionTrack    _runTrack;
        private double                _stopAt     = double.NaN;
        private Vector3               _modelBasePosition;
        private Vector2               _shift;
        private Vector2               _shiftVelocity;
        private string                _shiftClip;
        private Vector3               _clipStartPosition;
        private bool                  _isServerDriven;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 재생기, 시험과 진단용이다 </summary>
        public MucheolMotionDirector Director => _director;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            if ((_animator == null) || (_animator.runtimeAnimatorController == null))
            {
                Debug.LogError("[MucheolMotionView] 애니메이터나 컨트롤러가 비어 있다", this);
                enabled = false;
                return;
            }

            _animator.applyRootMotion = false;
            _director          = new MucheolMotionDirector(_animator);
            _modelBasePosition = _animator.transform.localPosition;
        }

        private void Update()
        {
            _director.Tick(ReadClock());
            ApplyBodyShift();
        }

        // 비활성화된 동안 받은 사건은 없으므로 다시 켜질 때 남은 속도나 정지 자세가 이어지지 않게 비운다
        private void OnDisable()
        {
            ClearDashTracking();
            if (_director != null)
            {
                _director.Reset(true);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 서버 시각을 읽는 함수, 비우면 로컬 시각이다 </summary>
        public void SetServerClock(Func<double> serverClock)
        {
            _serverClock = serverClock;
        }

        /// <summary> 이 패턴의 몸 동작을 이 표현이 맡는다 </summary>
        public static bool Handles(string patternId)
        {
            return MucheolMotionCatalog.Handles(patternId);
        }

        // 부채꼴, 원형 단발과 돌진(1페이즈 P01~P04, P06)의 예고 사건이다. 돌진은 예고 끝에서 멈춰 BeginDash를 기다린다
        /// <summary> 기본 예고 시작, 늦게 받아도 서버 시작 시각 기준으로 이어서 재생한다 </summary>
        public void BeginTelegraph(int castId, string patternId, double startServerTime, float telegraphSeconds)
        {
            if (_director == null)
            {
                return;
            }

            MucheolMotionAnchors anchors = MucheolMotionAnchors.Unknown();
            anchors.CastStart    = startServerTime;
            anchors.TelegraphEnd = startServerTime + telegraphSeconds;

            if (MucheolMotionCatalog.IsDash(patternId))
            {
                _dashCastId = castId;
                _dashCount  = 1;
                PlayDashWindup(castId, in anchors, MucheolMotionCatalog.DashWindup);
                return;
            }

            if (MucheolMotionCatalog.TryGetStrike(patternId, out MucheolMotionSegment[] segments))
            {
                PlayStrikes(castId, segments, in anchors);
            }
        }

        // distance는 서버가 돌진 시작 순간 잰 막힘 없는 거리다. 정의 거리보다 짧으면 그 거리에 닿는 시각(시작 + 거리 / 속도)에 멈추는 것으로 미리 잡는다.
        // 이 시각은 정지 사건이 늦게 와도 벽을 지나 달리지 않게 하는 예정 시각이고, 정지 사건의 서버 시각이 오면 그 시각으로 고친다
        /// <summary> 기본 돌진 시작, 서버가 정한 시작 시각과 돌진 시간으로 들이받는 구간을 잇는다 </summary>
        public void BeginDash(int castId, float distance, float seconds, float speed, double startServerTime)
        {
            AppendDashRun(castId, 0, distance, seconds, speed, startServerTime, false);
        }

        // stopServerTime은 서버가 정지를 판정한 시각이고, 그 시각부터 들이받는 구간을 끝 자세로 몰아 마친다. 미리 잡은 정지와 한 틱 넘게 다르면
        // 서버 시각으로 다시 끊는다. 시각이 없으면(NaN) 받은 순간(로컬에서 읽은 서버 시계 값)을 쓰고 이미 잡은 정지가 지났으면 그대로 둔다.
        // 우선순위는 그로기, 충돌 반동, 정지 순이라 반동이나 그로기가 몸을 쓰는 중이면 받지 않는다. 지난 시전, 다른 돌진 순번, 예정 끝 이후의 정지는 무시한다
        /// <summary> 서버 돌진 정지 사건을 받았다, 아직 달리는 중이면 제자리 달리기 없이 끝 자세로 마친다 </summary>
        public void StopDash(int castId, int dashIndex, double stopServerTime)
        {
            if ((_director == null) || (castId != _dashCastId) || (dashIndex != _runIndex) || (_runTrack == null))
            {
                return;
            }

            bool   hasServerTime = !double.IsNaN(stopServerTime);
            double at            = hasServerTime ? stopServerTime : ReadClock();
            bool   isStopped     = !double.IsNaN(_stopAt);
            if (at >= _runTrack.EndTime)
            {
                return;
            }

            if (isStopped && ((!hasServerTime) ? (at >= _stopAt) : (Math.Abs(at - _stopAt) <= STOP_TIME_TOLERANCE)))
            {
                return;
            }

            ApplyStop(castId, at);
        }

        /// <summary> 콤보(1페이즈 P05)와 순차 타격(2페이즈 P01, P02, P04, P05), 타 확정 시각마다 타격 키를 맞춘다 </summary>
        public void BeginStrikeChain(in BossStrikeChainStart start)
        {
            if ((_director == null) || (!MucheolMotionCatalog.TryGetStrike(start.PatternId, out MucheolMotionSegment[] segments)))
            {
                return;
            }

            double executeAt = start.StartServerTime + start.TelegraphSeconds;
            _strikes.Clear();
            IReadOnlyList<float> delays = start.DelaySeconds;
            for (int i = 0; i < start.StepCount; ++i)
            {
                _strikes.Add(executeAt + delays[i]);
            }

            MucheolMotionAnchors anchors = MucheolMotionAnchors.Unknown();
            anchors.CastStart    = start.StartServerTime;
            anchors.TelegraphEnd = executeAt;
            anchors.Strikes      = _strikes;
            PlayStrikes(start.CastId, segments, in anchors);
        }

        /// <summary> 연속 돌진(2페이즈 P03, P06)의 첫 돌진 예고 </summary>
        public void BeginMultiDash(in BossMultiDashStart start)
        {
            if ((_director == null) || (!MucheolMotionCatalog.IsDash(start.PatternId)))
            {
                return;
            }

            _dashCastId = start.CastId;
            _dashCount  = start.DashCount;

            MucheolMotionAnchors anchors = MucheolMotionAnchors.Unknown();
            anchors.CastStart    = start.StartServerTime;
            anchors.TelegraphEnd = start.StartServerTime + start.TelegraphSeconds;
            PlayDashWindup(start.CastId, in anchors, MucheolMotionCatalog.DashWindup);
        }

        /// <summary> 연속 돌진의 dashIndex번째 돌진 시작 </summary>
        public void BeginMultiDashRun(int castId, int dashIndex, float distance, float seconds, float speed, double startServerTime)
        {
            bool hasNext = (dashIndex + 1) < _dashCount;
            AppendDashRun(castId, dashIndex, distance, seconds, speed, startServerTime, hasNext);
        }

        /// <summary> 연속 돌진의 다음 돌진 재조준, 같은 클립을 모으는 자세부터 다시 시작한다 </summary>
        public void BeginMultiDashReaim(in BossMultiDashReaim reaim)
        {
            if ((_director == null) || (reaim.CastId != _dashCastId))
            {
                return;
            }

            MucheolMotionAnchors anchors = MucheolMotionAnchors.Unknown();
            anchors.CastStart    = reaim.StartServerTime;
            anchors.TelegraphEnd = reaim.StartServerTime + reaim.ReaimSeconds;
            MucheolMotionTrack track = MucheolMotionPlan.Resolve(MucheolMotionCatalog.DashReaim, in anchors);

            // 앞 돌진을 받지 못한 늦은 접속이면 재조준부터 새로 시작한다
            if (!_director.Append(reaim.CastId, track, true, ReadClock()))
            {
                PlayDashWindup(reaim.CastId, in anchors, MucheolMotionCatalog.DashReaim);
            }
        }

        /// <summary> 남은 돌진이 더 오지 않는다(실행 구간 부족), 마지막 돌진이 끝나면 Idle로 돌아간다 </summary>
        public void EndMultiDashEarly(int castId)
        {
            if ((_director == null) || (castId != _dashCastId))
            {
                return;
            }

            _dashCount = 0;
            _director.StopAwaiting(castId, ReadClock());
        }

        /// <summary> 하드 전장 공격(2페이즈 P07), 예고 끝 타격에 내려찍는다 </summary>
        public void BeginBattlefield(in BossBattlefieldStart start)
        {
            if ((_director == null) || (!MucheolMotionCatalog.TryGetStrike(MucheolMotionCatalog.P2_P07_ID, out MucheolMotionSegment[] segments)))
            {
                return;
            }

            MucheolMotionAnchors anchors = MucheolMotionAnchors.Unknown();
            anchors.CastStart    = start.StartServerTime;
            anchors.TelegraphEnd = start.StartServerTime + start.TelegraphSeconds;
            PlayStrikes(start.CastId, segments, in anchors);
        }

        // 그로기로 이어지는 마지막 충돌은 서버의 그로기 대기 사건이 BeginGroggy로 이 반동을 대신한다
        /// <summary> 마지막이 아닌 돌진 충돌의 반동, serverTime은 서버가 충돌 결과를 알린 시각이다 </summary>
        public void PlayChargeImpact(int castId, double serverTime)
        {
            if ((_director == null) || (_director.IsGroggy))
            {
                return;
            }

            MucheolMotionAnchors anchors = MucheolMotionAnchors.Unknown();
            anchors.Event = serverTime;
            _tracks.Clear();
            AddTrack(MucheolMotionCatalog.ChargeImpact, in anchors);
            _director.PlayReaction(castId, _tracks, ReadClock());
            ClearDashTracking();
        }

        // 마지막 충돌부터 그로기 끝까지 낮아진 자세를 유지하고 그로기 끝에 맞춰 일어선다. 서버 상태와 행동 가능 시각은 바꾸지 않는다.
        // 그로기 시작은 충돌한 시전의 회복이 끝난 뒤라, 충돌 순간에는 끝 시각을 모를 수 있다. 그때는 NaN을 넘기고 SetGroggyEnd로 잇는다.
        // 늦게 받으면 내려앉기를 건너뛰고 유지 자세로 바로 간다
        /// <summary> 기믹 1 마지막 충돌의 그로기, collisionServerTime부터 내려앉고 groggyEndServerTime에 다 일어선다. 끝을 모르면 NaN이다 </summary>
        public void BeginGroggy(int castId, double collisionServerTime, double groggyEndServerTime)
        {
            if (_director == null)
            {
                return;
            }

            MucheolMotionAnchors anchors = MucheolMotionAnchors.Unknown();
            anchors.Event     = collisionServerTime;
            anchors.GroggyEnd = groggyEndServerTime;
            bool hasEnd = !double.IsNaN(groggyEndServerTime);

            _tracks.Clear();
            AddTrack(hasEnd ? MucheolMotionCatalog.GroggyDown : MucheolMotionCatalog.GroggyDownOpen, in anchors);
            if (hasEnd)
            {
                AddTrack(MucheolMotionCatalog.GroggyUp, in anchors);
            }

            _director.PlayGroggy(castId, _tracks, !hasEnd, ReadClock());
            ClearDashTracking();
        }

        /// <summary> 끝 시각 없이 시작한 그로기에 서버 그로기 끝 시각을 준다, 그 시각에 다 일어서도록 일어나기를 잇는다 </summary>
        public void SetGroggyEnd(int castId, double groggyEndServerTime)
        {
            if ((_director == null) || (!_director.IsGroggy) || (_director.CastId != castId))
            {
                return;
            }

            MucheolMotionAnchors anchors = MucheolMotionAnchors.Unknown();
            anchors.GroggyEnd = groggyEndServerTime;
            MucheolMotionTrack rise = MucheolMotionPlan.Resolve(MucheolMotionCatalog.GroggyUp, in anchors);
            _director.Append(castId, rise, false, ReadClock());
        }

        /// <summary> 서버가 그로기를 사망과 전투 종료가 아닌 이유로 접었다, 이 시전의 그로기만 끊는다 </summary>
        public void CancelGroggy(int castId)
        {
            if (_director != null)
            {
                _director.CancelGroggy(castId);
            }
        }

        /// <summary> 시전 정상 종료 </summary>
        public void FinishCast(int castId)
        {
            if (_director == null)
            {
                return;
            }

            if (castId == _dashCastId)
            {
                ClearDashTracking();
            }

            _director.Finish(castId);
        }

        /// <summary> 시전 취소 </summary>
        public void CancelCast(int castId)
        {
            if (_director == null)
            {
                return;
            }

            if (castId == _dashCastId)
            {
                ClearDashTracking();
            }

            _director.Cancel(castId);
        }

        /// <summary> 보스 사망, 참이면 공격 모션을 끊고 사망 표현을 덮지 않게 막는다 </summary>
        public void SetDefeated(bool isDefeated)
        {
            if (_director == null)
            {
                return;
            }

            if (isDefeated)
            {
                ClearDashTracking();
            }

            _director.SetBlocked(isDefeated);
        }

        /// <summary> 새 네트워크 관찰이나 전투 재시작 전에 모션과 막힘을 비우고 Idle로 돌린다 </summary>
        public void ResetSession()
        {
            ClearDashTracking();
            if (_director != null)
            {
                _director.Reset(true);
            }
        }

        private void PlayStrikes(int castId, MucheolMotionSegment[] segments, in MucheolMotionAnchors anchors)
        {
            _tracks.Clear();
            for (int i = 0; i < segments.Length; ++i)
            {
                AddTrack(segments[i], in anchors);
            }

            if (_tracks.Count == segments.Length)
            {
                _director.PlayCast(castId, _tracks, false, ReadClock());
            }
        }

        private void PlayDashWindup(int castId, in MucheolMotionAnchors anchors, in MucheolMotionSegment windup)
        {
            _tracks.Clear();
            AddTrack(windup, in anchors);
            _director.PlayCast(castId, _tracks, true, ReadClock());
        }

        private void AppendDashRun(int castId, int dashIndex, float distance, float seconds, float speed, double startServerTime,
            bool hasNext)
        {
            if ((_director == null) || (castId != _dashCastId))
            {
                return;
            }

            MucheolMotionAnchors anchors = MucheolMotionAnchors.Unknown();
            anchors.DashStart = startServerTime;
            anchors.DashEnd   = startServerTime + Mathf.Max(0f, seconds);
            MucheolMotionTrack track = MucheolMotionPlan.Resolve(MucheolMotionCatalog.DashRun, in anchors);
            if (track == null)
            {
                return;
            }

            _runIndex = dashIndex;
            _runTrack = track;
            _stopAt   = double.NaN;

            // 예고를 받지 못한 늦은 접속이면 돌진 구간만 재생한다
            if (!_director.Append(castId, track, hasNext, ReadClock()))
            {
                _tracks.Clear();
                _tracks.Add(track);
                _director.PlayCast(castId, _tracks, hasNext, ReadClock());
            }

            // 정적인 벽에 막히는 돌진은 시작 사건의 거리로 정지 지점을 미리 안다, 정지 사건이 늦게 와도 그 자리에서 멈춘다
            float range = speed * Mathf.Max(0f, seconds);
            if ((speed > 0f) && (distance >= 0f) && (distance < range - EARLY_STOP_EPSILON))
            {
                ApplyStop(castId, startServerTime + (distance / speed));
            }
        }

        // at부터 들이받는 구간을 끝 자세까지 짧게 몰아 마친다. 다음 돌진을 기다리는 연속 돌진은 그 자세에서 재조준을 기다린다.
        // 이미 끊은 돌진이면 원래 들이받는 구간부터 다시 끊는다
        private void ApplyStop(int castId, double at)
        {
            if ((_runTrack == null) || (at <= _runTrack.StartTime) || (at >= _runTrack.EndTime))
            {
                return;
            }

            MucheolMotionPlan.Sample(_runTrack, at, out float clipAt, out _);
            MucheolMotionTrack tail = MucheolMotionPlan.Span(MucheolMotionCatalog.DASH_CLIP, at, clipAt,
                MucheolMotionCatalog.DashRunEndClip, MucheolMotionCatalog.DASH_STOP_SECONDS);
            bool isReplaced = double.IsNaN(_stopAt)
                ? _director.ReplaceFrom(castId, at, tail, ReadClock())
                : _director.ReplaceRunFrom(castId, _runTrack, at, tail, ReadClock());
            if (isReplaced)
            {
                _stopAt = at;
            }
        }

        private void AddTrack(in MucheolMotionSegment segment, in MucheolMotionAnchors anchors)
        {
            MucheolMotionTrack track = MucheolMotionPlan.Resolve(in segment, in anchors);
            if ((track != null) && _director.HasClip(track.ClipName))
            {
                _tracks.Add(track);
            }
        }

        // 서버 위치는 건드리지 않고 모델만 보스 기준으로 옮긴다. 재생 중인 클립이 표에 없거나 Idle이면 0으로 돌아온다
        // 서버가 이 클립 동안 보스를 직접 옮기면(위치가 클립 시작 위치에서 SERVER_MOVE_DISTANCE 넘게 벗어나면) 같은 이동이 두 번 더해지지 않게 그 클립이 끝날 때까지 보정을 끈다
        private void ApplyBodyShift()
        {
            string clip = _director.PlayingClip;
            if (clip != _shiftClip)
            {
                _shiftClip           = clip;
                _clipStartPosition   = transform.position;
                _isServerDriven      = false;
            }
            else if ((clip != null) && (!_isServerDriven))
            {
                Vector3 moved = transform.position - _clipStartPosition;
                _isServerDriven = (new Vector2(moved.x, moved.z).sqrMagnitude > (SERVER_MOVE_DISTANCE * SERVER_MOVE_DISTANCE));
            }

            bool    isPlaying = _applyBodyShift && (clip != null) && (!_isServerDriven);
            Vector2 target    = isPlaying ? MucheolBodyShiftCatalog.Sample(_director.PlayingClip, _director.ClipSeconds) : Vector2.zero;
            _shift = Vector2.SmoothDamp(_shift, target, ref _shiftVelocity, isPlaying ? SHIFT_SMOOTH_SECONDS : SHIFT_RETURN_SECONDS);
            _animator.transform.localPosition = _modelBasePosition + new Vector3(_shift.x, 0f, _shift.y);
        }

        private void ClearDashTracking()
        {
            _dashCastId = NO_CAST;
            _dashCount  = 0;
            _runIndex   = NO_DASH;
            _runTrack   = null;
            _stopAt     = double.NaN;
        }

        private double ReadClock()
        {
            return (_serverClock != null) ? _serverClock() : Time.timeAsDouble;
        }
    }
}
