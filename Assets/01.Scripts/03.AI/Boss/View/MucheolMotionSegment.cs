namespace Shinshi.AI
{
    /// <summary> 클립 하나를 시간 키로 재생하는 구간, 키 사이는 선형으로 잇는다 </summary>
    public readonly struct MucheolMotionSegment
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string             _clipName;
        private readonly MucheolMotionKey[] _keys;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 컨트롤러 상태 이름이자 클립 이름 </summary>
        public string ClipName => _clipName;

        public MucheolMotionKey[] Keys => _keys;

        /****************************************
        *                Methods
        ****************************************/

        public MucheolMotionSegment(string clipName, params MucheolMotionKey[] keys)
        {
            _clipName = clipName;
            _keys     = keys;
        }
    }
}
