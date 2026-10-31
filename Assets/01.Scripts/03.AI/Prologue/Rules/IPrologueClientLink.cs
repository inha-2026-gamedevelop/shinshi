// System
using System;

namespace Shinshi.AI
{
    /// <summary> 클라이언트가 서버에서 받은 프롤로그 상태를 읽고 서버에 알리는 창구, 실제 구현은 동기화 상태(PrologueState)를 감싸고 시험은 가짜로 대신한다 </summary>
    public interface IPrologueClientLink
    {
        // 상태 값은 연결부가 매 프레임 읽는다. 값이 바뀔 때 알리는 이벤트는 두지 않는다

        EPrologueStage Stage { get; }

        // 서버가 정한 현재 안내 한 가지
        EForestHint Hint { get; }

        // 부적에 닿아 조사 화면을 띄워야 하는 동안 참이다
        bool IsTalismanOpen { get; }

        // 월백이 열려 교전 중이다
        bool IsWolbaekOpen { get; }

        // 남은 임시 물약 수
        int PotionCount { get; }

        // 숲에서 체력 0으로 회복 처리됐다
        event Action Revived;

        // 조사 화면을 닫았다
        void SendTalismanConfirmed();

        // 도입 또는 마무리 일러스트 구간이 끝났다
        void SendIllustrationFinished();

        // 월백 등장 일러스트를 닫거나 건너뛰었다
        void SendWolbaekIntroFinished();
    }
}
