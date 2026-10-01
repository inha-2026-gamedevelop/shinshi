// System
using System;

namespace Shinshi.Common
{
    /// <summary> UI가 로컬 플레이어를 읽는 유일한 창구, 서버 상태를 바꾸는 멤버는 두지 않는다 </summary>
    public interface ILocalPlayerView
    {
        // 캐릭터 스폰 전, 연결이 끊긴 동안에는 LocalPlayerSnapshot.Unbound를 돌려준다
        LocalPlayerSnapshot Snapshot { get; }

        // 체력, 기력, 공명처럼 계속 변하는 값은 이벤트로 보내지 않는다. 표시하는 동안 매 프레임 이것을 읽는다.
        // 묶인 캐릭터가 없으면 LocalPlayerGauges.Empty다
        LocalPlayerGauges ReadGauges();

        // 이산 변화(생사, 직업, revision, 바인딩)를 받는다.
        // 등록하는 즉시 현재 Snapshot으로 handler를 한 번 부르므로 늦게 열린 화면도 현재 상태에서 시작한다.
        // 재접속이나 캐릭터 교체로 인스턴스가 바뀌면 구현이 구독을 새 인스턴스로 옮기고 BindingVersion이 바뀐 스냅샷을 보낸다.
        // UI는 다시 구독하지 않는다.
        //
        // 해제 책임은 구독한 쪽에 있다. MonoBehaviour는 OnEnable에서 등록하고 OnDisable에서 같은 메서드로 해제한다.
        // 람다는 해제할 수 없으므로 메서드 그룹을 넘긴다. 같은 handler를 두 번 등록해도 한 번만 불린다
        void SubscribeSnapshot(Action<LocalPlayerSnapshot> handler);

        // 등록하지 않은 handler를 넘겨도 아무 일도 하지 않는다. handler 안에서 해제해도 안전해야 한다
        void UnsubscribeSnapshot(Action<LocalPlayerSnapshot> handler);
    }
}
