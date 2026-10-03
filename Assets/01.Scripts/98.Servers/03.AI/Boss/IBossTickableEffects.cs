namespace Shinshi.AI
{
    // IBossEffectLifetime에 멤버를 더하면 기존 구현이 깨지므로 따로 둔 선택 계약이다
    // BossCombat이 수명 관리자가 이 인터페이스도 구현하면 공격 진행 뒤에 매 틱 한 번 부른다
    /// <summary> 매 틱 진행이 필요한 지속물 관리자 </summary>
    public interface IBossTickableEffects
    {
        /// <summary> 서버 시각으로 지속물을 진행한다. 이번 틱에 발사된 투사체도 발사 시각부터 지금까지 이어서 진행한다 </summary>
        void Tick(double serverTime);
    }
}
