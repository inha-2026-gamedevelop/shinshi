namespace Shinshi.Combat
{
    /// <summary> 플레이어에게 치명 피해가 확정됐을 때 죽음을 대신 처리할 수 있는 규칙, 프롤로그 숲의 제자리 회복이 쓴다 </summary>
    public interface IPlayerLethalInterceptor
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 치명 피해를 받은 직후 부른다, 참이면 죽음을 확정하지 않는다. 호출 안에서 체력을 회복시키는 일까지 맡는다 </summary>
        bool TryIntercept(PlayerServer player, double serverTime);
    }
}
