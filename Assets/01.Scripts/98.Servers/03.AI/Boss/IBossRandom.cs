namespace Shinshi.AI
{
    /// <summary> 보스 선택용 난수, 테스트가 결과를 고정할 수 있게 분리한다 </summary>
    public interface IBossRandom
    {
        /// <summary> 0 이상 1 미만 </summary>
        float NextFloat01();

        /// <summary> 0 이상 maxExclusive 미만 </summary>
        int NextInt(int maxExclusive);
    }
}
