// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Combat
{
    /// <summary> 서버가 소유자에게만 보내는 물약 상태, 요청 결과와 확정, 되돌림 때마다 온다 </summary>
    public readonly struct PotionStateMessage : NetworkMessage
    {
        // 남은 수는 지금 쓸 수 있는 수다(확정 대기와 되돌림 대기 중인 몫은 뺐다). 쿨다운 끝 시각은 NetworkTime 기준 서버 초다

        /****************************************
        *                Fields
        ****************************************/

        private readonly uint          _sequence;
        private readonly ERejectReason _reason;
        private readonly ushort        _smallRemaining;
        private readonly ushort        _largeRemaining;
        private readonly byte          _bossUsed;
        private readonly byte          _bossLimit;
        private readonly bool          _isPending;
        private readonly double        _cooldownEndsAt;

        /****************************************
        *              Properties
        ****************************************/

        // 답하는 요청의 입력 순번, 확정이나 되돌림처럼 요청 없이 바뀐 상태면 0이다
        public uint          Sequence       => _sequence;

        // None이면 받았거나 상태 알림이다. 그 밖은 거절 사유이고 상태는 바뀌지 않았다
        public ERejectReason Reason         => _reason;
        public ushort        SmallRemaining => _smallRemaining;
        public ushort        LargeRemaining => _largeRemaining;

        // 보스전 출정에서 쓴 횟수와 상한, 일반전은 상한이 0이다
        public byte          BossUsed       => _bossUsed;
        public byte          BossLimit      => _bossLimit;

        // 사용을 받아 저장 확정을 기다리는 중이다. 확정되면 회복하고 쿨다운이 시작된다
        public bool          IsPending      => _isPending;
        public double        CooldownEndsAt => _cooldownEndsAt;

        /****************************************
        *                Methods
        ****************************************/

        public PotionStateMessage(uint sequence, ERejectReason reason, ushort smallRemaining, ushort largeRemaining, byte bossUsed,
            byte bossLimit, bool isPending, double cooldownEndsAt)
        {
            _sequence       = sequence;
            _reason         = reason;
            _smallRemaining = smallRemaining;
            _largeRemaining = largeRemaining;
            _bossUsed       = bossUsed;
            _bossLimit      = bossLimit;
            _isPending      = isPending;
            _cooldownEndsAt = cooldownEndsAt;
        }
    }
}
