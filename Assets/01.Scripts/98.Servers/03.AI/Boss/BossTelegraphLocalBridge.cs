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
        IBossProjectileTelegraphListener, IBossAreaTelegraphListener, IBossProjectileListener, IBossAreaListener
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly BossTelegraphPresenter _presenter;
        private readonly BossTestRigBody        _body;
        private readonly Func<double>           _clock;

        // 부채꼴과 직사각형은 조준 갱신 알림에 길이가 없어서 시작 때 받은 길이를 시전 번호별로 기억한다
        private readonly Dictionary<int, float> _lengths = new Dictionary<int, float>();

        /****************************************
        *                Methods
        ****************************************/

        public BossTelegraphLocalBridge(BossTelegraphPresenter presenter, BossTestRigBody body, Func<double> clock)
        {
            _presenter = presenter;
            _body      = body;
            _clock     = clock;
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
            Begin(BossTelegraphStart.Circle(Boss, castInstanceId, WolbaekPatternFactory.P03_ID, center, radius, Now,
                telegraphSeconds));
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

        void IBossDashTelegraphListener.OnDashStopped(int castInstanceId, Vector3 position, bool stoppedByWall)
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
            Begin(BossTelegraphStart.Projectile(Boss, castInstanceId, WolbaekPatternFactory.P04_ID, origin, direction,
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

        void IBossProjectileListener.OnProjectileLaunched(int castInstanceId, Vector3 origin, Vector3 direction,
            float speed, float width, float pathLength, double launchServerTime)
        {
            _presenter.enabled = true;
            var start = new BossProjectileStart(Boss, castInstanceId, origin, direction, speed, width, pathLength,
                launchServerTime);
            _presenter.LaunchProjectile(in start);
        }

        void IBossProjectileListener.OnProjectileEnded(int castInstanceId, EBossProjectileEnd end, Vector3 position,
            int hitTargetId)
        {
            _presenter.EndProjectile(castInstanceId, ToFate(end), position);
        }

        /****************************************
        *             P05 Area
        ****************************************/

        void IBossAreaTelegraphListener.OnTelegraphStarted(int castInstanceId, Vector3 center, float radius,
            float telegraphSeconds)
        {
            Begin(BossTelegraphStart.Circle(Boss, castInstanceId, WolbaekPatternFactory.P05_ID, center, radius, Now,
                telegraphSeconds));
        }

        void IBossAreaTelegraphListener.OnCancelled(int castInstanceId, EBossInterruptReason reason)
        {
            CancelCast(castInstanceId);
        }

        void IBossAreaListener.OnAreaActivated(int castInstanceId, Vector3 center, float radius, float durationSeconds,
            float tickInterval, double activateServerTime)
        {
            _presenter.enabled = true;
            var start = new BossAreaStart(Boss, castInstanceId, center, radius, durationSeconds, tickInterval,
                activateServerTime);
            _presenter.ActivateArea(in start);
        }

        void IBossAreaListener.OnAreaTick(int castInstanceId, int hitIndex, double roundServerTime, int hitCount)
        {
            _presenter.TickArea(castInstanceId, hitIndex, roundServerTime);
        }

        void IBossAreaListener.OnAreaEnded(int castInstanceId, EBossAreaEnd end, double endServerTime)
        {
            _presenter.EndArea(castInstanceId, (end == EBossAreaEnd.Expired) ? EBossAreaFate.Expired : EBossAreaFate.Cancelled);
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
