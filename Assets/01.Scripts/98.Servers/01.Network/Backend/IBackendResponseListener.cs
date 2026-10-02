namespace Shinshi.Network
{
    /// <summary> 백엔드 응답을 받는 쪽, 게이트웨이가 메인 스레드의 Pump 안에서만 부른다 </summary>
    public interface IBackendResponseListener
    {
        // 요청을 보낸 뒤 상황이 바뀌었을 수 있다. 받는 쪽은 RequestId로 자기 요청인지, 대상이 아직 살아 있는지,
        // 단계와 revision이 그대로인지 다시 확인한 뒤 결과를 적용한다
        void OnBackendResponse(in BackendResponse response);
    }
}
