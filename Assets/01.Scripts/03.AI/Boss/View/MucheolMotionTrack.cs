namespace Shinshi.AI
{
    // 서버 시각으로 바꾼 구간이다. 시각은 앞에서 뒤로 줄어들지 않게 맞춰 두어 샘플링이 거꾸로 가지 않는다
    /// <summary> 서버 시각과 클립 시간의 짝으로 바꾼 모션 구간 하나 </summary>
    public sealed class MucheolMotionTrack
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string   _clipName;
        private readonly double[] _times;
        private readonly float[]  _clips;

        /****************************************
        *              Properties
        ****************************************/

        public string ClipName => _clipName;

        public int KeyCount => _times.Length;

        public double StartTime => _times[0];

        public double EndTime => _times[_times.Length - 1];

        public float StartClip => _clips[0];

        public float EndClip => _clips[_clips.Length - 1];

        /****************************************
        *                Methods
        ****************************************/

        public MucheolMotionTrack(string clipName, double[] times, float[] clips)
        {
            _clipName = clipName;
            _times    = times;
            _clips    = clips;
        }

        public double GetTime(int index) { return _times[index]; }

        public float GetClip(int index) { return _clips[index]; }
    }
}
