// 서드파티
using Mirror;

namespace Shinshi.Network
{
    /// <summary> 클라이언트가 프롤로그 화면이 끝났다고 서버에 알리는 사건 종류, 저장되지 않는 와이어 값이라 번호를 바꾸지 않는다 </summary>
    public enum EPrologueClientEvent : byte
    {
        // 부적 조사 화면을 닫았다
        TalismanConfirmed    = 0,

        // 도입 또는 마무리 일러스트 구간이 끝났다, 어느 쪽인지는 서버의 현재 단계로 가른다
        IllustrationFinished = 1,

        // 월백 등장 일러스트를 닫거나 건너뛰었다
        WolbaekIntroFinished = 2
    }

    // 서버는 인증된 연결의 프롤로그 참가자만 받고, 단계가 맞지 않는 사건은 진행기가 무시한다
    /// <summary> 클라이언트가 프롤로그 화면 사건을 서버에 알리는 요청 </summary>
    public readonly struct PrologueEventMessage : NetworkMessage
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly EPrologueClientEvent _event;

        /****************************************
        *              Properties
        ****************************************/

        public EPrologueClientEvent Event => _event;

        /****************************************
        *                Methods
        ****************************************/

        public PrologueEventMessage(EPrologueClientEvent clientEvent)
        {
            _event = clientEvent;
        }
    }
}
