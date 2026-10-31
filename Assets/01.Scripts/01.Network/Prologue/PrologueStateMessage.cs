// 서드파티
using Mirror;

namespace Shinshi.Network
{
    // 프롤로그 1인 출정이라 접속 연결 하나에만 보낸다. 값이 바뀔 때와 (재)접속 때만 보내고, 클라이언트는 마지막 값을 보관해 매 프레임 읽는다.
    // 남은 시간은 화면에 표시하지 않으므로 싣지 않는다. 숲 회복은 카운터(ReviveSerial)가 올랐을 때만 알림으로 본다
    /// <summary> 서버가 클라이언트 프롤로그 표현에 알리는 진행 상태 </summary>
    public readonly struct PrologueStateMessage : NetworkMessage, System.IEquatable<PrologueStateMessage>
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly byte   _stage;
        private readonly byte   _hint;
        private readonly byte   _potionCount;
        private readonly bool   _isTalismanOpen;
        private readonly bool   _isWolbaekOpen;
        private readonly ushort _reviveSerial;

        /****************************************
        *              Properties
        ****************************************/

        // EPrologueStage
        public byte   Stage          => _stage;

        // EForestHint
        public byte   Hint           => _hint;
        public byte   PotionCount    => _potionCount;
        public bool   IsTalismanOpen => _isTalismanOpen;
        public bool   IsWolbaekOpen  => _isWolbaekOpen;

        // 숲에서 체력 0으로 회복 처리될 때마다 1씩 오른다
        public ushort ReviveSerial   => _reviveSerial;

        /****************************************
        *                Methods
        ****************************************/

        public PrologueStateMessage(byte stage, byte hint, byte potionCount, bool isTalismanOpen, bool isWolbaekOpen,
            ushort reviveSerial)
        {
            _stage          = stage;
            _hint           = hint;
            _potionCount    = potionCount;
            _isTalismanOpen = isTalismanOpen;
            _isWolbaekOpen  = isWolbaekOpen;
            _reviveSerial   = reviveSerial;
        }

        // 값이 바뀔 때만 보내려고 서버가 마지막 전송 값과 비교한다. 박싱 없이 필드로 비교한다
        public bool Equals(PrologueStateMessage other)
        {
            return (_stage == other._stage) && (_hint == other._hint) && (_potionCount == other._potionCount)
                && (_isTalismanOpen == other._isTalismanOpen) && (_isWolbaekOpen == other._isWolbaekOpen)
                && (_reviveSerial == other._reviveSerial);
        }

        public override bool Equals(object obj)
        {
            return (obj is PrologueStateMessage other) && (Equals(other));
        }

        public override int GetHashCode()
        {
            return (_stage << 24) ^ (_hint << 16) ^ (_potionCount << 8) ^ _reviveSerial;
        }
    }
}
