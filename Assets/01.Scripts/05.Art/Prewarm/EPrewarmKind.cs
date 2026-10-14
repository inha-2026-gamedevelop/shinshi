namespace Shinshi.Art
{
    /// <summary> 프리워밍 항목을 데우는 방법 </summary>
    public enum EPrewarmKind
    {
        // ParticleSystem, TrailRenderer만으로 표시되는 프리팹, 러너가 재생과 정리를 직접 한다
        Particle = 0,

        // 스크립트가 렌더러를 숨기거나 메시를 재생 중에 만드는 프리팹, 루트의 IPrewarmTarget이 실제 재생 경로를 탄다
        Scripted = 1,

        // 스킨드 메시와 Animator를 가진 모델, 활성화해 그리기만 한다
        Model = 2
    }
}
