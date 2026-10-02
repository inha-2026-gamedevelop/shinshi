// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 연결이 끊긴 참가자의 보존 상태를 어떻게 다룰지 정한다, 만료 때 명단을 닫을지와 재접속 때 기술 상태를 이어 쓸지 </summary>
    public static class RetentionRules
    {
        // 명단을 닫으면 개인 실패 정산과 재합류 금지로 이어진다. 그래서 정산할 출정이 진행 중인 던전에서만 닫는다.
        // 마을은 출정이 없고, 던전 시작 전 끊김은 예약 만료까지 기다리므로 두 경우 모두 보존 상태만 버린다.
        //
        // 기술 상태(쿨다운, 기력, 공명)는 직업의 기술표에 묶여 있어 다른 직업의 개체에 이어 쓰면 의미가 없다.
        // 같은 참가자는 같은 캐릭터라 직업이 바뀔 수 없지만, 바뀐 채 들어오면 API 직업이 정본이므로 보존 쪽을 버린다

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 보존 기록 만료로 명단을 닫아야 하면 참, isDungeon은 출정 호스트가 있는 던전 역할인지 </summary>
        public static bool ClosesRosterOnExpiry(bool isDungeon, bool sessionStarted)
        {
            return (isDungeon) && (sessionStarted);
        }

        /// <summary> 보존된 기술 상태나 남은 개체를 새 연결에 이어 써도 되면 참, API 직업과 같을 때만 허용한다 </summary>
        public static bool MatchesBoundClass(EPlayerClass boundClass, EPlayerClass retainedClass)
        {
            return boundClass == retainedClass;
        }
    }
}
