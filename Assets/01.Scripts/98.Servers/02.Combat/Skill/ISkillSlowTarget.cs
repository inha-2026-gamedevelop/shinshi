namespace Shinshi.Combat
{
    // 보스는 이 창구를 구현하지 않는다. 보스 돌진과 위치 잡기는 서버가 예고와 맞춘 시각표라 둔화로 늦추면
    // 예고 범위와 실제 이동이 어긋나고, 궁수 여럿이 얼음을 이어 붙이면 보스를 상시 묶을 수 있기 때문이다
    /// <summary> 이동 속도 둔화를 받는 일반 적, 두뇌가 이동할 때 배율을 읽는다 </summary>
    public interface ISkillSlowTarget
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> rate는 0에서 1 사이 감소율이다. 걸려 있는 둔화와 겹치면 더 늦은 끝 시각과 더 큰 감소율을 쓴다 </summary>
        void ApplySlow(float rate, float duration, double serverTime);
    }
}
