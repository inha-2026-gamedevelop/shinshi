// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Combat
{
    /// <summary> 보스 네트워크 개체의 생성, 스폰, 상태 기록, 해제를 맡는다. 서버 빌드는 Mirror 스포너를 쓴다 </summary>
    public interface IBossObjectHost
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 보스 개체를 만들어 첫 상태를 싣고 스폰한 뒤 공격 사건을 상태로 잇는다, 실패하면 아무것도 남기지 않고 false </summary>
        bool TrySpawn(BossServer boss, out BossState state);

        /// <summary> 서버 몸체의 현재 값을 복제 상태에 기록한다, 매 틱 부른다 </summary>
        void Publish(BossServer boss, BossState state);

        /// <summary> 사건 연결을 끊고 개체를 내린다 </summary>
        void Despawn(BossServer boss, BossState state);
    }
}
