// System
using System.Collections.Generic;

namespace Shinshi.AI
{
    // ICombatTargetQuery는 살아 있는 대상만 돌려줘서 쓰러진 참가자를 필수 참가자로 세지 못한다. 기믹은 쓰러진 사람과 접속이 끊긴 사람까지
    // 같은 전투 번호로 계속 따라가야 하므로 출정 명단을 읽는 별도 창구를 둔다. 구현은 서버 연결이 하며 아직 없다.
    // 구현이 없으면 이 창구가 필요한 기믹은 연결하지 않는다
    /// <summary> 출정 명단의 참가자 조회, 쓰러진 참가자와 미접속 참가자를 포함한다 </summary>
    public interface IBossParticipantRoster
    {
        /// <summary> 이 전투의 참가 플레이어 전투 번호를 모두 담는다, 쓰러진 사람과 미접속자를 포함하고 소환물과 이탈이 확정된 참가자는 뺀다. 개수를 돌려준다 </summary>
        int CollectParticipants(List<int> combatIds);

        /// <summary> 한 참가자의 현재 상태, 명단에서 사라졌으면 거짓이다 </summary>
        bool TryGetParticipant(int combatId, out BossParticipantInfo info);
    }
}
