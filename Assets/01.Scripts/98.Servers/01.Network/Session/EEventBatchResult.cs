namespace Shinshi.Network
{
    /// <summary> 세션 이벤트 배치 저장 결과 </summary>
    public enum EEventBatchResult
    {
        // API가 배치 전체를 내구 저장했다
        Committed = 0,

        // 시간 초과, 연결 실패, 5xx, 처리 중, 임대 없음처럼 같은 배치를 다시 보내면 결과가 바뀔 수 있다
        Transient = 1,

        // 400이나 저장된 거절이라 같은 배치를 몇 번 보내도 같다. 큐는 멈추고 출정은 서비스 장애 취소로 끝난다
        Terminal  = 2
    }
}
