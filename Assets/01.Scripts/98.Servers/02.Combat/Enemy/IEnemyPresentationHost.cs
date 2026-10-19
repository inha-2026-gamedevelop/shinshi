// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 일반 적의 종류, 행동, 공격 예고, 출현 예고, 투사체를 클라이언트에 보이게 하는 서버 표현 창구, IEnemyObjectHost와 따로 구현한다 </summary>
    public interface IEnemyPresentationHost
    {
        // 웨이브 구현(적 담당)이 부른다. 판정은 서버가 이미 끝낸 값만 넘기고 표현은 그 값을 그린다.
        // 시각은 모두 NetworkTime.time 기준 서버 시각이다

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 다음 TrySpawn에서 이 전투 번호의 적에 붙일 종류를 알린다, TrySpawn 전에 부른다. 부르지 않으면 근접이다 </summary>
        void DescribeEnemy(int combatId, EEnemyRole role);

        /// <summary> 행동 상태가 바뀔 때 부른다 </summary>
        void SetBehavior(NetworkIdentity instance, EEnemyState behavior);

        /// <summary> 공격 예고를 시작한다, width는 부채꼴이면 각도(도), 그 밖에는 폭(m) </summary>
        void BeginTelegraph(NetworkIdentity instance, EEnemyAttackShape shape, Vector3 origin, Vector3 direction, float range, float width,
            double startServerTime, double endServerTime);

        /// <summary> 공격 예고를 끝낸다, 취소, 타격, 사망 모두 같다 </summary>
        void EndTelegraph(NetworkIdentity instance);

        /// <summary> 적이 나타날 자리에 바닥 표시를 띄운다, 번호를 돌려주고 실패하면 0이다 </summary>
        int ReserveSpawnPreview(Vector3 position, EEnemyRole role, float radius, double startServerTime, double endServerTime);

        /// <summary> 표시를 내린다, 적이 실제로 나타난 때 부른다 </summary>
        void MaterializeSpawnPreview(int previewId);

        /// <summary> 표시를 내린다, 예약이 취소됐을 때 부른다 </summary>
        void CancelSpawnPreview(int previewId);

        /// <summary> 열려 있는 표시를 모두 내린다, 웨이브 시작, 종료, 정리 때 부른다 </summary>
        void CancelAllSpawnPreviews();

        /// <summary> BossProjectileManager 생성자에 넘길 투사체 알림 어댑터를 만든다 </summary>
        IBossProjectileListener CreateProjectileListener();
    }
}
