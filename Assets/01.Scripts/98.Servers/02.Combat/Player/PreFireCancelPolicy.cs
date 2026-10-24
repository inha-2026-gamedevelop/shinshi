using System;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 취소 사유별로 지울 상태와 보관 입력에 돌려줄 거절 사유를 정한 표, None이면 알리지 않고 조용히 버린다 </summary>
    public readonly struct PreFireCancelPolicy
    {
        // 연결 유지 Detach는 날아간 화살과 회피 진행을 남기고, Shutdown은 개체가 사라지므로 모두 지운다.
        // 기술 서버의 Detach, Shutdown, Cancel, 사망 수명은 여기서 합치지 않고 PlayerServer가 각자 따로 부른다

        /****************************************
        *              Properties
        ****************************************/

        public EPreFireTargets Targets           { get; }
        public ERejectReason   SkillRejectReason { get; }
        public ERejectReason   DodgeRejectReason { get; }

        /****************************************
        *                Methods
        ****************************************/

        private PreFireCancelPolicy(EPreFireTargets targets, ERejectReason skillReject, ERejectReason dodgeReject)
        {
            Targets           = targets;
            SkillRejectReason = skillReject;
            DodgeRejectReason = dodgeReject;
        }

        public bool Has(EPreFireTargets target)
        {
            return (Targets & target) != EPreFireTargets.None;
        }

        public static PreFireCancelPolicy Get(EPreFireCancelReason reason)
        {
            switch (reason)
            {
                case EPreFireCancelReason.Detach:
                    return new PreFireCancelPolicy(
                        EPreFireTargets.PendingMove | EPreFireTargets.PendingAttack | EPreFireTargets.Windup
                        | EPreFireTargets.PendingSkill | EPreFireTargets.ReviveRequest | EPreFireTargets.PendingDodge
                        | EPreFireTargets.Path,
                        ERejectReason.None, ERejectReason.None);
                case EPreFireCancelReason.Shutdown:
                    return new PreFireCancelPolicy(
                        EPreFireTargets.PendingMove | EPreFireTargets.PendingAttack | EPreFireTargets.Windup
                        | EPreFireTargets.FiredArrows | EPreFireTargets.PendingSkill | EPreFireTargets.PendingDodge
                        | EPreFireTargets.ActiveDodge | EPreFireTargets.Path,
                        ERejectReason.None, ERejectReason.None);
                case EPreFireCancelReason.SkillCommit:
                    return new PreFireCancelPolicy(
                        EPreFireTargets.PendingMove | EPreFireTargets.PendingAttack | EPreFireTargets.Windup
                        | EPreFireTargets.Combo | EPreFireTargets.PendingSkill | EPreFireTargets.Path,
                        ERejectReason.WrongPhase, ERejectReason.None);
                case EPreFireCancelReason.SkillBuffered:
                    return new PreFireCancelPolicy(EPreFireTargets.PendingAttack, ERejectReason.None, ERejectReason.None);
                case EPreFireCancelReason.SkillDodgeCancel:
                case EPreFireCancelReason.SkillCombatStop:
                    return new PreFireCancelPolicy(EPreFireTargets.PendingSkill, ERejectReason.WrongPhase, ERejectReason.None);
                case EPreFireCancelReason.Dodge:
                    return new PreFireCancelPolicy(
                        EPreFireTargets.PendingMove | EPreFireTargets.PendingAttack | EPreFireTargets.Windup
                        | EPreFireTargets.Combo | EPreFireTargets.Path,
                        ERejectReason.None, ERejectReason.None);
                case EPreFireCancelReason.Death:
                    return new PreFireCancelPolicy(
                        EPreFireTargets.PendingMove | EPreFireTargets.PendingAttack | EPreFireTargets.Windup
                        | EPreFireTargets.Combo | EPreFireTargets.PendingSkill | EPreFireTargets.PendingDodge
                        | EPreFireTargets.ActiveDodge | EPreFireTargets.Path,
                        ERejectReason.Dead, ERejectReason.Dead);
                case EPreFireCancelReason.ControlInterrupt:
                    return new PreFireCancelPolicy(
                        EPreFireTargets.PendingMove | EPreFireTargets.PendingDodge | EPreFireTargets.ActiveDodge
                        | EPreFireTargets.Path,
                        ERejectReason.None, ERejectReason.Disabled);
                case EPreFireCancelReason.StunInterrupt:
                    return new PreFireCancelPolicy(
                        EPreFireTargets.PendingAttack | EPreFireTargets.Windup | EPreFireTargets.Combo
                        | EPreFireTargets.PendingSkill,
                        ERejectReason.Disabled, ERejectReason.None);
                case EPreFireCancelReason.MoveInput:
                    return new PreFireCancelPolicy(
                        EPreFireTargets.PendingAttack | EPreFireTargets.ComboImmunity,
                        ERejectReason.None, ERejectReason.None);
                case EPreFireCancelReason.MoveInputWindupCancel:
                    return new PreFireCancelPolicy(
                        EPreFireTargets.PendingAttack | EPreFireTargets.Windup | EPreFireTargets.ComboImmunity,
                        ERejectReason.None, ERejectReason.None);
                case EPreFireCancelReason.MoveInputHeld:
                    return new PreFireCancelPolicy(EPreFireTargets.PendingAttack, ERejectReason.None, ERejectReason.None);
                default:
                    throw new ArgumentOutOfRangeException(nameof(reason), reason, null);
            }
        }
    }
}
