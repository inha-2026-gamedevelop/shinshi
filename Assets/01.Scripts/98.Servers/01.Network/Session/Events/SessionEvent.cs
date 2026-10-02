namespace Shinshi.Network
{
    /// <summary> 던전 안에서 단조 증가하는 저장 순번과 최초 판정 payload, 재시도 때 바꾸지 않는다 </summary>
    public readonly struct SessionEvent
    {
        /****************************************
        *              Properties
        ****************************************/

        public long             Seq  { get; }
        public SessionEventData Data { get; }

        /****************************************
        *                Methods
        ****************************************/

        public SessionEvent(long seq, in SessionEventData data)
        {
            Seq  = seq;
            Data = data;
        }
    }
}
