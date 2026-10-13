namespace Shinshi.AI
{
    // 반격 중 체력이 다음 경계에 멈추면 앞선 기믹은 그 경계를 맡을 기믹이 이어받기 전까지 보스를 무적으로 두고 기다린다.
    // 이어받는 기믹은 자기 Tick에서 TryTakeHandoff를 불러 참을 받은 틱에 시작한다. 이어받을 기믹이 없으면 기다림은 끝나지 않는다
    /// <summary> 앞선 기믹에서 다음 체력 경계 기믹으로 넘기는 창구 </summary>
    public interface IBossBoundaryHandoff
    {
        /// <summary> 다음 경계 기믹이 이어받기를 기다리는 중이다 </summary>
        bool HasPendingHandoff { get; }

        /// <summary> 기다리는 인계가 있으면 받고 참을 돌려준다, 한 번 받으면 다시 참이 되지 않는다 </summary>
        bool TryTakeHandoff();
    }
}
