namespace Shinshi.Network
{
    /// <summary> 접속 토큰 검증, 출시 서버는 백엔드 교환 검증기를 쓰고 에디터와 개발 빌드는 개발 검증기를 고를 수 있다 </summary>
    public interface IJoinTokenVerifier
    {
        // 결과는 반드시 Unity 메인 스레드에서 listener.OnJoinTokenVerified로 돌려준다. 동기 검증기는 이 호출 안에서 바로 돌려줘도 된다.
        // 백엔드 검증기는 HTTP 응답을 메인 스레드로 넘긴 뒤 부르고, 인증기는 그 시점에 연결과 참가자 상태를 다시 확인한다
        void Verify(int requestId, string joinToken, IJoinTokenVerifyListener listener);

        // 인증기가 그 검증 결과로 연결을 받아들였을 때 부른다. 백엔드 검증기는 입장이 거절된 교환 결과만 잠시 기억해
        // 같은 토큰의 재시도를 받아 주므로, 입장한 토큰은 여기서 잊어 다시 쓰지 못하게 한다
        void OnAdmitted(int requestId);
    }
}
