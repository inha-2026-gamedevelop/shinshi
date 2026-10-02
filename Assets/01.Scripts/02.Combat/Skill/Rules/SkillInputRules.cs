// Unity
using UnityEngine;
using UnityEngine.AI;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Combat
{
    /// <summary> 기술 키 해석, 지점 지정 전이와 지정점 사전 검사, 서버 TryCast와 같은 기준을 소유 클라이언트가 미리 쓴다 </summary>
    public static class SkillInputRules
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> Q/W/E/R은 모든 직업의 일반 기술, Z/X/C는 신령사만의 강림이다 </summary>
        public static bool TryMapKey(EPlayerClass playerClass, ESkillInputKey key, out ESkillRequestKind kind, out byte slot)
        {
            kind = ESkillRequestKind.Regular;
            slot = 0;
            switch (key)
            {
                case ESkillInputKey.Q:
                    slot = Constants.Combat.SKILL_SLOT_Q;
                    return true;
                case ESkillInputKey.W:
                    slot = Constants.Combat.SKILL_SLOT_W;
                    return true;
                case ESkillInputKey.E:
                    slot = Constants.Combat.SKILL_SLOT_E;
                    return true;
                case ESkillInputKey.R:
                    slot = Constants.Combat.SKILL_SLOT_R;
                    return true;
                case ESkillInputKey.Z:
                case ESkillInputKey.X:
                case ESkillInputKey.C:
                    break;
                default:
                    return false;
            }

            if (playerClass != EPlayerClass.Summoner)
            {
                return false;
            }

            kind = ESkillRequestKind.Ascension;
            slot = (byte)(key - ESkillInputKey.Z);
            return true;
        }

        /// <summary> 카탈로그에서 조준 방식, 최대 지정 거리, 범위 반경을 읽는다, 직업에 없는 기술이면 거짓 </summary>
        public static bool TryGetAim(SkillCatalogSO catalog, EPlayerClass playerClass, ESkillRequestKind kind, int slot,
            out ESkillAimMode aimMode, out float castRange, out float radius)
        {
            aimMode   = ESkillAimMode.Direction;
            castRange = 0f;
            radius    = 0f;
            if (catalog == null)
            {
                return false;
            }

            if (kind == ESkillRequestKind.Regular)
            {
                if (!catalog.TryGetSkillBySlot(playerClass, slot, out SkillCatalogSO.SkillDefinition skill))
                {
                    return false;
                }

                aimMode   = skill.Area.AimMode;
                castRange = skill.Area.CastRange;
                radius    = skill.Area.Radius;
                return true;
            }

            if ((kind != ESkillRequestKind.Ascension) || (playerClass != EPlayerClass.Summoner))
            {
                return false;
            }

            // 강림은 모두 지정 지점 기술이다
            for (int i = 0; i < catalog.Ascensions.Count; ++i)
            {
                SkillCatalogSO.AscensionDefinition ascension = catalog.Ascensions[i];
                if ((ascension != null) && (ascension.Slot == slot))
                {
                    aimMode   = ESkillAimMode.Point;
                    castRange = ascension.CastRange;
                    radius    = ascension.Radius;
                    return true;
                }
            }

            return false;
        }

        /// <summary> 서버가 받아 둘 수 있는 시점인지, 보관 시간보다 이르면 보내 봐야 쿨다운 거절만 온다 </summary>
        public static bool IsReady(double readyAt, double now, double bufferSeconds)
        {
            return (now + bufferSeconds) >= readyAt;
        }

        /// <summary> 기술 키 입력에 대한 다음 행동, 같은 지점 기술을 다시 누르면 지정을 유지한다 </summary>
        public static ESkillInputDecision DecideKey(ESkillAimMode aimMode, bool isReady, bool isTargeting,
            ESkillRequestKind targetingKind, int targetingSlot, ESkillRequestKind kind, int slot)
        {
            if (!isReady)
            {
                return ESkillInputDecision.Ignore;
            }

            if (aimMode != ESkillAimMode.Point)
            {
                return ESkillInputDecision.CastNow;
            }

            if ((isTargeting) && (targetingKind == kind) && (targetingSlot == slot))
            {
                return ESkillInputDecision.Ignore;
            }

            return ESkillInputDecision.BeginTargeting;
        }

        /// <summary> 전투 중 흔히 나는 거절인지, 아니면 헤더, 세션처럼 연결 상태를 의심해야 하는 거절인지 </summary>
        public static bool IsRoutineRejection(ERejectReason reason)
        {
            switch (reason)
            {
                case ERejectReason.Dead:
                case ERejectReason.WrongPhase:
                case ERejectReason.Cooldown:
                case ERejectReason.InsufficientEnergy:
                case ERejectReason.InvalidTarget:
                case ERejectReason.OutOfRange:
                case ERejectReason.TooEarly:
                case ERejectReason.NoCapacity:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary> 지정점의 수평 거리를 최대치로 제한한다, 높이는 원래 지점을 유지한다 </summary>
        public static Vector3 ClampToRange(Vector3 origin, Vector3 target, float maxDistance)
        {
            float deltaX = target.x - origin.x;
            float deltaZ = target.z - origin.z;
            float sqr    = (deltaX * deltaX) + (deltaZ * deltaZ);
            float limit  = Mathf.Max(0f, maxDistance);
            if (sqr <= (limit * limit))
            {
                return target;
            }

            float scale = limit / Mathf.Sqrt(sqr);
            return new Vector3(origin.x + (deltaX * scale), target.y, origin.z + (deltaZ * scale));
        }

        /// <summary> 커서 지점을 클라이언트 지정 거리로 제한한다, 서버 사거리보다 여유만큼 안쪽이다 </summary>
        public static Vector3 ClampTargetPoint(Vector3 origin, Vector3 cursor, float castRange)
        {
            return ClampToRange(origin, cursor, castRange - Constants.Player.SKILL_POINT_RANGE_MARGIN);
        }

        /// <summary> 커서를 제한하고 서버가 받을 지점인지 미리 본다, 거짓이어도 point는 표시할 위치다 </summary>
        public static bool TryResolveTargetPoint(Vector3 origin, Vector3 cursor, float castRange, CombatLineOfSight lineOfSight,
            out Vector3 point)
        {
            // 서버 TryCast의 지점 검증 순서를 따른다. 내비 메시 보정, 높이 차, 보정 뒤 사거리, 지형 시야
            point = ClampTargetPoint(origin, cursor, castRange);
            if (!NavMesh.SamplePosition(point, out NavMeshHit hit, Constants.Player.MOVE_DESTINATION_SAMPLE_RADIUS, NavMesh.AllAreas))
            {
                return false;
            }

            Vector3 sampled = hit.position;
            if (!BasicAttackRules.IsWithinHeight(origin, sampled, Constants.Combat.MAX_ATTACK_HEIGHT_DIFFERENCE))
            {
                return false;
            }

            float deltaX = sampled.x - origin.x;
            float deltaZ = sampled.z - origin.z;
            if (((deltaX * deltaX) + (deltaZ * deltaZ)) > (castRange * castRange))
            {
                return false;
            }

            if ((lineOfSight != null) && (lineOfSight.IsBlocked(origin, sampled)))
            {
                return false;
            }

            point = sampled;
            return true;
        }
    }
}
