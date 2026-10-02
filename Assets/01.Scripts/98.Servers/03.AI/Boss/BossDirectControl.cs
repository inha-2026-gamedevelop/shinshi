// System
using System;

namespace Shinshi.AI
{
    // 속박 등 제어 종류별로 보스가 어떻게 움직이는지는 협의 전이라, 여기에는 값만 싣고 해석하지 않는다
    // 종류를 구분해야 하면 필드를 더하거나 SourceId로 구분한다
    /// <summary> 보스에 도착한 직접 제어 한 건 </summary>
    public readonly struct BossDirectControl
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly string _sourceId;
        private readonly float  _requestedSeconds;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 제어를 건 기술이나 효과의 정의 ID, 알 수 없으면 빈 문자열 </summary>
        public string SourceId => _sourceId ?? string.Empty;

        /// <summary> 제어가 요청한 지속 시간, 0이면 지정하지 않았다 </summary>
        public float RequestedSeconds => _requestedSeconds;

        /****************************************
        *                Methods
        ****************************************/

        public BossDirectControl(string sourceId, float requestedSeconds)
        {
            _sourceId         = sourceId;
            _requestedSeconds = Math.Max(0f, requestedSeconds);
        }
    }
}
