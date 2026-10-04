// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Combat
{
    /// <summary> 일반 적 네트워크 개체의 스폰, 위치 기록, 해제를 맡는다. 웨이브 구현(적 담당)이 적을 만들고 지울 때 부른다 </summary>
    public interface IEnemyObjectHost
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 적 개체를 몸체 위치에 스폰한다, 실패하면 아무것도 남기지 않고 false </summary>
        bool TrySpawn(EnemyServer enemy, out NetworkIdentity instance);

        /// <summary> 서버 몸체의 현재 위치와 방향을 개체에 기록한다, 매 틱 부른다 </summary>
        void Publish(EnemyServer enemy, NetworkIdentity instance);

        /// <summary> 개체를 내린다, 다음 스폰에 다시 쓴다 </summary>
        void Despawn(NetworkIdentity instance);
    }
}
