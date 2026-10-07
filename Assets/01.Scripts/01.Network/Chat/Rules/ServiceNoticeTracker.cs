// System
using System.Collections.Generic;

// 프로젝트 내부
using Shinshi.Contracts;

namespace Shinshi.Network
{
    // 같은 응답을 60초마다 다시 받으므로 어떤 공지를 언제 보였는지 기억해 중복을 막는다. 엔진과 네트워크를 쓰지 않아 시험할 수 있다
    /// <summary> 조회한 공지 목록에서 이번에 채팅에 넣을 줄만 고른다, 메인 스레드에서만 부른다 </summary>
    public sealed class ServiceNoticeTracker
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly Dictionary<string, double> _shownAt = new Dictionary<string, double>();
        private readonly HashSet<string>            _liveIds = new HashSet<string>();
        private readonly List<string>               _stale   = new List<string>();

        /****************************************
        *              Properties
        ****************************************/

        public int TrackedCount => _shownAt.Count;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 채팅 로그가 비워졌을 때 부른다. 이미 보인 공지를 다시 보이게 한다 </summary>
        public void Reset()
        {
            _shownAt.Clear();
            _liveIds.Clear();
            _stale.Clear();
        }

        // 서버가 중요도와 시작 시각 내림차순으로 보내므로 뒤에서부터 넣어 가장 중요한 공지가 로그 맨 아래에 온다.
        // 응답에서 사라진(끝난) 공지의 기록은 지워 기록이 계속 쌓이지 않게 한다
        /// <summary> 보일 공지의 한 줄을 lines 끝에 붙이고 보인 시각을 기록한다 </summary>
        public void Collect(IReadOnlyList<ServiceNotice> notices, long serverNowUnixMs, double nowSeconds, List<string> lines)
        {
            if (lines == null)
            {
                return;
            }

            _liveIds.Clear();
            if (notices != null)
            {
                for (int i = notices.Count - 1; i >= 0; --i)
                {
                    ServiceNotice notice = notices[i];
                    if ((notice == null) || (string.IsNullOrEmpty(notice.NoticeId))
                        || (!ServiceNoticeRules.IsForGame(notice)) || (!ServiceNoticeRules.IsActive(notice, serverNowUnixMs)))
                    {
                        continue;
                    }

                    _liveIds.Add(notice.NoticeId);
                    bool alreadyShown = _shownAt.TryGetValue(notice.NoticeId, out double lastShown);
                    if (!ServiceNoticeRules.ShouldShow(alreadyShown, lastShown, nowSeconds, notice.Severity))
                    {
                        continue;
                    }

                    string line = ServiceNoticeRules.FormatLine(notice);
                    if (line == null)
                    {
                        continue;
                    }

                    _shownAt[notice.NoticeId] = nowSeconds;
                    lines.Add(line);
                }
            }

            RemoveStale();
        }

        private void RemoveStale()
        {
            _stale.Clear();
            foreach (KeyValuePair<string, double> pair in _shownAt)
            {
                if (!_liveIds.Contains(pair.Key))
                {
                    _stale.Add(pair.Key);
                }
            }

            for (int i = 0; i < _stale.Count; ++i)
            {
                _shownAt.Remove(_stale[i]);
            }
        }
    }
}
