namespace Shinshi.Dev
{
    /// <summary> 렉프레임 동안 굳을 수 있는 대상. 공격자와 피격자가 같은 방식으로 멈춘다 </summary>
    public interface IHitLagTarget
    {
        // 전역 timeScale을 건드리지 않고 이 대상만 굳는다. 멀티에서 남의 화면과 다른 대상은 그대로 흐른다

        /// <summary> frames 프레임 동안 굳힌다. 이미 굳어 있으면 더 긴 쪽을 쓴다 </summary>
        void Freeze(int frames);
    }
}
