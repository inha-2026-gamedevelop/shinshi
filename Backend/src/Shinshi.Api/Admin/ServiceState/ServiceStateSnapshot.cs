// System
using System;
using System.Collections.Generic;
using System.Linq;

// Project
using Shinshi.Contracts;
using Shinshi.Data.Admin;

namespace Shinshi.Backend.Admin
{
    /// <summary> 한 시점의 점검, 기능 차단, 허용 빌드, 공지. 만든 뒤 바꾸지 않으므로 여러 요청이 같이 읽어도 된다 </summary>
    public sealed class ServiceStateSnapshot
    {
        /****************************************
        *                Fields
        ****************************************/

        // 공개 응답에 싣는 노출 공지 수
        public const int MAX_PUBLIC_NOTICES = 20;

        private readonly string[] _disabledFeatures;
        private readonly string[] _allowedBuildIds;
        private readonly ServiceNoticeRecord[] _notices;

        /****************************************
        *              Properties
        ****************************************/

        public bool Maintenance { get; }
        public long Revision { get; }
        public EServiceStateSource Source { get; }

        // 노출 중인 런처 공지가 없을 때 notice에 넣는 문구. DB 값이면 빈 문자열이다
        public string FallbackNotice { get; }

        public IReadOnlyList<string> DisabledFeatures => _disabledFeatures;
        public IReadOnlyList<string> AllowedBuildIds => _allowedBuildIds;

        // 아직 끝나지 않은 공지(예정 포함). 중요도, 시작 시각 내림차순
        public IReadOnlyList<ServiceNoticeRecord> Notices => _notices;

        /****************************************
        *                Methods
        ****************************************/

        public ServiceStateSnapshot(bool maintenance, IEnumerable<string>? disabledFeatures,
            IEnumerable<string>? allowedBuildIds, long revision, EServiceStateSource source, string? fallbackNotice,
            IEnumerable<ServiceNoticeRecord>? notices)
        {
            Maintenance       = maintenance;
            Revision          = revision;
            Source            = source;
            FallbackNotice    = fallbackNotice ?? string.Empty;
            _disabledFeatures = (disabledFeatures ?? Array.Empty<string>()).ToArray();
            _allowedBuildIds  = (allowedBuildIds ?? Array.Empty<string>()).ToArray();
            _notices          = (notices ?? Array.Empty<ServiceNoticeRecord>())
                .OrderByDescending(notice => notice.Severity)
                .ThenByDescending(notice => notice.StartsAt)
                .ToArray();
        }

        public bool IsFeatureDisabled(string featureKey)
        {
            return Array.IndexOf(_disabledFeatures, featureKey) >= 0;
        }

        public bool IsBuildAllowed(string? buildId)
        {
            return (!string.IsNullOrWhiteSpace(buildId)) && (Array.IndexOf(_allowedBuildIds, buildId) >= 0);
        }

        /// <summary> now에 노출 중인 공지만 고른다. 캐시 주기 사이에도 시작, 끝 시각을 정확히 지킨다 </summary>
        public IReadOnlyList<ServiceNoticeRecord> VisibleNotices(DateTimeOffset now)
        {
            return _notices.Where(notice => notice.IsVisibleAt(now)).Take(MAX_PUBLIC_NOTICES).ToArray();
        }

        /// <summary> 공개 /v1/service-state 응답. notice는 노출 중인 런처 대상 첫 공지의 제목이다(런처가 이 문자열만 배너로 쓴다) </summary>
        public ServiceStateResponse ToResponse(DateTimeOffset now)
        {
            IReadOnlyList<ServiceNoticeRecord> visible = VisibleNotices(now);
            ServiceNoticeRecord? launcher = visible.FirstOrDefault(notice =>
                (notice.Target == ServiceNotice.TARGET_LAUNCHER) || (notice.Target == ServiceNotice.TARGET_BOTH));
            return new ServiceStateResponse(Maintenance, launcher?.Title ?? FallbackNotice, _disabledFeatures.ToArray(),
                _allowedBuildIds.ToArray(), now.ToUnixTimeMilliseconds(), visible.Select(notice => new ServiceNotice(
                    notice.NoticeId.ToString("D"), notice.Title, notice.Body, notice.Target, notice.Severity,
                    notice.StartsAt.ToUnixTimeMilliseconds(), notice.EndsAt.ToUnixTimeMilliseconds())).ToArray());
        }
    }
}
