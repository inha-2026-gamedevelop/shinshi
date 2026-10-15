// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Combat
{
    // 월백 하드 2페이즈 P01~P06의 서버 알림을 표현용 사건(IBossHardPhaseEventListener)으로 바꿔 보낸다.
    // 서버 전용 배치 타입은 클라이언트 어셈블리에 없어 값으로 풀어 싣고, 표현에 쓰지 않는 처리 시각, 적중 수, 중단 때의 진행 수는 버린다.
    // P04 장판의 활성 뒤와 P05 투사체의 비행은 몸체가 기본 사건으로 보내므로 여기서 다루지 않는다.
    //
    // 수신처가 없어도 시전 번호 알림은 한다. 리스너마다 알림 이름이 달라 겹치지 않지만 몸체와 같게 인터페이스별로 나눠 구현한다
    /// <summary> 월백 하드 2페이즈 패턴 알림을 복제 상태가 받는 하드 페이즈 사건으로 바꾸는 중계 </summary>
    public sealed class BossHardPhaseEventRelay : IBossStarStageListener, IBossGridBeamListener, IBossChainFanListener,
        IBossHardAreaListener, IBossBarrageListener, IBossCircleBurstListener
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly IBossEventRelayHost _host;

        private IBossHardPhaseEventListener _sink;

        /****************************************
        *                Methods
        ****************************************/

        public BossHardPhaseEventRelay(IBossEventRelayHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        /// <summary> 사건을 받을 수신처를 바꾼다, 비우면 사건을 보내지 않는다 </summary>
        public void SetListener(IBossHardPhaseEventListener listener)
        {
            _sink = listener;
        }

        /****************************************
        *            P01 Star Stage
        ****************************************/

        // 기준 방향은 패턴이 시전 시작에 대상 쪽으로 잡은 방향이다. 회전 별 빔의 예고 시작과 같게 몸을 그쪽으로 돌린다
        void IBossStarStageListener.OnStageSequenceStarted(int castInstanceId, int starId, in BossStarShape shape,
            float stepDegrees, int stageCount, double castStartServerTime, float firstStrikeSeconds, float strikeIntervalSeconds)
        {
            _host.Face(shape.BaseDirection);
            _host.BeginPhaseCast(castInstanceId);
            if (_sink == null)
            {
                return;
            }

            var start = new BossStarStageStart(_host.CombatId, castInstanceId, starId, shape, stepDegrees, stageCount,
                castStartServerTime, firstStrikeSeconds, strikeIntervalSeconds);
            _sink.OnStarStageStarted(in start);
        }

        void IBossStarStageListener.OnStageStruck(int castInstanceId, int starId, int stageIndex, float rotationDegrees,
            double scheduledServerTime, double processedServerTime, int hitCount)
        {
            _sink?.OnStarStageStruck(castInstanceId, stageIndex, rotationDegrees, scheduledServerTime);
        }

        void IBossStarStageListener.OnStageSequenceCancelled(int castInstanceId, int starId, int nextStageIndex,
            EBossInterruptReason reason)
        {
            _sink?.OnStarStageCancelled(castInstanceId);
        }

        /****************************************
        *            P02 Grid Beam
        ****************************************/

        void IBossGridBeamListener.OnGridStarted(int castInstanceId, in BossGridBeamLayout layout, double castStartServerTime,
            float firstStrikeSeconds, float strikeIntervalSeconds, float axisGapSeconds)
        {
            _host.BeginPhaseCast(castInstanceId);
            if (_sink == null)
            {
                return;
            }

            var start = new BossGridBeamStart(_host.CombatId, castInstanceId, layout.ArenaCenter, layout.ArenaRadius,
                layout.LineCount, layout.LineWidth, layout.LineSpacing, castStartServerTime, firstStrikeSeconds,
                strikeIntervalSeconds, axisGapSeconds);
            _sink.OnGridBeamStarted(in start);
        }

        void IBossGridBeamListener.OnLineTelegraphed(int castInstanceId, int lineId, EBossGridBeamAxis axis, int lineIndex,
            double telegraphServerTime, double strikeServerTime)
        {
            _sink?.OnGridBeamLineTelegraphed(castInstanceId, lineId, IsHorizontal(axis), lineIndex, telegraphServerTime,
                strikeServerTime);
        }

        void IBossGridBeamListener.OnLineStruck(int castInstanceId, int lineId, EBossGridBeamAxis axis, int lineIndex,
            double scheduledServerTime, double processedServerTime, int hitCount)
        {
            _sink?.OnGridBeamLineStruck(castInstanceId, lineId, IsHorizontal(axis), lineIndex, scheduledServerTime);
        }

        void IBossGridBeamListener.OnGridCancelled(int castInstanceId, int telegraphedCount, int struckCount,
            EBossInterruptReason reason)
        {
            _sink?.OnGridBeamCancelled(castInstanceId);
        }

        /****************************************
        *            P03 Chain Fan
        ****************************************/

        // 피난 방향은 조준이 아니라 안전지대의 가운데라 몸을 돌리지 않는다
        void IBossChainFanListener.OnChainFanStarted(int castInstanceId, in BossChainFanLayout layout,
            double castStartServerTime, float safeRevealSeconds, float firstBlastSeconds, float blastIntervalSeconds,
            float lingerSeconds)
        {
            _host.BeginPhaseCast(castInstanceId);
            if (_sink == null)
            {
                return;
            }

            var start = new BossChainFanStart(_host.CombatId, castInstanceId, layout.Origin, layout.SafeDirection, layout.Range,
                layout.ZoneCount, layout.ZoneArc, castStartServerTime, safeRevealSeconds, firstBlastSeconds,
                blastIntervalSeconds, lingerSeconds);
            _sink.OnChainFanStarted(in start);
        }

        void IBossChainFanListener.OnChainFanBlasted(int castInstanceId, int zoneIndex, double scheduledServerTime,
            double processedServerTime, int hitCount)
        {
            _sink?.OnChainFanBlasted(castInstanceId, zoneIndex, scheduledServerTime);
        }

        void IBossChainFanListener.OnChainFanDangerEnded(int castInstanceId, double scheduledServerTime)
        {
            _sink?.OnChainFanDangerEnded(castInstanceId, scheduledServerTime);
        }

        void IBossChainFanListener.OnChainFanCancelled(int castInstanceId, int blastedCount, bool dangerEnded,
            EBossInterruptReason reason)
        {
            _sink?.OnChainFanCancelled(castInstanceId);
        }

        /****************************************
        *          P04 Tracking Area
        ****************************************/

        // 한 시전이 장판을 여럿 예고하면 시작 알림이 장판마다 온다. 시전 번호 알림은 같은 번호를 다시 적는 것이라 여러 번 불러도 된다
        void IBossHardAreaListener.OnMovingStarted(int castInstanceId, int areaId, int targetCombatId, Vector3 center,
            float radius, double startServerTime, float followSeconds, float fixedSeconds)
        {
            _host.BeginPhaseCast(castInstanceId);
            if (_sink == null)
            {
                return;
            }

            var start = new BossTrackingAreaStart(_host.CombatId, castInstanceId, areaId, targetCombatId, center, radius,
                startServerTime, followSeconds, fixedSeconds);
            _sink.OnTrackingAreaStarted(in start);
        }

        void IBossHardAreaListener.OnMovingUpdated(int castInstanceId, int areaId, Vector3 center, double serverTime)
        {
            _sink?.OnTrackingAreaMoved(areaId, center, serverTime);
        }

        void IBossHardAreaListener.OnCenterFixed(int castInstanceId, int areaId, Vector3 center, double fixedServerTime)
        {
            _sink?.OnTrackingAreaFixed(areaId, center, fixedServerTime);
        }

        void IBossHardAreaListener.OnCancelled(int castInstanceId, int areaId, EBossInterruptReason reason)
        {
            _sink?.OnTrackingAreaCancelled(areaId);
        }

        /****************************************
        *             P05 Barrage
        ****************************************/

        void IBossBarrageListener.OnBarrageStarted(int castInstanceId, Vector3 origin, double castStartServerTime,
            int shotCount, float telegraphSeconds, float shotIntervalSeconds, float width)
        {
            _host.BeginPhaseCast(castInstanceId);
            if (_sink == null)
            {
                return;
            }

            var start = new BossBarrageStart(_host.CombatId, castInstanceId, origin, castStartServerTime, shotCount,
                telegraphSeconds, shotIntervalSeconds, width);
            _sink.OnBarrageStarted(in start);
        }

        // 발마다 방향이 사방으로 흩어져 몸을 돌리지 않는다
        void IBossBarrageListener.OnBarrageShotTelegraphed(int castInstanceId, int shotIndex, int sectorIndex,
            Vector3 direction, float pathLength, double scheduledServerTime, double processedServerTime)
        {
            if (_sink == null)
            {
                return;
            }

            var shot = new BossBarrageShot(castInstanceId, shotIndex, sectorIndex, direction, pathLength, scheduledServerTime);
            _sink.OnBarrageShotTelegraphed(in shot);
        }

        void IBossBarrageListener.OnBarrageShotFired(int castInstanceId, int shotIndex, int projectileId, bool isLaunched,
            double scheduledServerTime, double processedServerTime)
        {
            _sink?.OnBarrageShotFired(castInstanceId, shotIndex, projectileId, isLaunched, scheduledServerTime);
        }

        void IBossBarrageListener.OnBarrageCancelled(int castInstanceId, int telegraphedCount, int firedCount,
            EBossInterruptReason reason)
        {
            _sink?.OnBarrageCancelled(castInstanceId);
        }

        /****************************************
        *           P06 Circle Burst
        ****************************************/

        // 원 배열은 시전 시작에 한 번만 만든다. 사건 값이 시전이 끝날 때까지 들고 있어야 해서 재사용 버퍼를 쓸 수 없다
        void IBossCircleBurstListener.OnBurstStarted(int castInstanceId, in BossCircleBurstLayout layout,
            double castStartServerTime, float strikeSeconds)
        {
            _host.BeginPhaseCast(castInstanceId);
            if (_sink == null)
            {
                return;
            }

            int count   = layout.Count;
            var centers = new Vector3[count];
            var radii   = new float[count];
            for (int i = 0; i < count; ++i)
            {
                centers[i] = layout.GetCenter(i);
                radii[i]   = layout.GetRadius(i);
            }

            var start = new BossCircleBurstStart(_host.CombatId, castInstanceId, centers, radii, castStartServerTime,
                strikeSeconds);
            _sink.OnCircleBurstStarted(in start);
        }

        void IBossCircleBurstListener.OnBurstStruck(int castInstanceId, double scheduledServerTime,
            double processedServerTime, int hitCount)
        {
            _sink?.OnCircleBurstStruck(castInstanceId, scheduledServerTime);
        }

        void IBossCircleBurstListener.OnBurstCancelled(int castInstanceId, bool struck, EBossInterruptReason reason)
        {
            _sink?.OnCircleBurstCancelled(castInstanceId);
        }

        /****************************************
        *               Shared
        ****************************************/

        private static bool IsHorizontal(EBossGridBeamAxis axis)
        {
            return axis == EBossGridBeamAxis.Horizontal;
        }
    }
}
