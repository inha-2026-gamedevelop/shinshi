// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Combat
{
    // 월백 P02, P03, P06은 장판과 투사체를 남기지 않아 정리할 것이 없다. 서버 한 사이클 시험용 자리다
    // TODO(기웅): 지속물 관리자가 생기면 보스 생성에서 이 클래스 대신 넘긴다
    /// <summary> 지속물이 없는 보스용 수명 관리자, 아무것도 정리하지 않고 남은 장판은 항상 0이다 </summary>
    public sealed class EmptyBossEffectLifetime : IBossEffectLifetime
    {
        /****************************************
        *                Methods
        ****************************************/

        public void Cancel(EBossInterruptReason reason, int castInstanceId)
        {
        }

        public int ActiveAreaCount(string patternId)
        {
            return 0;
        }
    }
}
