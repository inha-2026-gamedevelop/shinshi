// Unity
using UnityEngine;

namespace Shinshi.Combat
{
    /// <summary> 안전 복귀 지점 판정이 묻는 위험 영역 조회 창구, 서버 틱에서만 부른다 </summary>
    public interface IReviveHazardQuery
    {
        /// <summary> 지점이 지금 피해 장판, 공격 예고, 진행 중 투사체 경로 안에 있으면 true </summary>
        bool IsHazardous(Vector3 point, double serverTime);
    }
}
