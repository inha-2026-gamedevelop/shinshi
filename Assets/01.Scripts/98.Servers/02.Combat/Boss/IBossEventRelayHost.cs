// Unity
using UnityEngine;

namespace Shinshi.Combat
{
    // 무철과 월백 하드 2페이즈의 패턴 알림은 몸체가 아니라 중계가 받는다. 중계는 사건에 실을 값과 몸체에 남길 부작용(정면, 시전 번호)만
    // 이 창구로 읽고 쓰며, 몸체의 다른 상태는 보지 않는다
    /// <summary> 보스 사건 중계가 몸체에서 읽는 값과 몸체에 남기는 것, BossServer가 구현한다 </summary>
    public interface IBossEventRelayHost
    {
        /// <summary> 보스의 전투 번호 </summary>
        int CombatId { get; }

        /// <summary> 몸체의 현재 위치 </summary>
        Vector3 Position { get; }

        /// <summary> 이번 틱의 서버 시각, 알림에 시각이 없는 사건에 싣는다 </summary>
        double TickTime { get; }

        /// <summary> 지금 시전 중인 패턴 정의 ID </summary>
        string ActivePatternId { get; }

        /// <summary> 페이즈 번호가 바뀔 때마다 오르는 세대, 틱 안에서는 그 틱이 시작될 때의 값이다 </summary>
        ushort PhaseGeneration { get; }

        /// <summary> 몸체의 정면을 direction으로 돌린다, 수평 성분이 없으면 그대로 둔다 </summary>
        void Face(Vector3 direction);

        /// <summary> 전용 사건으로 예고한 시전의 번호를 알린다, 몸체가 이 시전의 정상 종료를 기본 사건으로 보낸다 </summary>
        void BeginPhaseCast(int castInstanceId);
    }
}
