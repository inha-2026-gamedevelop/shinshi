namespace Shinshi.Combat
{
    /// <summary> 신뢰 채널의 증가 순번과 현재 세션 수명으로 중복·오래된 숫자를 거절한다 </summary>
    public sealed class DamageFeedbackReceiver
    {
        /****************************************
        *                Fields
        ****************************************/

        private uint _lastSequence;
        private int _sessionEpoch = -1;

        /****************************************
        *                Methods
        ****************************************/

        // 소유권이나 씬이 바뀔 때 순번을 되감지 않는다. 이미 받은 숫자는 새 풀에서 재생하지 않는다
        public bool TryAccept(in DamageFeedback feedback, int sessionEpoch, double now)
        {
            if ((!DamageFeedbackRules.IsValid(in feedback)) || (feedback.SessionEpoch != sessionEpoch)
                || (!DamageFeedbackRules.IsFresh(feedback.ServerTime, now)))
            {
                return false;
            }

            if (_sessionEpoch != sessionEpoch)
            {
                _sessionEpoch = sessionEpoch;
                _lastSequence = 0;
            }
            if (feedback.Sequence <= _lastSequence)
            {
                return false;
            }

            _lastSequence = feedback.Sequence;
            return true;
        }
    }
}
