// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버 피해 판정기가 정상 적중을 확정한 순간의 값을 채운다. 보스 위치와 시선은 그 순간의 것이고 발사자나 이전 틱의 값이 아니다
    /// <summary> 보스에 정상 적중한 타격 한 건, 봉인 기믹이 타수와 방향을 가르는 입력 </summary>
    public readonly struct MucheolSealHit
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly int     _actionInstanceId;
        private readonly bool    _isDamageOverTime;
        private readonly bool    _isSummonAttack;
        private readonly Vector3 _bossForward;
        private readonly Vector3 _incomingDirection;

        /****************************************
        *              Properties
        ****************************************/

        // 공격 동작 하나의 번호, 같은 동작의 중복 충돌 보고를 한 번만 세는 기준이다
        public int ActionInstanceId => _actionInstanceId;

        public bool IsDamageOverTime => _isDamageOverTime;

        public bool IsSummonAttack => _isSummonAttack;

        // 적중 확정 순간 보스의 시선
        public Vector3 BossForward => _bossForward;

        // 적중 확정 순간 보스에서 공격이 온 쪽을 가리키는 방향, 투사체는 날아온 쪽이고 발사자 위치가 아니다. 모르면 영벡터다
        public Vector3 IncomingDirection => _incomingDirection;

        /****************************************
        *                Methods
        ****************************************/

        public MucheolSealHit(int actionInstanceId, bool isDamageOverTime, bool isSummonAttack, Vector3 bossForward,
            Vector3 incomingDirection)
        {
            _actionInstanceId  = actionInstanceId;
            _isDamageOverTime  = isDamageOverTime;
            _isSummonAttack    = isSummonAttack;
            _bossForward       = bossForward;
            _incomingDirection = incomingDirection;
        }
    }
}
