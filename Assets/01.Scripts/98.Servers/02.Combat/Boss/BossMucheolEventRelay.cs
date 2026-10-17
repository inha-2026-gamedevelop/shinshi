// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Combat
{
    // 무철 전용 패턴(콤보, 순차 타격, 연속 돌진, 전장 공격, 돌진 결과)과 돌진 유도 기믹 그로기의 서버 알림을 표현용 사건(IBossMucheolEventListener)으로 바꿔 보낸다.
    // 서버 전용 타 값에서 피해 배수를 빼고, 알림에 없는 시각은 몸체의 틱 시각으로 채운다. 적중 수와 밀림은 표현에 쓰지 않아 보내지 않는다.
    //
    // 수신처가 없어도 정면 갱신과 시전 번호 알림은 한다. 정면은 다음 선택의 방향 구간과 후방 판정의 입력이라 표시 연결과 무관하게 맞아야 한다.
    // 여러 리스너에 같은 형식의 알림이 있어 모두 인터페이스별로 나눠 구현한다
    /// <summary> 무철 패턴 알림을 복제 상태가 받는 무철 사건으로 바꾸는 중계 </summary>
    public sealed class BossMucheolEventRelay : IBossComboTelegraphListener, IMucheolSequenceTelegraphListener,
        IMucheolMultiDashListener, IMucheolBattlefieldListener, IBossChargeOutcomeListener, IMucheolChargeGimmickListener
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly IBossEventRelayHost _host;

        private IBossMucheolEventListener _sink;

        // 마지막으로 보낸 돌진 결과, 그로기 사건의 충돌 시각을 여기서 읽는다
        private int    _chargeCastId      = CombatIds.NONE;
        private int    _chargeStructureId = CombatIds.NONE;
        private double _chargeServerTime;
        private bool   _isChargeCollision = false;

        // 같은 시전의 돌진 결과보다 먼저 온 그로기 대기, 그 결과를 보낸 뒤에 보낸다
        private bool   _hasHeldGroggy     = false;
        private int    _heldCastId        = CombatIds.NONE;
        private int    _heldStructureId;
        private double _heldConfirmTime;

        // 보낸 대기의 충돌 시각, 같은 그로기의 시작 사건에 같은 값을 싣는다
        private int    _pendingCastId     = CombatIds.NONE;
        private double _pendingCollision  = double.NaN;

        /****************************************
        *                Methods
        ****************************************/

        public BossMucheolEventRelay(IBossEventRelayHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        /// <summary> 사건을 받을 수신처를 바꾼다, 비우면 사건을 보내지 않는다 </summary>
        public void SetListener(IBossMucheolEventListener listener)
        {
            _sink = listener;
        }

        /****************************************
        *             P1 P05 Combo
        ****************************************/

        // 타 배열은 시전 시작에 한 번만 만든다. 사건 값이 시전이 끝날 때까지 들고 있어야 해서 재사용 버퍼를 쓸 수 없다
        void IBossComboTelegraphListener.OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction,
            float telegraphSeconds, IReadOnlyList<MucheolComboHit> hits)
        {
            _host.Face(direction);
            _host.BeginPhaseCast(castInstanceId);
            if ((_sink == null) || (hits == null))
            {
                return;
            }

            int count            = hits.Count;
            var shapes           = new EBossTelegraphShape[count];
            var ranges           = new float[count];
            var arcAngles        = new float[count];
            var widths           = new float[count];
            var aimOffsetDegrees = new float[count];
            var delaySeconds     = new float[count];

            // 콤보의 타는 모두 조준 방향 그대로의 부채꼴이라 폭과 오프셋은 0으로 남긴다
            for (int i = 0; i < count; ++i)
            {
                MucheolComboHit hit = hits[i];
                shapes[i]       = EBossTelegraphShape.Fan;
                ranges[i]       = hit.Range;
                arcAngles[i]    = hit.ArcAngle;
                delaySeconds[i] = hit.DelaySeconds;
            }

            var start = new BossStrikeChainStart(_host.CombatId, castInstanceId, _host.ActivePatternId,
                EBossStrikeChainKind.Combo, origin, direction, _host.TickTime, telegraphSeconds, shapes, ranges, arcAngles,
                widths, aimOffsetDegrees, delaySeconds);
            _sink.OnStrikeChainStarted(in start);
        }

        void IBossComboTelegraphListener.OnAimChanged(int castInstanceId, Vector3 direction)
        {
            AimStrikeChain(EBossStrikeChainKind.Combo, castInstanceId, direction);
        }

        void IBossComboTelegraphListener.OnDirectionLocked(int castInstanceId, Vector3 direction)
        {
            LockStrikeChain(EBossStrikeChainKind.Combo, castInstanceId, direction);
        }

        void IBossComboTelegraphListener.OnHit(int castInstanceId, int hitIndex, int hitCount)
        {
            _sink?.OnStrikeChainHit(EBossStrikeChainKind.Combo, castInstanceId, hitIndex, _host.TickTime);
        }

        void IBossComboTelegraphListener.OnCancelled(int castInstanceId, EBossInterruptReason reason)
        {
            _sink?.OnStrikeChainCancelled(EBossStrikeChainKind.Combo, castInstanceId);
        }

        /****************************************
        *        P2 P01, P02, P04, P05 Sequence
        ****************************************/

        // 원형 타만 있는 시전(P04)도 정면을 돌린다. 패턴은 형태와 상관없이 정의의 추적 값대로 조준을 움직이고,
        // 추적 값이 0이면 조준이 시작 순간의 정면 그대로라 돌려도 달라지지 않는다
        void IMucheolSequenceTelegraphListener.OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction,
            float telegraphSeconds, IReadOnlyList<MucheolSequenceStep> steps)
        {
            _host.Face(direction);
            _host.BeginPhaseCast(castInstanceId);
            if ((_sink == null) || (steps == null))
            {
                return;
            }

            int count            = steps.Count;
            var shapes           = new EBossTelegraphShape[count];
            var ranges           = new float[count];
            var arcAngles        = new float[count];
            var widths           = new float[count];
            var aimOffsetDegrees = new float[count];
            var delaySeconds     = new float[count];

            for (int i = 0; i < count; ++i)
            {
                MucheolSequenceStep step = steps[i];
                shapes[i]           = ToTelegraphShape(step.Shape);
                ranges[i]           = step.Range;
                arcAngles[i]        = step.ArcAngle;
                widths[i]           = step.Width;
                aimOffsetDegrees[i] = step.AimOffsetDegrees;
                delaySeconds[i]     = step.DelaySeconds;
            }

            var start = new BossStrikeChainStart(_host.CombatId, castInstanceId, _host.ActivePatternId,
                EBossStrikeChainKind.Sequence, origin, direction, _host.TickTime, telegraphSeconds, shapes, ranges, arcAngles,
                widths, aimOffsetDegrees, delaySeconds);
            _sink.OnStrikeChainStarted(in start);
        }

        void IMucheolSequenceTelegraphListener.OnAimChanged(int castInstanceId, Vector3 direction)
        {
            AimStrikeChain(EBossStrikeChainKind.Sequence, castInstanceId, direction);
        }

        void IMucheolSequenceTelegraphListener.OnDirectionLocked(int castInstanceId, Vector3 direction)
        {
            LockStrikeChain(EBossStrikeChainKind.Sequence, castInstanceId, direction);
        }

        void IMucheolSequenceTelegraphListener.OnStepHit(int castInstanceId, int stepIndex, int hitCount)
        {
            _sink?.OnStrikeChainHit(EBossStrikeChainKind.Sequence, castInstanceId, stepIndex, _host.TickTime);
        }

        void IMucheolSequenceTelegraphListener.OnCancelled(int castInstanceId, EBossInterruptReason reason)
        {
            _sink?.OnStrikeChainCancelled(EBossStrikeChainKind.Sequence, castInstanceId);
        }

        /****************************************
        *          P2 P03, P06 Multi Dash
        ****************************************/

        void IMucheolMultiDashListener.OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction, float length,
            float width, float telegraphSeconds, int dashCount)
        {
            _host.Face(direction);
            _host.BeginPhaseCast(castInstanceId);
            if (_sink == null)
            {
                return;
            }

            var start = new BossMultiDashStart(_host.CombatId, castInstanceId, _host.ActivePatternId, origin, direction, length,
                width, _host.TickTime, telegraphSeconds, dashCount);
            _sink.OnMultiDashStarted(in start);
        }

        void IMucheolMultiDashListener.OnAimChanged(int castInstanceId, int dashIndex, Vector3 direction, float length)
        {
            _host.Face(direction);
            _sink?.OnMultiDashAimChanged(castInstanceId, dashIndex, direction, length, _host.TickTime);
        }

        void IMucheolMultiDashListener.OnDirectionLocked(int castInstanceId, int dashIndex, Vector3 direction, float length)
        {
            _host.Face(direction);
            _sink?.OnMultiDashDirectionLocked(castInstanceId, dashIndex, direction, length);
        }

        void IMucheolMultiDashListener.OnReaimStarted(int castInstanceId, int dashIndex, Vector3 origin, Vector3 direction,
            float length, float width, float reaimSeconds)
        {
            _host.Face(direction);
            if (_sink == null)
            {
                return;
            }

            var reaim = new BossMultiDashReaim(castInstanceId, dashIndex, origin, direction, length, width, _host.TickTime,
                reaimSeconds);
            _sink.OnMultiDashReaimStarted(in reaim);
        }

        // 시작 시각은 틱 시각이 아니라 패턴이 계산한 경계라 그대로 싣는다
        void IMucheolMultiDashListener.OnDashStarted(int castInstanceId, int dashIndex, Vector3 origin, Vector3 direction,
            float distance, float seconds, float speed, double startServerTime)
        {
            _host.Face(direction);
            _sink?.OnMultiDashDashStarted(castInstanceId, dashIndex, distance, seconds, speed, startServerTime);
        }

        // 정지 시각은 틱 시각이 아니라 패턴이 정지를 판정한 시각이라 그대로 싣는다
        void IMucheolMultiDashListener.OnDashStopped(int castInstanceId, int dashIndex, Vector3 position, bool stoppedByWall,
            double stopServerTime)
        {
            _sink?.OnMultiDashDashStopped(castInstanceId, dashIndex, position, stoppedByWall, stopServerTime);
        }

        // 돌진 중 적중은 표시가 없어 보내지 않는다
        void IMucheolMultiDashListener.OnHit(int castInstanceId, int dashIndex, int hitCount)
        {
        }

        void IMucheolMultiDashListener.OnTruncated(int castInstanceId, int plannedDashCount, int completedDashCount)
        {
            _sink?.OnMultiDashTruncated(castInstanceId, plannedDashCount, completedDashCount);
        }

        void IMucheolMultiDashListener.OnCancelled(int castInstanceId, EBossInterruptReason reason)
        {
            _sink?.OnMultiDashCancelled(castInstanceId);
        }

        /****************************************
        *           P2 P07 Battlefield
        ****************************************/

        void IMucheolBattlefieldListener.OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction, float radius,
            float arcAngle, float telegraphSeconds)
        {
            _host.Face(direction);
            _host.BeginPhaseCast(castInstanceId);
            if (_sink == null)
            {
                return;
            }

            var start = new BossBattlefieldStart(_host.CombatId, castInstanceId, origin, direction, radius, arcAngle,
                _host.TickTime, telegraphSeconds);
            _sink.OnBattlefieldStarted(in start);
        }

        // 밀린 결과는 대상의 서버 이동이 자기 위치 동기화로 보이므로 따로 보내지 않는다
        void IMucheolBattlefieldListener.OnPushed(int castInstanceId, int combatId, float distance)
        {
        }

        void IMucheolBattlefieldListener.OnHit(int castInstanceId, int hitCount)
        {
            _sink?.OnBattlefieldHit(castInstanceId, _host.TickTime);
        }

        void IMucheolBattlefieldListener.OnCancelled(int castInstanceId, EBossInterruptReason reason)
        {
            _sink?.OnBattlefieldCancelled(castInstanceId);
        }

        /****************************************
        *            P1 P06 Charge
        ****************************************/

        // 이 시전의 예고와 돌진은 몸체가 기본 돌진 알림으로 받아 보내므로 여기서 시전 번호를 알리지 않는다. 알리면 종료 사건이 두 경로에 걸린다.
        // 충돌이 아니면 패턴이 충돌 값을 비워 두어(위치 0, 거리 0) 멈춘 위치는 몸체에서 읽는다. 패턴도 같은 몸체 위치로 충돌 위치를 채운다
        void IBossChargeOutcomeListener.OnChargeEnded(int castInstanceId, EBossChargeEnd end, BossChargeCollision collision,
            float recoverySeconds)
        {
            if (_sink == null)
            {
                return;
            }

            bool    isCollision = end == EBossChargeEnd.Collision;
            Vector3 position    = isCollision ? collision.Position : _host.Position;
            var     outcome     = new BossChargeOutcome(_host.CombatId, castInstanceId, isCollision, position,
                collision.TravelDistance, collision.StructureId, recoverySeconds, _host.TickTime);
            _sink.OnChargeEnded(in outcome);

            _chargeCastId      = castInstanceId;
            _chargeStructureId = collision.StructureId;
            _chargeServerTime  = _host.TickTime;
            _isChargeCollision = isCollision;

            if ((_hasHeldGroggy) && (_heldCastId == castInstanceId))
            {
                SendHeldGroggy();
            }
        }

        /****************************************
        *        P1 Charge Gimmick Groggy
        ****************************************/

        // 파괴 확인이 같은 시전의 돌진 결과보다 먼저 오면 결과를 보낼 때까지 미룬다. 표현은 충돌 반동 뒤에 그로기를 받아야 한다
        void IMucheolChargeGimmickListener.OnGroggyPending(int castInstanceId, int structureId, double confirmServerTime)
        {
            _hasHeldGroggy   = true;
            _heldCastId      = castInstanceId;
            _heldStructureId = structureId;
            _heldConfirmTime = confirmServerTime;

            if ((castInstanceId == CombatIds.NONE) || (castInstanceId == _chargeCastId))
            {
                SendHeldGroggy();
            }
        }

        // 결과를 기다리던 대기가 있으면 먼저 보내 대기 뒤에 시작이 가게 한다
        void IMucheolChargeGimmickListener.OnGroggyStarted(int castInstanceId, int structureId, double startServerTime,
            double endServerTime)
        {
            if (_hasHeldGroggy)
            {
                SendHeldGroggy();
            }

            if (_sink == null)
            {
                return;
            }

            double collision = ((castInstanceId == _pendingCastId) && (!double.IsNaN(_pendingCollision)))
                ? _pendingCollision
                : ReadCollisionTime(castInstanceId, structureId, startServerTime);
            var groggy = new BossChargeGroggy(_host.CombatId, castInstanceId, structureId, collision, startServerTime,
                endServerTime);
            _sink.OnChargeGroggyStarted(in groggy);
        }

        // 아직 보내지 않은 대기는 클라이언트가 모르므로 버리기만 한다
        void IMucheolChargeGimmickListener.OnGroggyCancelled(int castInstanceId)
        {
            if (_hasHeldGroggy)
            {
                _hasHeldGroggy = false;
                return;
            }

            _sink?.OnChargeGroggyCancelled(castInstanceId);
        }

        /****************************************
        *               Shared
        ****************************************/

        private void SendHeldGroggy()
        {
            _hasHeldGroggy    = false;
            _pendingCastId    = _heldCastId;
            _pendingCollision = ReadCollisionTime(_heldCastId, _heldStructureId, _heldConfirmTime);
            if (_sink == null)
            {
                return;
            }

            var groggy = new BossChargeGroggy(_host.CombatId, _heldCastId, _heldStructureId, _pendingCollision, double.NaN,
                double.NaN);
            _sink.OnChargeGroggyPending(in groggy);
        }

        // 부순 시전을 알면 그 시전의 충돌 결과를, 모르면 같은 구조물에 막힌 마지막 충돌을 쓴다. 둘 다 없으면 fallback(확인 시각)이다
        private double ReadCollisionTime(int castInstanceId, int structureId, double fallback)
        {
            if (!_isChargeCollision)
            {
                return fallback;
            }

            bool sameCast      = (castInstanceId != CombatIds.NONE) && (castInstanceId == _chargeCastId);
            bool sameStructure = (castInstanceId == CombatIds.NONE) && (structureId == _chargeStructureId);
            return ((sameCast) || (sameStructure)) ? _chargeServerTime : fallback;
        }

        private void AimStrikeChain(EBossStrikeChainKind kind, int castInstanceId, Vector3 direction)
        {
            _host.Face(direction);
            _sink?.OnStrikeChainAimChanged(kind, castInstanceId, direction, _host.TickTime);
        }

        private void LockStrikeChain(EBossStrikeChainKind kind, int castInstanceId, Vector3 direction)
        {
            _host.Face(direction);
            _sink?.OnStrikeChainDirectionLocked(kind, castInstanceId, direction);
        }

        private static EBossTelegraphShape ToTelegraphShape(EMucheolSequenceShape shape)
        {
            switch (shape)
            {
                case EMucheolSequenceShape.Line:
                    return EBossTelegraphShape.Line;
                case EMucheolSequenceShape.Circle:
                    return EBossTelegraphShape.Circle;
                default:
                    return EBossTelegraphShape.Fan;
            }
        }
    }
}
