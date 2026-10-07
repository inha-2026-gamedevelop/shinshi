namespace Shinshi.UI
{
    /// <summary> 거래 창 버튼과 편집 가능 여부, 단계와 내 확정, 수락 상태로 정해진다 </summary>
    public readonly struct TradeButtonStates
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly bool _canEdit;
        private readonly bool _canToggleLock;
        private readonly bool _lockIsRelease;
        private readonly bool _canAccept;
        private readonly bool _canCancel;
        private readonly bool _isBusy;

        /****************************************
        *              Properties
        ****************************************/

        // 품목을 넣고 빼고 엽전을 바꿀 수 있는지. 바꾸면 양쪽 확정이 풀린다
        public bool CanEdit       => _canEdit;
        public bool CanToggleLock => _canToggleLock;

        // 확정 버튼이 지금은 확정 풀기를 뜻하는지
        public bool LockIsRelease => _lockIsRelease;
        public bool CanAccept     => _canAccept;
        public bool CanCancel     => _canCancel;

        // 서버가 실행 중이라 모든 조작이 막힌 상태
        public bool IsBusy        => _isBusy;

        /****************************************
        *                Methods
        ****************************************/

        public TradeButtonStates(bool canEdit, bool canToggleLock, bool lockIsRelease, bool canAccept, bool canCancel, bool isBusy)
        {
            _canEdit       = canEdit;
            _canToggleLock = canToggleLock;
            _lockIsRelease = lockIsRelease;
            _canAccept     = canAccept;
            _canCancel     = canCancel;
            _isBusy        = isBusy;
        }
    }
}
