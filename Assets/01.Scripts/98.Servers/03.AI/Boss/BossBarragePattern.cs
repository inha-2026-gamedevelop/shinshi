// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.AI
{
    // 발 수와 간격과 예고 시간과 사거리와 폭과 속도와 피해 배수와 대상당 피격 횟수는 모두 패턴 정의에서 읽는다. 이 클래스에는 밸런스 값이 없다
    //
    // 1페이즈 P04와 2페이즈 이지 P05(BossProjectilePattern)는 대상을 따라 돈 뒤 고정한 한 방향으로 쏘고 발마다 그때의 보스 위치를 원점으로 쓴다
    // 이 패턴은 대상을 보지 않고 전방위를 구역으로 나눠 구역마다 한 발씩 쏘며 원점이 시전 시작 순간에 고정되고 대상당 피격 횟수를 시전 전체에서 센다.
    // 실행 규칙이 달라 하드 전용 ID에만 연결하고 기존 단일 방향 패턴은 건드리지 않는다
    //
    // 시작 순간 고정: 원점은 보스의 서버 위치, 구역 순서와 구역 안 각도는 서버 난수로 한 번에 추첨한다(BossBarrageLayout)
    //   난수가 없거나 정의가 계약에 맞지 않으면 임의로 대신하지 않고 시작을 거절한다. 거절하면 예고도 알리지 않고 두뇌가 기록 없이 기존 재조회로 다시 고른다
    //
    // 시각(시전 시작 기준): 발 k의 예고 시작 = k x 간격, 발 k의 발사 = 예고 시간 + k x 간격. 예고는 발사 예고 시간 전에 시작해 발사 때까지 이어지므로 여러 발의 예고가 겹친다
    //   예고 시작과 발사는 예정 시각 순서대로 한 번씩 처리한다. 같은 시각이면 예고를 먼저 처리한다
    //   큰 틱으로 늦게 처리돼도 건너뛰지 않고 모두 처리하며 알림에는 예정 시각과 처리 시각을 따로 담는다. 투사체의 발사 시각도 예정 시각이다
    //   늦게 처리된 발의 방향과 원점은 시작 때 고정한 값 그대로이고 벽 조회만 처리 시점에 한다(정적 지형이라 같은 값이다)
    //   처리 시점에 발사 창구가 받아들이지 않은 발은 알리기만 하고 다시 쏘지 않는다. 시각이 지난 발을 뒤늦게 내면 간격 계약이 깨지기 때문이다
    //
    // 정리: 취소, 사망, 페이즈 전환, 전투 종료는 BossPatternBase가 취소 뒤 피해 요청을 막는다. 아직 처리하지 않은 예고와 발사는 더 처리하지 않고 취소 알림 하나만 낸다
    //   이미 나간 투사체는 패턴 밖의 지속물 관리자에 있으며 두뇌가 지속물 관리자에 정리를 지시한다
    /// <summary> 월백 하드 2페이즈 전방위 투사체 난사, 전방위를 같은 각도의 구역으로 나눠 구역마다 한 발씩 무작위 순서로 쏜다 </summary>
    public sealed class BossBarragePattern : BossPatternBase, IBossPatternStartResult
    {
        /****************************************
        *              Constants
        ****************************************/

        private const double TIME_EPSILON = 0.000001; // 초, 예정 시각 비교의 부동소수 오차 흡수

        // 한 시전의 발 수의 기술적 상한, 지속물 관리자의 예비 슬롯 수와 같다. 실제 수는 정의의 발 수가 정한다
        private const int    MAX_SHOTS    = 8;

        /****************************************
        *                Fields
        ****************************************/

        private readonly float                    _referenceHealth;
        private readonly IBossProjectileLauncher  _launcher;
        private readonly IBossBarrageListener     _listener;

        private BossBarrageLayout       _layout;
        private BossProjectileHitLimit  _hitLimit;
        private int                     _shotCount;
        private int                     _telegraphedCount;
        private int                     _firedCount;
        private double                  _castStart;
        private bool                    _isStarted;
        private bool                    _isRejected;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 시작 순간 고정한 배치 </summary>
        public BossBarrageLayout Layout => _layout;

        /// <summary> 이 시전의 대상별 피격 기록 </summary>
        public BossProjectileHitLimit HitLimit => _hitLimit;

        public int ShotCount => _shotCount;

        /// <summary> 지금까지 예고를 처리한 발 수 </summary>
        public int TelegraphedCount => _telegraphedCount;

        /// <summary> 지금까지 발사를 처리한 발 수, 발사 창구가 받아들이지 않은 발도 센다 </summary>
        public int FiredCount => _firedCount;

        /// <summary> 난수가 없거나 정의가 계약에 맞지 않아 시작하지 못했다 </summary>
        public bool IsStartRejected => _isRejected;

        /****************************************
        *                Methods
        ****************************************/

        // referenceHealth는 지역과 난이도에 맞는 Href다. 정하는 쪽은 서버 연결이다
        public BossBarragePattern(float referenceHealth, IBossProjectileLauncher launcher, IBossBarrageListener listener)
        {
            _referenceHealth = referenceHealth;
            _launcher        = launcher ?? throw new ArgumentNullException(nameof(launcher));
            _listener        = listener ?? throw new ArgumentNullException(nameof(listener));
        }

        /// <summary> 발 k의 예고 시작 예정 시각(서버 시각) </summary>
        public double GetTelegraphTime(int shotIndex)
        {
            return _castStart + (shotIndex * (double)Context.Definition.ProjectileIntervalSeconds);
        }

        /// <summary> 발 k의 발사 예정 시각(서버 시각) </summary>
        public double GetFireTime(int shotIndex)
        {
            return _castStart + Context.Definition.TelegraphSeconds + (shotIndex * (double)Context.Definition.ProjectileIntervalSeconds);
        }

        protected override void OnBegin(double serverTime)
        {
            PatternDefinition definition = Context.Definition;

            _isStarted        = false;
            _isRejected       = true;
            _telegraphedCount = 0;
            _firedCount       = 0;
            _castStart        = serverTime;
            _shotCount        = definition.RawProjectileCount;

            if ((_shotCount < 1) || (_shotCount > MAX_SHOTS) || (definition.TelegraphSeconds <= 0f)
                || ((_shotCount > 1) && (definition.ProjectileIntervalSeconds <= 0f))
                || (definition.Shape.Range <= 0f) || (definition.Shape.Width <= 0f) || (definition.Shape.ProjectileSpeed <= 0f)
                || (definition.ProjectileMaxHitsPerTarget < 1) || (Context.Random == null))
            {
                return;
            }

            _layout   = new BossBarrageLayout(Context.Self.Position, _shotCount, Context.Random);
            _hitLimit = new BossProjectileHitLimit(definition.ProjectileMaxHitsPerTarget);

            _isStarted  = true;
            _isRejected = false;
            _listener.OnBarrageStarted(Context.CastInstanceId, _layout.Origin, _castStart, _shotCount, definition.TelegraphSeconds,
                definition.ProjectileIntervalSeconds, definition.Shape.Width);

            ProcessDue(serverTime);
        }

        protected override void OnTick(EBossAttackState state, double serverTime)
        {
            ProcessDue(serverTime);
        }

        protected override void OnEnterState(EBossAttackState state, double serverTime)
        {
            // serverTime은 FSM이 넘기는 단계 경계라 실행 끝 경계에 놓인 마지막 발이 회복 진입에서도 빠지지 않는다
            ProcessDue(serverTime);
        }

        // 시작하지 못한 시전은 알릴 것이 없다. 시작한 시전은 아직 나가지 않은 발과 예고를 정리하라고 알린다
        protected override void OnCancelled(EBossInterruptReason reason)
        {
            if (_isStarted)
            {
                _listener.OnBarrageCancelled(Context.CastInstanceId, _telegraphedCount, _firedCount, reason);
            }
        }

        // 예정 시각이 지금까지인 예고와 발사를 시각 순서대로 한 번씩 처리한다. 순서 번호를 먼저 올려 처리 중 재진입해도 같은 일을 두 번 하지 않는다
        private void ProcessDue(double serverTime)
        {
            while (_isStarted && (!IsCancelled))
            {
                bool   hasTelegraph = _telegraphedCount < _shotCount;
                bool   hasFire      = _firedCount < _shotCount;
                if ((!hasTelegraph) && (!hasFire))
                {
                    return;
                }

                double telegraphTime = hasTelegraph ? GetTelegraphTime(_telegraphedCount) : double.MaxValue;
                double fireTime      = hasFire ? GetFireTime(_firedCount) : double.MaxValue;
                bool   isTelegraph   = hasTelegraph && (telegraphTime <= fireTime);
                double nextTime      = isTelegraph ? telegraphTime : fireTime;

                if (nextTime > (serverTime + TIME_EPSILON))
                {
                    return;
                }

                if (isTelegraph)
                {
                    int index = _telegraphedCount;
                    ++_telegraphedCount;
                    Telegraph(index, nextTime, serverTime);
                }
                else
                {
                    int index = _firedCount;
                    ++_firedCount;
                    Fire(index, nextTime, serverTime);
                }
            }
        }

        private void Telegraph(int shotIndex, double scheduledTime, double processedTime)
        {
            PatternDefinition definition = Context.Definition;
            Vector3 direction = _layout.GetDirection(shotIndex);

            // 예고가 보여 주는 길이와 실제 비행 길이가 같은 계산이다
            float pathLength = _launcher.MeasurePath(_layout.Origin, direction, definition.Shape.Range, definition.Shape.Width);

            _listener.OnBarrageShotTelegraphed(Context.CastInstanceId, shotIndex, _layout.GetSector(shotIndex), direction, pathLength,
                scheduledTime, processedTime);
        }

        // 발마다 동작 번호를 새로 받고, 원점과 방향은 시작 때 고정한 값이며 발사 시각은 예정 시각이다
        private void Fire(int shotIndex, double scheduledTime, double processedTime)
        {
            PatternDefinition definition = Context.Definition;
            EDamageFlags flags = definition.Guardable ? EDamageFlags.None : EDamageFlags.Unblockable;
            int projectileId = Context.Damage.IssueInstanceId();

            BossProjectileSpec spec = new BossProjectileSpec(Context.CastInstanceId, projectileId, Context.Self.CombatId,
                definition.DefinitionId, _layout.Origin, _layout.GetDirection(shotIndex), definition.Shape.ProjectileSpeed,
                definition.Shape.Range, definition.Shape.Width, definition.DamageRef * _referenceHealth, flags, scheduledTime,
                Context.Damage, _hitLimit);

            bool isLaunched = _launcher.TryLaunch(in spec);
            _listener.OnBarrageShotFired(Context.CastInstanceId, shotIndex, projectileId, isLaunched, scheduledTime, processedTime);
        }
    }
}
