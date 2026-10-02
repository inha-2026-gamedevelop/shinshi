namespace Shinshi.Network
{
    /// <summary> 인증 대기 연결 하나, PendingAuthTable이 배열로 들고 있다 </summary>
    public struct PendingAuthEntry
    {
        // 테이블이 제자리에서 고치는 값 묶음이라 readonly로 두지 않는다. 밖으로는 복사본만 나간다

        /****************************************
        *                Fields
        ****************************************/

        private int               _connectionId;
        private AuthAddressKey    _address;
        private int               _requestId;
        private double            _deadline;
        private EPendingAuthStage _stage;

        /****************************************
        *              Properties
        ****************************************/

        public int               ConnectionId => _connectionId;

        // 연결 때 한 번 만든 주소 묶음 키, 주소별 대기 상한을 셀 때만 쓴다. 표가 직접 넣은 끊기 대기는 None이다
        public AuthAddressKey    Address      => _address;
        public int               RequestId    => _requestId;
        public double            Deadline     => _deadline;
        public EPendingAuthStage Stage        => _stage;

        /****************************************
        *                Methods
        ****************************************/

        public PendingAuthEntry(int connectionId, AuthAddressKey address, double deadline)
        {
            _connectionId = connectionId;
            _address      = address;
            _requestId    = 0;
            _deadline     = deadline;
            _stage        = EPendingAuthStage.Waiting;
        }

        // 요청을 받은 뒤에는 검증 마감을 새로 준다. 요청 대기 마감(1초)을 그대로 쓰면 백엔드 교환이 끝나기 전에 끊긴다
        public void BeginVerify(int requestId, double deadline)
        {
            _requestId = requestId;
            _deadline  = deadline;
            _stage     = EPendingAuthStage.Verifying;
        }

        public void BeginClosing(double deadline)
        {
            _requestId = 0;
            _deadline  = deadline;
            _stage     = EPendingAuthStage.Closing;
        }
    }
}
