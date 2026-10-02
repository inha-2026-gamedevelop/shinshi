// System
using System;
using System.Threading;
using System.Threading.Tasks;

// Project
using Shinshi.Contracts;
using Shinshi.Contracts.Admin;

namespace Shinshi.Backend.Admin
{
    /// <summary> 운영자가 끈 기능과 점검을 API 진입점에서 확인한다(싱글턴). 기능 키는 ServiceFeatureKeys 상수만 받는다.
    /// 막을 때는 ContractCodes.RejectReason.FEATURE_DISABLED(점검은 MAINTENANCE)로 거절하고, 이미 시작한 진행(정산, 경매 마감 등)은 막지 않는다 </summary>
    public sealed class ServiceFeatureGate
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly ServiceStateStore _store;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 캐시 스냅샷 기준 점검 여부. DB를 한 번도 읽지 못했으면 true다 </summary>
        public bool IsMaintenance => _store.Current.Maintenance;

        /****************************************
        *                Methods
        ****************************************/

        public ServiceFeatureGate(ServiceStateStore store)
        {
            ArgumentNullException.ThrowIfNull(store);
            _store = store;
        }

        /// <summary> 캐시 스냅샷으로 바로 판단한다. 동기 경로에서 쓴다 </summary>
        public bool IsDisabled(string featureKey)
        {
            RequireKnown(featureKey);
            return _store.Current.IsFeatureDisabled(featureKey);
        }

        public async ValueTask<bool> IsDisabledAsync(string featureKey, CancellationToken cancellationToken = default)
        {
            RequireKnown(featureKey);
            ServiceStateSnapshot snapshot = await _store.GetAsync(cancellationToken);
            return snapshot.IsFeatureDisabled(featureKey);
        }

        /// <summary> 꺼진 기능이면 FEATURE_DISABLED, 아니면 NONE. 점검은 보지 않는다 </summary>
        public async ValueTask<int> CheckFeatureAsync(string featureKey, CancellationToken cancellationToken = default)
        {
            return await IsDisabledAsync(featureKey, cancellationToken)
                ? ContractCodes.RejectReason.FEATURE_DISABLED
                : ContractCodes.RejectReason.NONE;
        }

        /// <summary> 새 출정 예약의 진입 검사. 점검 중이면 MAINTENANCE, dungeon_entry가 꺼졌거나 보스전인데 boss_entry가 꺼졌으면
        /// FEATURE_DISABLED, 아니면 NONE이다. 이미 예약하거나 시작한 세션의 입장, 정산에는 쓰지 않는다 </summary>
        public async ValueTask<int> CheckNewSortieAsync(int mode, CancellationToken cancellationToken = default)
        {
            ServiceStateSnapshot snapshot = await _store.GetAsync(cancellationToken);
            if (snapshot.Maintenance)
            {
                return ContractCodes.RejectReason.MAINTENANCE;
            }
            if (snapshot.IsFeatureDisabled(ServiceFeatureKeys.DUNGEON_ENTRY)
                || ((mode == GameSessionCodes.Mode.BOSS) && snapshot.IsFeatureDisabled(ServiceFeatureKeys.BOSS_ENTRY)))
            {
                return ContractCodes.RejectReason.FEATURE_DISABLED;
            }

            return ContractCodes.RejectReason.NONE;
        }

        // 철자가 틀린 키는 늘 열린 기능으로 보이므로 호출 코드의 실수로 보고 예외를 낸다
        private static void RequireKnown(string featureKey)
        {
            if (!ServiceFeatureKeys.IsKnown(featureKey))
            {
                throw new ArgumentException("Unknown service feature key.", nameof(featureKey));
            }
        }
    }
}
