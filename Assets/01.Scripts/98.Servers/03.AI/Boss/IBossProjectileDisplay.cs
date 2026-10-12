namespace Shinshi.AI
{
    // 발사와 소멸을 화면에 알릴 수신처가 없는 서버에서 대상 상한이 있는 난사가 보이지 않는 피해를 내지 못하게, 팩토리가 패턴을 만들기 전에 확인하는 자리다
    /// <summary> 투사체의 발사와 소멸을 알릴 표시 수신처가 연결돼 있는가 </summary>
    public interface IBossProjectileDisplay
    {
        bool IsDisplayConnected { get; }
    }
}
