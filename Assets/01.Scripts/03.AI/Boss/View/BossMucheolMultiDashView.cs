// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 무철 2페이즈 연속 돌진(P03, P06)의 표시다. 돌진마다 방향과 길이가 달라 돌진 하나가 기존 돌진 예고 하나이고, 같은 시전의 돌진은 시전 번호를 묶음으로 달아 서로 지우지 않는다
    // 첫 돌진은 시전 시작에 예고하고, 이후 돌진은 앞 돌진이 선 자리에서 재조준을 시작할 때 예고한다. 돌진의 이동과 정지는 서버가 알린 시작 시각과 속도와 정지 위치로만 그린다
    // 입력은 서버의 IMucheolMultiDashListener 인자에서 클라이언트 안전한 값만 옮긴 것이다. 돌진 중 피해는 서버 판정이라 이 뷰가 그리지 않는다
    // 늦게 접속하면 지금 진행 중인 돌진 하나를 RestoreDash로 복원한다. 복제 계층이 현재 돌진 번호와 그 돌진의 예고, 발동, 정지 값을 들고 있어야 한다
    /// <summary> 연속 돌진의 돌진별 예고, 재조준, 돌진, 정지, 잘림, 취소를 시전 번호별로 관리한다 </summary>
    [DisallowMultipleComponent]
    public sealed class BossMucheolMultiDashView : BossPresenterViewBase
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float NOT_STOPPED = -1f;

        /****************************************
        *                Fields
        ****************************************/

        private sealed class Cast
        {
            public int    BossId;
            public string PatternId;
            public int    DashCount;
            public int    PlannedBeforeCut;
            public int    CompletedBeforeCut;
            public bool   IsTruncated;
        }

        private readonly Dictionary<int, Cast> _casts = new Dictionary<int, Cast>();

        /****************************************
        *              Properties
        ****************************************/

        public int CastCount => _casts.Count;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 첫 돌진의 예고를 시작한다. 이미 있는 시전이거나 취소된 시전이면 무시한다. dashCount는 이 시전이 달릴 돌진 횟수다 </summary>
        public bool Begin(int bossId, int castInstanceId, string patternId, Vector3 origin, Vector3 direction, float length, float width,
            double startServerTime, float telegraphSeconds, int dashCount)
        {
            if (IsServerBuild || _casts.ContainsKey(castInstanceId) || (dashCount < 1) || (PreparePresenter() == null))
            {
                return false;
            }

            _casts.Add(castInstanceId, new Cast { BossId = bossId, PatternId = patternId, DashCount = dashCount });
            TrackGroup(castInstanceId);

            // 취소됐거나 이미 지난 시전이라 첫 예고를 받아들이지 않았으면 기록을 남기지 않는다
            bool isBegun = BeginDashTelegraph(castInstanceId, 0, origin, direction, length, width, startServerTime, telegraphSeconds);
            if (!isBegun)
            {
                _casts.Remove(castInstanceId);
                ForgetCast(castInstanceId);
            }

            return isBegun;
        }

        /// <summary> 조준 방향이 바뀌었다, 방향이 바뀌면 벽까지의 거리도 달라져 길이를 함께 받는다 </summary>
        public bool AimChanged(int castInstanceId, int dashIndex, Vector3 direction, float length, double serverTime)
        {
            if ((IsServerBuild) || (Presenter == null) || (!Keys.TryGet(castInstanceId, dashIndex, out int key)))
            {
                return false;
            }

            return Presenter.UpdateAim(key, direction, length, serverTime);
        }

        /// <summary> dashIndex번째 돌진의 방향이 고정됐다 </summary>
        public bool DirectionLocked(int castInstanceId, int dashIndex, Vector3 direction, float length)
        {
            if ((IsServerBuild) || (Presenter == null) || (!Keys.TryGet(castInstanceId, dashIndex, out int key)))
            {
                return false;
            }

            return Presenter.LockDirection(key, direction, length);
        }

        /// <summary> 앞 돌진이 끝나고 dashIndex번째 돌진의 재조준이 시작됐다, 이 예고는 reaimSeconds 뒤 돌진으로 이어진다 </summary>
        public bool ReaimStarted(int castInstanceId, int dashIndex, Vector3 origin, Vector3 direction, float length, float width,
            double startServerTime, float reaimSeconds)
        {
            if (IsServerBuild || (!_casts.ContainsKey(castInstanceId)))
            {
                return false;
            }

            return BeginDashTelegraph(castInstanceId, dashIndex, origin, direction, length, width, startServerTime, reaimSeconds);
        }

        /// <summary> dashIndex번째 돌진 시작, startServerTime부터 초당 speed로 일정하게 가고 최대 distance에서 멈춘다 </summary>
        public bool DashStarted(int castInstanceId, int dashIndex, float distance, float seconds, float speed, double startServerTime)
        {
            if ((IsServerBuild) || (Presenter == null) || (!Keys.TryGet(castInstanceId, dashIndex, out int key)))
            {
                return false;
            }

            return Presenter.BeginDash(key, distance, seconds, speed, startServerTime);
        }

        /// <summary> dashIndex번째 돌진이 더 이상 가지 않는다, 정해진 거리에 닿았거나 벽에 막혔거나 실행이 끝나 잘렸다 </summary>
        public bool DashStopped(int castInstanceId, int dashIndex, Vector3 position, bool stoppedByWall)
        {
            if ((IsServerBuild) || (Presenter == null) || (!Keys.TryGet(castInstanceId, dashIndex, out int key)))
            {
                return false;
            }

            bool stopped = Presenter.StopDash(key, position, stoppedByWall);

            // 마지막 돌진까지 멈췄으면 이 시전의 기록은 더 필요 없다, 남은 연출은 예고 표현이 마친다
            if ((_casts.TryGetValue(castInstanceId, out Cast cast)) && (dashIndex >= (cast.DashCount - 1)))
            {
                _casts.Remove(castInstanceId);
                ForgetCast(castInstanceId);
            }

            return stopped;
        }

        /// <summary> 실행 구간이 짧아 계획한 돌진을 다 달리지 못하고 잘렸다, 남은 돌진은 나오지 않는다. 표시는 바꾸지 않고 기록만 한다 </summary>
        public bool Truncated(int castInstanceId, int plannedDashCount, int completedDashCount)
        {
            if ((IsServerBuild) || (!_casts.TryGetValue(castInstanceId, out Cast cast)))
            {
                return false;
            }

            cast.IsTruncated        = true;
            cast.PlannedBeforeCut   = plannedDashCount;
            cast.CompletedBeforeCut = completedDashCount;
            return true;
        }

        /// <summary> 중단, 아직 발동하지 않은 돌진 예고를 모두 지운다. 달리는 중인 돌진은 서버가 알린 정지 위치에서 멈춘다 </summary>
        public bool Cancel(int castInstanceId)
        {
            if (IsServerBuild)
            {
                return false;
            }

            _casts.Remove(castInstanceId);
            return CancelCast(castInstanceId);
        }

        /// <summary> 지금 진행 중인 돌진 하나를 복원한다. 처음부터 다시 재생하지 않고 서버 시각 기준으로 이어서 그린다 </summary>
        public bool RestoreDash(int bossId, int castInstanceId, string patternId, int dashIndex, int dashCount, Vector3 origin,
            Vector3 direction, float length, float width, bool isDirectionLocked, double telegraphStartServerTime, float telegraphSeconds,
            bool hasDashStarted, double dashStartServerTime, float dashDistance, float dashSeconds, float dashSpeed, float stoppedDistance,
            bool stoppedByWall)
        {
            if (IsServerBuild || _casts.ContainsKey(castInstanceId) || (dashCount < 1) || (dashIndex < 0) || (dashIndex >= dashCount))
            {
                return false;
            }

            BossTelegraphPresenter presenter = PreparePresenter();
            if (presenter == null)
            {
                return false;
            }

            _casts.Add(castInstanceId, new Cast { BossId = bossId, PatternId = patternId, DashCount = dashCount });
            TrackGroup(castInstanceId);

            BossTelegraphStart start = new BossTelegraphStart(bossId, Keys.Get(castInstanceId, dashIndex), castInstanceId, patternId,
                EBossTelegraphShape.Dash, origin, direction, length, width, 0f, telegraphStartServerTime, telegraphSeconds);
            BossTelegraphSnapshot snapshot = new BossTelegraphSnapshot
            {
                Start               = start,
                Direction           = direction,
                Length              = hasDashStarted ? dashDistance : length,
                IsDirectionLocked   = isDirectionLocked || hasDashStarted,
                Phase               = hasDashStarted ? EBossTelegraphPhase.Execute : EBossTelegraphPhase.Telegraph,
                ExecuteServerTime   = dashStartServerTime,
                DashDistance        = hasDashStarted ? dashDistance : 0f,
                DashSeconds         = hasDashStarted ? dashSeconds : 0f,
                DashSpeed           = hasDashStarted ? dashSpeed : 0f,
                DashStoppedDistance = hasDashStarted ? stoppedDistance : NOT_STOPPED,
                DashStoppedByWall   = hasDashStarted && stoppedByWall
            };

            // 이미 지난 스냅샷처럼 예고 표현이 받아들이지 않았으면 등록한 시전과 묶음과 번호를 남기지 않는다
            bool isRestored = presenter.Restore(in snapshot);
            if (!isRestored)
            {
                _casts.Remove(castInstanceId);
                ForgetCast(castInstanceId);
            }

            return isRestored;
        }

        /// <summary> 진행 중인 시전의 상태, 시험과 진단용이다 </summary>
        public bool TryGetCast(int castInstanceId, out int dashCount, out bool isTruncated, out int completedBeforeCut)
        {
            if (_casts.TryGetValue(castInstanceId, out Cast cast))
            {
                dashCount          = cast.DashCount;
                isTruncated        = cast.IsTruncated;
                completedBeforeCut = cast.CompletedBeforeCut;
                return true;
            }

            dashCount          = 0;
            isTruncated        = false;
            completedBeforeCut = 0;
            return false;
        }

        public override void ClearAll()
        {
            _casts.Clear();
            base.ClearAll();
        }

        private bool BeginDashTelegraph(int castInstanceId, int dashIndex, Vector3 origin, Vector3 direction, float length, float width,
            double startServerTime, float seconds)
        {
            BossTelegraphPresenter presenter = PreparePresenter();
            if ((presenter == null) || (!_casts.TryGetValue(castInstanceId, out Cast cast)))
            {
                return false;
            }

            BossTelegraphStart start = new BossTelegraphStart(cast.BossId, Keys.Get(castInstanceId, dashIndex), castInstanceId, cast.PatternId,
                EBossTelegraphShape.Dash, origin, direction, length, width, 0f, startServerTime, seconds);
            return presenter.BeginTelegraph(in start);
        }
    }
}
