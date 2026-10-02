namespace Shinshi.Data.Admin
{
    /// <summary> 서비스 상태 저장 결과. 저장했으면 State는 새 행, revision이 달랐으면 State는 그대로인 현재 행이다 </summary>
    public sealed class ServiceStateUpdateOutcome
    {
        /****************************************
        *              Properties
        ****************************************/

        public bool IsUpdated { get; }
        public ServiceStateRecord State { get; }

        /****************************************
        *                Methods
        ****************************************/

        private ServiceStateUpdateOutcome(bool isUpdated, ServiceStateRecord state)
        {
            IsUpdated = isUpdated;
            State     = state;
        }

        public static ServiceStateUpdateOutcome Updated(ServiceStateRecord state)
        {
            return new ServiceStateUpdateOutcome(true, state);
        }

        public static ServiceStateUpdateOutcome Stale(ServiceStateRecord current)
        {
            return new ServiceStateUpdateOutcome(false, current);
        }
    }
}
