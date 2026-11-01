// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Data
{
    /// <summary> 시작 인원 배율, 부활, 재접속, 경매처럼 출정 한 판의 규칙 수치 </summary>
    [CreateAssetMenu(fileName = "SessionConfigSO", menuName = "Shinshi/SessionConfigSO")]
    public class SessionConfigSO : GameDataSO
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float MAX_RATE = 1f;

        /****************************************
        *                Fields
        ****************************************/

        // 배열은 모두 시작 인원 1명부터 Constants.Session.MAX_PARTY_SIZE명 순서다
        [Header("시작 인원별 배율")]
        [SerializeField] private float[] _enemyHealthScale = { 1f, 1.55f, 2.1f, 2.65f };
        [SerializeField] private float[] _bossHealthScale  = { 1f, 1.6f, 2.2f, 2.8f };
        [SerializeField] private float[] _damageScale      = { 1f, 1.05f, 1.1f, 1.15f };

        [Header("부활")]
        [SerializeField] private float _normalReviveDelay      = 5f;    // 초, 일반전 사망 뒤 대기
        [SerializeField] private float _normalReviveHealthRate = 0.5f;  // 최대 체력 비율
        [SerializeField] private float _bossReviveDelay        = 10f;   // 초, 협동 보스 사망 뒤 대기
        [SerializeField] private float _bossReviveHealthRate   = 0.3f;
        [SerializeField] private int   _bossSharedRevives      = 3;     // 파티 공용 횟수
        [SerializeField] private float _safePointRetryInterval = 0.25f; // 초, 안전 지점이 없을 때 재평가 간격
        [SerializeField] private float _safePointEnemyClearance = 6f;   // m, 복귀 지점과 살아 있는 적 사이 최소 수평 거리

        [Header("재접속과 준비")]
        [SerializeField] private float _reconnectGraceSeconds  = 120f;
        [SerializeField] private float _readyCountdownSeconds  = 3f;

        [Header("물약")]
        [SerializeField] private int   _bossPotionUseLimit     = 9;     // 보스전 소형, 대형 합산

        [Header("파티 경매")]
        [SerializeField] private int   _auctionMinBid          = 100;
        [SerializeField] private int   _auctionMinIncrement    = 10;
        [SerializeField] private float _auctionBaseSeconds     = 30f;
        [SerializeField] private float _auctionExtendThreshold = 10f;   // 남은 시간이 이보다 짧을 때 유효 입찰이 들어오면 연장
        [SerializeField] private float _auctionExtendTo        = 10f;   // 연장 뒤 남은 시간
        [SerializeField] private float _auctionMaxSeconds      = 120f;  // 품목 시작부터 최대
        [SerializeField] private float _auctionFeeRate         = 0f;

        // 기획 결정 전에는 Undecided로 두고, 이 값으로 배포하지 않는다
        [Header("서비스 장애 취소")]
        [SerializeField] private EExtraEssencePolicy _serviceCancelExtraEssence = EExtraEssencePolicy.Undecided;

        public float NormalReviveDelay       => _normalReviveDelay;
        public float NormalReviveHealthRate  => _normalReviveHealthRate;
        public float BossReviveDelay         => _bossReviveDelay;
        public float BossReviveHealthRate    => _bossReviveHealthRate;
        public int   BossSharedRevives       => _bossSharedRevives;
        public float SafePointRetryInterval  => _safePointRetryInterval;
        public float SafePointEnemyClearance => _safePointEnemyClearance;
        public float ReconnectGraceSeconds   => _reconnectGraceSeconds;
        public float ReadyCountdownSeconds   => _readyCountdownSeconds;
        public int   BossPotionUseLimit      => _bossPotionUseLimit;
        public int   AuctionMinBid           => _auctionMinBid;
        public int   AuctionMinIncrement     => _auctionMinIncrement;
        public float AuctionBaseSeconds      => _auctionBaseSeconds;
        public float AuctionExtendThreshold  => _auctionExtendThreshold;
        public float AuctionExtendTo         => _auctionExtendTo;
        public float AuctionMaxSeconds       => _auctionMaxSeconds;
        public float AuctionFeeRate          => _auctionFeeRate;
        public EExtraEssencePolicy ServiceCancelExtraEssence => _serviceCancelExtraEssence;

        /****************************************
        *                Methods
        ****************************************/

        public float GetEnemyHealthScale(int startingCount) { return Pick(_enemyHealthScale, startingCount); }
        public float GetBossHealthScale(int startingCount)  { return Pick(_bossHealthScale, startingCount); }
        public float GetDamageScale(int startingCount)      { return Pick(_damageScale, startingCount); }

        private static float Pick(float[] table, int startingCount)
        {
            if ((startingCount < Constants.Session.MIN_PARTY_SIZE) || (startingCount > Constants.Session.MAX_PARTY_SIZE))
            {
                throw new ArgumentOutOfRangeException(nameof(startingCount), "시작 인원이 허용 범위를 벗어났다");
            }

            return table[startingCount - Constants.Session.MIN_PARTY_SIZE];
        }

        protected override void ValidateValues(List<string> errors)
        {
            const string OWNER = nameof(SessionConfigSO);

            ValidateScaleTable(errors, nameof(_enemyHealthScale), _enemyHealthScale);
            ValidateScaleTable(errors, nameof(_bossHealthScale), _bossHealthScale);
            ValidateScaleTable(errors, nameof(_damageScale), _damageScale);

            DataValidation.RequireNonNegative(errors, OWNER, nameof(_normalReviveDelay), _normalReviveDelay);
            DataValidation.RequirePositiveAtMost(errors, OWNER, nameof(_normalReviveHealthRate), _normalReviveHealthRate, MAX_RATE);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_bossReviveDelay), _bossReviveDelay);
            DataValidation.RequirePositiveAtMost(errors, OWNER, nameof(_bossReviveHealthRate), _bossReviveHealthRate, MAX_RATE);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_bossSharedRevives), _bossSharedRevives);
            DataValidation.RequirePositive(errors, OWNER, nameof(_safePointRetryInterval), _safePointRetryInterval);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_safePointEnemyClearance), _safePointEnemyClearance);
            DataValidation.RequirePositive(errors, OWNER, nameof(_reconnectGraceSeconds), _reconnectGraceSeconds);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_readyCountdownSeconds), _readyCountdownSeconds);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_bossPotionUseLimit), _bossPotionUseLimit);
            if (_bossPotionUseLimit != Shinshi.Contracts.GameSessionCodes.Potion.BOSS_USE_LIMIT)
            {
                // 던전 서버와 API는 공용 계약 상수를 쓴다. SO 값은 표시와 검토용이라 다르면 어긋난 것이다
                errors.Add($"{OWNER}.{nameof(_bossPotionUseLimit)} = {_bossPotionUseLimit}: "
                    + $"GameSessionCodes.Potion.BOSS_USE_LIMIT({Shinshi.Contracts.GameSessionCodes.Potion.BOSS_USE_LIMIT})와 같아야 한다");
            }
            DataValidation.RequirePositive(errors, OWNER, nameof(_auctionMinBid), _auctionMinBid);
            DataValidation.RequirePositive(errors, OWNER, nameof(_auctionMinIncrement), _auctionMinIncrement);
            DataValidation.RequirePositive(errors, OWNER, nameof(_auctionBaseSeconds), _auctionBaseSeconds);
            DataValidation.RequireNonNegative(errors, OWNER, nameof(_auctionExtendThreshold), _auctionExtendThreshold);
            DataValidation.RequirePositive(errors, OWNER, nameof(_auctionExtendTo), _auctionExtendTo);
            DataValidation.RequireRange(errors, OWNER, nameof(_auctionMaxSeconds), _auctionMaxSeconds, _auctionBaseSeconds, float.MaxValue);
            DataValidation.RequireRange(errors, OWNER, nameof(_auctionFeeRate), _auctionFeeRate, 0f, MAX_RATE);
        }

        private static void ValidateScaleTable(List<string> errors, string field, float[] table)
        {
            const string OWNER = nameof(SessionConfigSO);

            int expected = (Constants.Session.MAX_PARTY_SIZE - Constants.Session.MIN_PARTY_SIZE) + 1;
            if ((table == null) || (table.Length != expected))
            {
                int count = (table == null) ? 0 : table.Length;
                errors.Add($"{OWNER}.{field}: 인원별 배율 배열 길이 {count} != {expected}");
                return;
            }

            for (int i = 0; i < table.Length; ++i)
            {
                DataValidation.RequirePositive(errors, OWNER, $"{field}[{i}]", table[i]);
            }
        }
    }
}
