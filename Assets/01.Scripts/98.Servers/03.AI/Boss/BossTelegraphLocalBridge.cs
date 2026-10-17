// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버 패턴 알림과 클라이언트 표현(BossTelegraphPresenter) 사이의 연결이다. 실제 구성에서는 복제 계층이 맡을 일을
    // 한 프로세스 안에서 직접 부른다. 서버 판정과 표현은 이 연결로만 닿고 표현이 서버 상태를 되쓰지 않는다.
    // 같은 시그니처의 알림이 여러 인터페이스에 있어 모두 명시적으로 구현한다
    /// <summary> 서버 알림을 표현 진입점 호출로 바꾸는 시험용 연결부 </summary>
    public sealed class BossTelegraphLocalBridge :
        IBossFanTelegraphListener, IBossCircleTelegraphListener, IBossLineTelegraphListener, IBossDashTelegraphListener,
        IBossProjectileTelegraphListener, IBossAreaTelegraphListener, IBossProjectileListener, IBossAreaListener,
        IBossSweepTelegraphListener, IBossBeamTelegraphListener, IBossBeamListener,
        IBossStarTelegraphListener, IBossStarListener
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly BossTelegraphPresenter _presenter;
        private readonly BossTestRigBody        _body;
        private readonly Func<double>           _clock;
        private readonly Func<string>           _activePatternId;

        // 반월 연쇄 폭발의 고정한 중심과 반경, 구역 예고를 시작할 때 쓴다. 시전은 한 번에 하나다
        private Vector3 _sweepOrigin;
        private float   _sweepRadius;

        // 부채꼴과 직사각형은 조준 갱신 알림에 길이가 없어서 시작 때 받은 길이를 시전 번호별로 기억한다
        private readonly Dictionary<int, float> _lengths = new Dictionary<int, float>();

        /****************************************
        *                Methods
        ****************************************/

        // activePatternId는 지금 진행 중인 패턴 ID를 읽는 창구다. 원형 알림에는 패턴 ID가 없어서 1페이즈 P03과 2페이즈 P06을 가르는 데 쓴다
        public BossTelegraphLocalBridge(BossTelegraphPresenter presenter, BossTestRigBody body, Func<double> clock,
            Func<string> activePatternId = null)
        {
            _presenter       = presenter;
            _body            = body;
            _clock           = clock;
            _activePatternId = activePatternId;
        }

        private int Boss => _body.CombatId;

        private double Now => _clock();

        // 표현 진입점은 진행 중인 표현이 없으면 스스로 꺼지므로 새 표현을 시작하기 전에 켠다
        private void Begin(in BossTelegraphStart start)
        {
            _presenter.enabled = true;
            _presenter.BeginTelegraph(in start);
        }

        /****************************************
        *               P02 Fan
        ****************************************/

        void IBossFanTelegraphListener.OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction,
            float radius, float arcAngle, float telegraphSeconds)
        {
            _body.Face(direction);
            _lengths[castInstanceId] = radius;
            Begin(BossTelegraphStart.Fan(Boss, castInstanceId, WolbaekPatternFactory.P02_ID, origin, direction, radius,
                arcAngle, Now, telegraphSeconds));
        }

        void IBossFanTelegraphListener.OnAimChanged(int castInstanceId, Vector3 direction)
        {
            AimLine(castInstanceId, direction);
        }

        void IBossFanTelegraphListener.OnDirectionLocked(int castInstanceId, Vector3 direction)
        {
            LockLine(castInstanceId, direction);
        }

        void IBossFanTelegraphListener.OnHit(int castInstanceId, int hitCount)
        {
            Strike(castInstanceId);
        }

        void IBossFanTelegraphListener.OnCancelled(int castInstanceId, EBossInterruptReason reason)
        {
            CancelCast(castInstanceId);
        }

        /****************************************
        *              P03 Circle
        ****************************************/

        void IBossCircleTelegraphListener.OnTelegraphStarted(int castInstanceId, Vector3 center, float radius,
            float telegraphSeconds)
        {
            // 서버는 활성 패턴 ID를 그대로 싣는다. 대역도 2페이즈 P06과 P07은 자기 ID를 실어야 화면이 P03으로 오인하지 않는다
            string activeId  = (_activePatternId != null) ? _activePatternId() : null;
            string patternId = ((activeId == WolbaekPatternFactory.P2_P06_ID) || (activeId == WolbaekPatternFactory.P2_P07_ID))
                ? activeId
                : WolbaekPatternFactory.P03_ID;
            Begin(BossTelegraphStart.Circle(Boss, castInstanceId, patternId, center, radius, Now, telegraphSeconds));
        }

        void IBossCircleTelegraphListener.OnHit(int castInstanceId, int hitCount)
        {
            Strike(castInstanceId);
        }

        void IBossCircleTelegraphListener.OnCancelled(int castInstanceId, EBossInterruptReason reason)
        {
            CancelCast(castInstanceId);
        }

        /****************************************
        *               P06 Line
        ****************************************/

        void IBossLineTelegraphListener.OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction,
            float length, float width, float telegraphSeconds)
        {
            _body.Face(direction);
            _lengths[castInstanceId] = length;
            Begin(BossTelegraphStart.Line(Boss, castInstanceId, WolbaekPatternFactory.P06_ID, origin, direction, length,
                width, Now, telegraphSeconds));
        }

        void IBossLineTelegraphListener.OnAimChanged(int castInstanceId, Vector3 direction)
        {
            AimLine(castInstanceId, direction);
        }

        void IBossLineTelegraphListener.OnDirectionLocked(int castInstanceId, Vector3 direction)
        {
            LockLine(castInstanceId, direction);
        }

        void IBossLineTelegraphListener.OnHit(int castInstanceId, int hitCount)
        {
            Strike(castInstanceId);
        }

        void IBossLineTelegraphListener.OnCancelled(int castInstanceId, EBossInterruptReason reason)
        {
            CancelCast(castInstanceId);
        }

        /****************************************
        *               P01 Dash
        ****************************************/

        void IBossDashTelegraphListener.OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction,
            float length, float width, float telegraphSeconds)
        {
            _body.Face(direction);
            Begin(BossTelegraphStart.Dash(Boss, castInstanceId, WolbaekPatternFactory.P01_ID, origin, direction, length,
                width, Now, telegraphSeconds));
        }

        void IBossDashTelegraphListener.OnAimChanged(int castInstanceId, Vector3 direction, float length)
        {
            _body.Face(direction);
            _presenter.UpdateAim(castInstanceId, direction, length, Now);
        }

        void IBossDashTelegraphListener.OnDirectionLocked(int castInstanceId, Vector3 direction, float length)
        {
            _body.Face(direction);
            _presenter.LockDirection(castInstanceId, direction, length);
        }

        void IBossDashTelegraphListener.OnDashStarted(int castInstanceId, Vector3 origin, Vector3 direction,
            float distance, float seconds, float speed, double startServerTime)
        {
            _presenter.BeginDash(castInstanceId, distance, seconds, speed, startServerTime);
        }

        void IBossDashTelegraphListener.OnDashStopped(int castInstanceId, Vector3 position, bool stoppedByWall,
            double stopServerTime)
        {
            _presenter.StopDash(castInstanceId, position, stoppedByWall);
        }

        void IBossDashTelegraphListener.OnHit(int castInstanceId, int hitCount)
        {
        }

        void IBossDashTelegraphListener.OnCancelled(int castInstanceId, EBossInterruptReason reason)
        {
            CancelCast(castInstanceId);
        }

        /****************************************
        *            P04 Projectile
        ****************************************/

        void IBossProjectileTelegraphListener.OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction,
            float length, float width, float telegraphSeconds)
        {
            _body.Face(direction);
            // 투사체 알림에도 패턴 ID가 없어 진행 중인 패턴 ID로 1페이즈 P04와 2페이즈 P05를 가른다
            string patternId = (_activePatternId != null) && (_activePatternId() == WolbaekPatternFactory.P2_P05_ID)
                ? WolbaekPatternFactory.P2_P05_ID
                : WolbaekPatternFactory.P04_ID;
            Begin(BossTelegraphStart.Projectile(Boss, castInstanceId, patternId, origin, direction,
                length, width, Now, telegraphSeconds));
        }

        void IBossProjectileTelegraphListener.OnAimChanged(int castInstanceId, Vector3 direction, float length)
        {
            _body.Face(direction);
            _presenter.UpdateAim(castInstanceId, direction, length, Now);
        }

        void IBossProjectileTelegraphListener.OnDirectionLocked(int castInstanceId, Vector3 direction, float length)
        {
            _body.Face(direction);
            _presenter.LockDirection(castInstanceId, direction, length);
        }

        void IBossProjectileTelegraphListener.OnCancelled(int castInstanceId, EBossInterruptReason reason)
        {
            CancelCast(castInstanceId);
        }

        void IBossProjectileListener.OnProjectileLaunched(int castInstanceId, int projectileId, Vector3 origin,
            Vector3 direction, float speed, float width, float pathLength, double launchServerTime)
        {
            _presenter.enabled = true;
            var start = new BossProjectileStart(Boss, projectileId, castInstanceId, origin, direction, speed, width, pathLength,
                launchServerTime);
            _presenter.LaunchProjectile(in start);
        }

        void IBossProjectileListener.OnProjectileEnded(int castInstanceId, int projectileId, EBossProjectileEnd end,
            Vector3 position, int hitTargetId)
        {
            _presenter.EndProjectile(projectileId, ToFate(end), position);
        }

        /****************************************
        *             P05 Area
        ****************************************/

        // 장판 예고는 장판 번호를 표현의 시전 키로 쓴다. 한 시전의 장판 예고들은 시전 번호를 묶음으로 달아 서로 지우지 않게 한다
        void IBossAreaTelegraphListener.OnTelegraphStarted(int castInstanceId, int areaId, Vector3 center, float radius,
            float telegraphSeconds)
        {
            // 장판 알림에도 패턴 ID가 없어 진행 중인 패턴 ID로 1페이즈 P05와 2페이즈 P04를 가른다
            string patternId = (_activePatternId != null) && (_activePatternId() == WolbaekPatternFactory.P2_P04_ID)
                ? WolbaekPatternFactory.P2_P04_ID
                : WolbaekPatternFactory.P05_ID;
            Begin(BossTelegraphStart.Circle(Boss, areaId, patternId, center, radius, Now, telegraphSeconds, castInstanceId));
        }

        void IBossAreaTelegraphListener.OnCancelled(int castInstanceId, int areaId, EBossInterruptReason reason)
        {
            CancelCast(areaId);
        }

        void IBossAreaListener.OnAreaActivated(int castInstanceId, int areaId, Vector3 center, float radius,
            float durationSeconds, float tickInterval, double activateServerTime)
        {
            _presenter.enabled = true;
            var start = new BossAreaStart(Boss, areaId, castInstanceId, center, radius, durationSeconds, tickInterval,
                activateServerTime);
            _presenter.ActivateArea(in start);
        }

        void IBossAreaListener.OnAreaTick(int castInstanceId, int areaId, int hitIndex, double roundServerTime, int hitCount)
        {
            _presenter.TickArea(areaId, hitIndex, roundServerTime);
        }

        void IBossAreaListener.OnAreaEnded(int castInstanceId, int areaId, EBossAreaEnd end, double endServerTime)
        {
            _presenter.EndArea(areaId, (end == EBossAreaEnd.Expired) ? EBossAreaFate.Expired : EBossAreaFate.Cancelled);
        }

        /****************************************
        *        P03 (Phase 2) Sweep
        ****************************************/

        // 구역마다 부채꼴 예고 하나를 시작하고 구역 번호를 표현의 시전 키로, 시전 번호를 묶음 번호로 쓴다. 구역의 예고 길이는 그 구역의 폭발 시각까지다
        // 서버가 구역 폭발을 확정하면 그 구역만 터지고, 예고 시간이 끝나는 것만으로는 터지지 않는다
        void IBossSweepTelegraphListener.OnSweepStarted(int castInstanceId, Vector3 origin, Vector3 baseDirection, bool isRight,
            float radius, float totalArcAngle, int zoneCount, float telegraphSeconds, float blastSpanSeconds)
        {
            _body.Face(baseDirection);
            _sweepOrigin = origin;
            _sweepRadius = radius;

            _presenter.enabled = true;
            var start = new BossSweepStart(Boss, castInstanceId, origin, baseDirection, isRight, radius, totalArcAngle,
                telegraphSeconds, blastSpanSeconds, Now);
            _presenter.BeginSweepCue(in start);
        }

        void IBossSweepTelegraphListener.OnZoneScheduled(int castInstanceId, int zoneId, int zoneIndex, Vector3 centerDirection,
            float zoneArcAngle, float secondsUntilBlast)
        {
            Begin(BossTelegraphStart.Fan(Boss, zoneId, WolbaekPatternFactory.P2_P03_ID, _sweepOrigin, centerDirection,
                _sweepRadius, zoneArcAngle, Now, secondsUntilBlast, castInstanceId));
        }

        void IBossSweepTelegraphListener.OnZoneBlasted(int castInstanceId, int zoneId, int zoneIndex, int hitCount,
            double blastServerTime)
        {
            _presenter.Strike(zoneId, blastServerTime);
        }

        void IBossSweepTelegraphListener.OnCancelled(int castInstanceId, EBossInterruptReason reason)
        {
            _presenter.CancelGroup(castInstanceId);
        }

        /****************************************
        *         P02 (Phase 2) Field Beam
        ****************************************/

        // 빔 한 줄마다 예고를 시작하고 빔 번호를 표현의 키로 쓴다. 시전 번호는 묶음 번호로 달려 서로 지우지 않는다
        // 서버가 활성을 확정하기 전에는 예고만 보이고, 예고 시간이 끝나는 것만으로는 지속 빔이 되지 않는다
        void IBossBeamTelegraphListener.OnTelegraphStarted(int castInstanceId, int beamId, in BossBeamShape shape,
            float telegraphSeconds, float durationSeconds, float tickInterval)
        {
            _presenter.enabled = true;
            var start = new BossBeamStart(Boss, beamId, castInstanceId, in shape, Now, telegraphSeconds, durationSeconds, tickInterval);
            _presenter.BeginBeam(in start);
        }

        void IBossBeamTelegraphListener.OnCancelled(int castInstanceId, int beamId, EBossInterruptReason reason)
        {
            _presenter.CancelBeam(beamId);
        }

        void IBossBeamListener.OnBeamActivated(int castInstanceId, int beamId, float durationSeconds, float tickInterval,
            double activateServerTime)
        {
            _presenter.enabled = true;
            _presenter.ActivateBeam(beamId, activateServerTime);
        }

        void IBossBeamListener.OnBeamTick(int castInstanceId, int beamId, int hitIndex, double roundServerTime, int hitCount)
        {
            _presenter.TickBeam(beamId, hitIndex, roundServerTime);
        }

        void IBossBeamListener.OnBeamEnded(int castInstanceId, int beamId, EBossAreaEnd end, double endServerTime)
        {
            _presenter.EndBeam(beamId, (end == EBossAreaEnd.Expired) ? EBossAreaFate.Expired : EBossAreaFate.Cancelled);
        }

        /****************************************
        *         P01 (Phase 2) Rotating Star
        ****************************************/

        // 별 구조 하나마다 예고를 시작하고 구조 번호를 표현의 키로 쓴다. 서버가 활성을 확정하기 전에는 예고만 보이고 회전하지 않는다
        void IBossStarTelegraphListener.OnTelegraphStarted(int castInstanceId, int starId, in BossStarShape shape,
            float telegraphSeconds, float durationSeconds, float tickInterval)
        {
            _body.Face(shape.BaseDirection);
            _presenter.enabled = true;
            var start = new BossStarStart(Boss, starId, castInstanceId, in shape, Now, telegraphSeconds, durationSeconds, tickInterval);
            _presenter.BeginStar(in start);
        }

        void IBossStarTelegraphListener.OnCancelled(int castInstanceId, int starId, EBossInterruptReason reason)
        {
            _presenter.CancelStar(starId);
        }

        void IBossStarListener.OnStarActivated(int castInstanceId, int starId, float durationSeconds, float tickInterval,
            double activateServerTime)
        {
            _presenter.ActivateStar(starId, activateServerTime);
        }

        void IBossStarListener.OnStarTick(int castInstanceId, int starId, int hitIndex, double roundServerTime, int hitCount)
        {
            _presenter.TickStar(starId, hitIndex, roundServerTime);
        }

        void IBossStarListener.OnStarEnded(int castInstanceId, int starId, EBossAreaEnd end, double endServerTime)
        {
            _presenter.EndStar(starId, (end == EBossAreaEnd.Expired) ? EBossAreaFate.Expired : EBossAreaFate.Cancelled);
        }

        /****************************************
        *               Shared
        ****************************************/

        private void AimLine(int castInstanceId, Vector3 direction)
        {
            _body.Face(direction);
            if (_lengths.TryGetValue(castInstanceId, out float length))
            {
                _presenter.UpdateAim(castInstanceId, direction, length, Now);
            }
        }

        private void LockLine(int castInstanceId, Vector3 direction)
        {
            _body.Face(direction);
            if (_lengths.TryGetValue(castInstanceId, out float length))
            {
                _presenter.LockDirection(castInstanceId, direction, length);
            }
        }

        private void Strike(int castInstanceId)
        {
            _lengths.Remove(castInstanceId);
            _presenter.Strike(castInstanceId, Now);
        }

        private void CancelCast(int castInstanceId)
        {
            _lengths.Remove(castInstanceId);
            _presenter.Cancel(castInstanceId);
        }

        private static EBossProjectileFate ToFate(EBossProjectileEnd end)
        {
            switch (end)
            {
                case EBossProjectileEnd.Hit:
                    return EBossProjectileFate.Hit;
                case EBossProjectileEnd.Wall:
                    return EBossProjectileFate.Wall;
                case EBossProjectileEnd.Range:
                    return EBossProjectileFate.Range;
                default:
                    return EBossProjectileFate.Cancelled;
            }
        }
    }
}
