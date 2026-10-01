// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 서버 판정이 읽는 전투 대상의 현재 상태, 플레이어와 소환물과 적이 구현한다. 읽기 전용이며 체력 변경은 IDamageResolver로만 한다 </summary>
    public interface ICombatTarget
    {
        // 전투 수명 동안 유일한 서버 발급 번호. 재접속으로 netId가 바뀌어도 같은 참가자는 같은 번호를 유지한다
        int CombatId { get; }

        ECombatFaction Faction { get; }

        // 적은 Alive와 Dead만 쓴다. 미접속 중이어도 살아 있으면 Alive다
        EParticipantLifeState LifeState { get; }

        // 소환물은 보스 타깃 선택과 주변 인원 계산에서 빠지지만 범위, 경로 피해는 받는다
        bool IsSummon { get; }

        // 소환물이면 주인의 CombatId, 아니면 CombatIds.NONE
        int OwnerCombatId { get; }

        // 서버 시뮬레이션 좌표, 클라이언트 보간 위치가 아니다
        Vector3 Position { get; }

        // 수평면 정면 단위 벡터. 전방 반격과 후방 피격, 보스 방향 배율의 기준
        Vector3 Forward { get; }
    }
}
