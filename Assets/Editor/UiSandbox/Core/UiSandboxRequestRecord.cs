namespace Shinshi.Editor
{
    /// <summary> 가짜 요청 한 건의 진행 상태 </summary>
    public enum EUiSandboxRequestState
    {
        Pending,
        Succeeded,
        Failed,
        Canceled,
        Blocked
    }

    /// <summary> 요청 이력 한 줄. 컨텍스트만 값을 바꾸고 창은 읽기만 한다 </summary>
    public sealed class UiSandboxRequestRecord
    {
        public int Id { get; }
        public string Operation { get; }
        public EUiSandboxResponseMode Mode { get; }
        public float DelaySeconds { get; }
        public double StartedAt { get; }
        public double CompletedAt { get; internal set; }
        public EUiSandboxRequestState State { get; internal set; }
        public string Message { get; internal set; }

        public UiSandboxRequestRecord(int id, string operation, EUiSandboxResponseMode mode, float delaySeconds,
            double startedAt, EUiSandboxRequestState state, string message)
        {
            Id           = id;
            Operation    = operation ?? string.Empty;
            Mode         = mode;
            DelaySeconds = delaySeconds;
            StartedAt    = startedAt;
            CompletedAt  = startedAt;
            State        = state;
            Message      = message ?? string.Empty;
        }

        public override string ToString()
        {
            return $"#{Id} {Operation} [{State}] {Message}";
        }
    }
}
