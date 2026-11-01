// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    // 서버가 명단 순서(최대 4명)로 표를 들고 있는다. 투표 자격은 매번 서버가 비트 마스크로 넘긴다(접속 중이고 이탈하지 않은 참가자).
    // 자격자 전원이 찬성하면 가결, 한 명이라도 반대하면 부결, 시간이 지나면 부결이다
    /// <summary> 보스전 토벌 중단 투표의 서버 상태와 판정 규칙 </summary>
    public sealed class SortieStopVote
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly ESortieStopVoteChoice[] _choices;
        private readonly double                  _durationSeconds;
        private readonly double                  _retrySeconds;

        private ESortieStopVoteState _state         = ESortieStopVoteState.None;
        private double               _endsAt        = 0.0;
        private double               _retryAt       = 0.0;
        private int                  _agreeCount    = 0;
        private int                  _disagreeCount = 0;
        private int                  _eligibleCount = 0;
        private int                  _version       = 0;

        /****************************************
        *              Properties
        ****************************************/

        public ESortieStopVoteState State         => _state;
        public double               EndsAt        => _endsAt;
        public int                  AgreeCount    => _agreeCount;
        public int                  DisagreeCount => _disagreeCount;
        public int                  EligibleCount => _eligibleCount;
        public int                  RequiredCount => RequiredFor(_eligibleCount);

        // 보이는 값이 바뀔 때마다 오른다. 서버는 이 값이 바뀐 틱에만 상태를 보낸다
        public int                  Version       => _version;

        /****************************************
        *                Methods
        ****************************************/

        public SortieStopVote(int capacity, double durationSeconds, double retrySeconds)
        {
            _choices         = new ESortieStopVoteChoice[capacity];
            _durationSeconds = durationSeconds;
            _retrySeconds    = retrySeconds;
        }

        /// <summary> 가결에 필요한 찬성 수, 자격자 전원이다 </summary>
        public static int RequiredFor(int eligibleCount)
        {
            return eligibleCount;
        }

        public ESortieStopVoteChoice GetChoice(int index)
        {
            return ((index < 0) || (index >= _choices.Length)) ? ESortieStopVoteChoice.None : _choices[index];
        }

        /// <summary> 한 표를 넣는다. 열린 투표가 없을 때 찬성하면 새 투표를 연다. 받지 않으면 거절 사유를 돌려준다 </summary>
        public ERejectReason Cast(int index, bool agree, double now, int eligibleMask)
        {
            if ((index < 0) || (index >= _choices.Length) || (!IsEligible(eligibleMask, index)))
            {
                return ERejectReason.NotEligible;
            }

            if (_state == ESortieStopVoteState.Passed)
            {
                return ERejectReason.SessionTerminal;
            }

            if (_state != ESortieStopVoteState.Open)
            {
                // 반대로는 투표를 열지 않는다. 부결 직후에는 같은 제안이 반복되지 않게 잠시 막는다
                if (!agree)
                {
                    return ERejectReason.WrongPhase;
                }

                if (now < _retryAt)
                {
                    return ERejectReason.Cooldown;
                }

                Open(now);
            }

            if (_choices[index] != ESortieStopVoteChoice.None)
            {
                return ERejectReason.AlreadySettled;
            }

            _choices[index] = agree ? ESortieStopVoteChoice.Agree : ESortieStopVoteChoice.Disagree;
            ++_version;
            return ERejectReason.None;
        }

        /// <summary> 매 틱 표를 넣은 뒤 부른다. 자격을 잃은 참가자의 표는 세지 않고, 가결, 부결, 시간 초과를 정한다 </summary>
        public ESortieStopVoteState Evaluate(double now, int eligibleMask)
        {
            if (_state != ESortieStopVoteState.Open)
            {
                return _state;
            }

            int agree    = 0;
            int disagree = 0;
            int eligible = 0;
            for (int i = 0; i < _choices.Length; ++i)
            {
                if (!IsEligible(eligibleMask, i))
                {
                    continue;
                }

                ++eligible;
                if (_choices[i] == ESortieStopVoteChoice.Agree)
                {
                    ++agree;
                }
                else if (_choices[i] == ESortieStopVoteChoice.Disagree)
                {
                    ++disagree;
                }
            }

            if ((agree != _agreeCount) || (disagree != _disagreeCount) || (eligible != _eligibleCount))
            {
                _agreeCount    = agree;
                _disagreeCount = disagree;
                _eligibleCount = eligible;
                ++_version;
            }

            int required = RequiredFor(eligible);
            if ((eligible > 0) && (agree >= required))
            {
                _state = ESortieStopVoteState.Passed;
                ++_version;
            }
            else if ((eligible == 0) || ((eligible - disagree) < required) || (now >= _endsAt))
            {
                _state   = ESortieStopVoteState.Failed;
                _retryAt = now + _retrySeconds;
                ++_version;
            }

            return _state;
        }

        private void Open(double now)
        {
            for (int i = 0; i < _choices.Length; ++i)
            {
                _choices[i] = ESortieStopVoteChoice.None;
            }

            _state         = ESortieStopVoteState.Open;
            _endsAt        = now + _durationSeconds;
            _agreeCount    = 0;
            _disagreeCount = 0;
            ++_version;
        }

        private static bool IsEligible(int eligibleMask, int index)
        {
            return (eligibleMask & (1 << index)) != 0;
        }
    }
}
