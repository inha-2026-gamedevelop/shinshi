namespace Shinshi.Combat
{
    /// <summary> 플레이어 입력 Command의 서버 처리기, 구현은 서버 어셈블리에 있고 헤더와 순번 검증부터 한다 </summary>
    public interface IPlayerInputServerHandler
    {
        void HandleMove(in PlayerMoveInput input);

        void HandleAttack(in PlayerAttackInput input);

        // 회피는 다른 입력보다 먼저 처리하고, 남은 쿨다운이 입력 보관 시간 안이면 받아 둔다
        void HandleDodge(in PlayerDodgeInput input);

        // 사망 뒤 부활 요청, 승인은 서버 틱의 부활 단계가 한다
        void HandleRevive(in PlayerInputHeader header);
    }
}
