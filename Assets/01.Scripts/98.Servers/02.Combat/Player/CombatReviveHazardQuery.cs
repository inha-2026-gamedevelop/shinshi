// Unity
using UnityEngine;

namespace Shinshi.Combat
{
    /// <summary> 전투 런타임이 안전 복귀 지점 판정에 넘기는 위험 조회, 아직 연결된 위험 원천이 없어 항상 안전으로 답한다 </summary>
    public sealed class CombatReviveHazardQuery : IReviveHazardQuery
    {
        // 보스 장판, 예고, 투사체는 보스 서버와 두뇌의 지속물이 들고 있다. 어떤 지속물을 위험으로 볼지와
        // 판정 모양(원, 부채꼴, 경로 폭)을 보스 담당과 맞추기 전에는 지점을 걸러 내지 않는다. 적 거리 조건은 선택기가 따로 본다
        // TODO(기웅): 보스 장판, 공격 예고, 진행 중 투사체 경로를 이 조회에 연결하는 방식을 합의한 뒤 구현한다

        /****************************************
        *                Methods
        ****************************************/

        public bool IsHazardous(Vector3 point, double serverTime)
        {
            return false;
        }
    }
}
