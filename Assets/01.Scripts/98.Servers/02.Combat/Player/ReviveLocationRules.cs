// Unity
using UnityEngine;

namespace Shinshi.Combat
{
    /// <summary> 부활 위치를 어디서 정할지 가린다. 일반전은 서버가 기록한 사망 위치, 협동 보스는 안전 복귀 지점이다 </summary>
    public enum EReviveLocationPlan
    {
        SafePoint,
        DeathPosition,
        NearbyWalkable
    }

    /// <summary> 부활 위치 계획을 정하는 순수 규칙, 엔진 상태를 읽지 않고 판단만 한다 </summary>
    public static class ReviveLocationRules
    {
        // 일반전은 안전 지점 이동도 위험 종료 대기도 없이 죽은 자리 그대로 되살아난다.
        // 사망 위치를 서버가 사망 순간에 따로 적어 두는 이유는 사망 뒤에도 기술 이동이 위치를 옮길 수 있고,
        // 재접속으로 새로 만든 개체는 스폰 위치에서 시작하기 때문이다.
        // 좌표 자체가 무효인 경우(지형 소실, 범위 밖)의 최종 처리는 기획이 아직 정하지 않았다.
        // 지금은 값이 유한하지 않거나 기록이 없을 때만 무효로 보고 기존 NavMesh 근처 지점으로 대신한다

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 좌표 세 값이 모두 유한하면 참 </summary>
        public static bool IsUsablePosition(Vector3 position)
        {
            return (IsFinite(position.x)) && (IsFinite(position.y)) && (IsFinite(position.z));
        }

        /// <summary> 보스 세션이면 안전 지점, 일반전이면 사망 위치, 일반전인데 사망 위치가 무효면 근처 걸을 수 있는 지점 </summary>
        public static EReviveLocationPlan Decide(bool isBossSession, bool hasDeathPosition, Vector3 deathPosition)
        {
            if (isBossSession)
            {
                return EReviveLocationPlan.SafePoint;
            }

            if ((hasDeathPosition) && (IsUsablePosition(deathPosition)))
            {
                return EReviveLocationPlan.DeathPosition;
            }

            return EReviveLocationPlan.NearbyWalkable;
        }

        private static bool IsFinite(float value)
        {
            return (!float.IsNaN(value)) && (!float.IsInfinity(value));
        }
    }
}
