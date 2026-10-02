namespace Shinshi.Art
{
    /// <summary> 부채꼴 폭발이 영역을 진행하는 방식, 값은 셰이더의 _Sweep.x와 같다 </summary>
    public enum ESectorExplosionMode
    {
        Simultaneous = 0,
        RadialSweep  = 1,
        AngularSweep = 2
    }
}
