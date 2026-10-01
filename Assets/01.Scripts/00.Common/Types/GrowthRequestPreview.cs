// System
using System;
using System.Collections.Generic;

namespace Shinshi.Common
{
    /// <summary> 수련, 제작, 강화, 등록, 계승 요청 전에 규칙이 계산한 예상 비용과 결과, 확정이 아니다 </summary>
    public readonly struct GrowthRequestPreview
    {
        /****************************************
        *              Constants
        ****************************************/

        public const float MIN_SUCCESS_RATE = 0f;
        public const float MAX_SUCCESS_RATE = 1f;

        /****************************************
        *                Fields
        ****************************************/

        private readonly string                             _targetId;
        private readonly ERejectReason                      _blockReason;
        private readonly long                               _basedOnRevision;
        private readonly string                             _costVersion;
        private readonly IReadOnlyList<GrowthPreviewCost>   _costs;
        private readonly IReadOnlyList<GrowthPreviewChange> _changes;
        private readonly float                              _successRate;
        private readonly int                                _failureCount;

        /****************************************
        *              Properties
        ****************************************/

        // 미리보기를 만든 대상(수련 항목, 장비 인스턴스, 부적 효과 등)의 ID, 선택이 바뀌면 미리보기를 다시 만든다
        public string TargetId => _targetId;

        // 로컬 사전 검사에서 막힌 사유. None이어도 서버가 같은 조건을 다시 검증하므로 승인을 뜻하지 않는다
        public ERejectReason BlockReason => _blockReason;

        // default 값은 사유가 None이므로 대상 ID가 없으면 요청할 수 없는 빈 미리보기로 본다
        public bool CanRequest => (_blockReason == ERejectReason.None) && (!string.IsNullOrEmpty(_targetId));

        // 요청에 기대 revision과 예상 비용 버전으로 그대로 보낸다. 서버 값과 다르면 StaleRevision으로 거절되고 효과가 없다
        public long   BasedOnRevision => _basedOnRevision;
        public string CostVersion     => _costVersion;

        public IReadOnlyList<GrowthPreviewCost>   Costs   => _costs ?? Array.Empty<GrowthPreviewCost>();
        public IReadOnlyList<GrowthPreviewChange> Changes => _changes ?? Array.Empty<GrowthPreviewChange>();

        // 실패 보정이 반영된 이번 시도의 성공률, 확정 성공은 MAX_SUCCESS_RATE. 실제 성공 여부는 서버만 정한다
        public float SuccessRate => _successRate;

        // 성공률 보정의 근거가 되는 같은 키의 누적 실패 횟수
        public int FailureCount => _failureCount;

        /****************************************
        *                Methods
        ****************************************/

        public GrowthRequestPreview(string targetId, ERejectReason blockReason, long basedOnRevision, string costVersion,
            IReadOnlyList<GrowthPreviewCost> costs, IReadOnlyList<GrowthPreviewChange> changes, float successRate,
            int failureCount)
        {
            if ((successRate < MIN_SUCCESS_RATE) || (successRate > MAX_SUCCESS_RATE))
            {
                throw new ArgumentOutOfRangeException(nameof(successRate), "성공률은 0 이상 1 이하여야 한다");
            }

            if (failureCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(failureCount), "누적 실패 횟수는 음수일 수 없다");
            }

            _targetId        = targetId;
            _blockReason     = blockReason;
            _basedOnRevision = basedOnRevision;
            _costVersion     = costVersion;
            _costs           = costs;
            _changes         = changes;
            _successRate     = successRate;
            _failureCount    = failureCount;
        }

        // 예상 비용 버전까지 같아야 같은 조건의 미리보기다. 하나라도 다르면 확인창을 다시 띄운다
        public bool IsBasedOn(long characterRevision, string costVersion)
        {
            return (_basedOnRevision == characterRevision) && (string.Equals(_costVersion, costVersion, StringComparison.Ordinal));
        }
    }
}
