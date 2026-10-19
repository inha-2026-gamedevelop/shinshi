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
        private bool              _loadGraced;

        /****************************************
        *              Properties
        ****************************************/

        public int               ConnectionId => _connectionId;

        // 연결 때 한 번 만든 주소 묶음 키, 주소별 대기 상한을 셀 때만 쓴다. 표가 직접 넣은 끊기 대기는 None이다
        public AuthAddressKey    Address      => _address;
        public int               RequestId    => _requestId;
        public double            Deadline     => _deadline;
        public EPendingAuthStage Stage        => _stage;

        // 서버 맵 로드 유예로 마감이 늘어난 항목, 로드가 끝나면 정상 마감으로 되돌린다
        public bool              LoadGraced   => _loadGraced;

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
            _loadGraced   = false;
        }

        // 요청을 받은 뒤에는 검증 마감을 새로 준다. 요청 대기 마감(1초)을 그대로 쓰면 백엔드 교환이 끝나기 전에 끊긴다
        public void BeginVerify(int requestId, double deadline)
        {
            _requestId  = requestId;
            _deadline   = deadline;
            _stage      = EPendingAuthStage.Verifying;
            _loadGraced = false;
        }

        // 검증 뒤 서버 맵 전환을 기다리는 동안만 마감을 늘린다
        public void ExtendDeadline(double deadline)
        {
            _deadline = deadline;
        }

        // 맵 로드 중에는 Mirror가 받은 메시지를 처리하지 않으므로 요청 대기와 검증 대기의 마감을 로드 상한까지 미룬다. 줄이지는 않는다
        public bool GraceForLoad(double graceUntil)
        {
            if ((_stage == EPendingAuthStage.Closing) || (graceUntil <= _deadline))
            {
                return false;
            }

            _deadline   = graceUntil;
            _loadGraced = true;
            return true;
        }

        // 로드가 끝났다, 유예로 늘어난 항목만 정상 마감(요청 1초, 검증 5초)으로 되돌리고 더 짧은 쪽을 쓴다
        public void EndLoadGrace(double waitingDeadline, double verifyingDeadline)
        {
            if (!_loadGraced)
            {
                return;
            }

            _loadGraced = false;
            double limit = (_stage == EPendingAuthStage.Waiting) ? waitingDeadline : verifyingDeadline;
            if (limit < _deadline)
            {
                _deadline = limit;
            }
        }

        public void BeginClosing(double deadline)
        {
            _requestId  = 0;
            _deadline   = deadline;
            _stage      = EPendingAuthStage.Closing;
            _loadGraced = false;
        }
    }
}
