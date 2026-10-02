// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Common;

using BossDefinition = Shinshi.Data.BossPatternCatalogSO.BossDefinition;

namespace Shinshi.Combat
{
    // 보스 두뇌(BossBrain)가 읽고 쓰는 서버 쪽 몸체다. 체력, 생사, 무적, 받은 속박을 한곳에 두어 두뇌와 피해 판정이
    // 같은 상태를 본다. CombatServerRuntime이 만들고 틱마다 Tick을 부르며, 레지스트리에는 적 진영으로 등록된다.
    //
    // 치명타는 IDamageable 계약대로 그 자리에서 Dead로 바꾼다. 체력 0인 채 Alive로 남기면 조회 후보로 남아
    // 플레이어 투사체를 흡수하고 돌진을 멈추기 때문이다. 사망을 미루는 정책(최종 발악)이 생기면 이 전이를 다시 정한다.
    //
    // 받은 속박은 한 칸에 담고 Tick 끝에 비운다. 두뇌는 한 틱에 한 건만 꺼내고 페이즈 전환 틱에는 꺼내지 않으므로,
    // 비우지 않으면 지난 틱의 속박이 다음 패턴의 허용 구간에서 뒤늦게 적용된다
    /// <summary> 서버의 보스 개체, 피해를 받고 두뇌에 체력과 직접 제어를 넘기며 예고 방향으로 몸을 돌린다 </summary>
    public sealed class BossServer : IDamageable, ISkillControlTarget, IBossVitals, IBossControlSource,
        IBossFanTelegraphListener, IBossCircleTelegraphListener, IBossLineTelegraphListener
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float ROOT_DURATION_SCALE = 1f;
        private const float DIRECTION_EPSILON   = 0.0001f;

        private readonly int       _combatId;
        private readonly Vector3   _position;
        private readonly float     _maxHealth;
        private readonly float     _defense;
        private readonly BossBrain _brain;

        private Vector3               _forward;
        private float                 _health;
        private EParticipantLifeState _lifeState         = EParticipantLifeState.Alive;
        private BossDirectControl     _pendingControl;
        private bool                  _hasPendingControl = false;
        private bool                  _defeatReported    = false;

        /****************************************
        *              Properties
        ****************************************/

        public int                   CombatId      => _combatId;
        public ECombatFaction        Faction       => ECombatFaction.Enemy;
        public EParticipantLifeState LifeState     => _lifeState;
        public bool                  IsSummon      => false;
        public int                   OwnerCombatId => CombatIds.NONE;
        public Vector3               Position      => _position;
        public Vector3               Forward       => _forward;
        public float                 BodyRadius    => Constants.Combat.BOSS_BODY_RADIUS;

        public float Health          => _health;
        public float MaxHealth       => _maxHealth;
        public float Defense         => _defense;
        public bool  IsGuardingFront => false;

        // 등장 대기와 종료 뒤에는 피해와 공명을 받지 않는다
        public bool IsInvulnerable => (_lifeState != EParticipantLifeState.Alive) || (_brain.FlowState != EBossFlowState.Combat);

        public float HealthRate   => (_maxHealth > 0f) ? Mathf.Clamp01(_health / _maxHealth) : 0f;
        public bool  IsHealthZero => _health <= Constants.Combat.HEALTH_EPSILON;

        public bool  IsBoss            => true;
        public float RootDurationScale => ROOT_DURATION_SCALE;

        // 허용 구간, 1회, 면역은 두뇌가 판정하므로 여기서는 전투 중인지만 본다
        public bool AllowsRoot => (_lifeState == EParticipantLifeState.Alive) && (_brain.FlowState == EBossFlowState.Combat);

        public EBossFlowState FlowState => _brain.FlowState;

        /// <summary> 등장 대기나 전투 중이라 틱과 종료 요청을 받는다 </summary>
        public bool IsActive => (_brain.FlowState == EBossFlowState.Appearing) || (_brain.FlowState == EBossFlowState.Combat);

        /// <summary> 두뇌가 사망을 확정했는데 세션에 아직 알리지 않았다 </summary>
        public bool HasUnreportedDefeat => (_brain.IsBossDeathRecorded) && (!_defeatReported);

        public bool IsBossDeathRecorded => _brain.IsBossDeathRecorded;
        public bool IsCombatEndRecorded => _brain.IsCombatEndRecorded;

        /****************************************
        *                Methods
        ****************************************/

        // referenceAttack, referenceHealth는 전장 지역의 Aref, Href이고 healthScale은 출정 시작 때 고정한 보스 체력 배율이다
        // TODO(민성): 두 번째 보스가 생기면 패턴 팩토리를 보스 ID로 고른다. 지금은 월백만 있다
        public BossServer(string bossId, int combatId, Vector3 position, Vector3 forward,
            float referenceAttack, float referenceHealth, float healthScale,
            IBossPatternCatalog catalog, ICombatTargetQuery targets, IDamageResolver damage,
            IBossEffectLifetime effects, IBossRandom random)
        {
            if (string.IsNullOrEmpty(bossId))
            {
                throw new ArgumentException("보스 ID가 비어 있다", nameof(bossId));
            }

            if (combatId == CombatIds.NONE)
            {
                throw new ArgumentOutOfRangeException(nameof(combatId), "전투 번호가 없다");
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if ((!IsPositiveFinite(referenceAttack)) || (!IsPositiveFinite(referenceHealth)) || (!IsPositiveFinite(healthScale)))
            {
                throw new ArgumentOutOfRangeException(nameof(referenceAttack), "지역 기준값과 체력 배율은 0보다 큰 유한값이다");
            }

            _combatId = combatId;
            _position = position;
            _forward  = Flatten(forward, Vector3.forward);

            // 정의가 없거나 수치가 비어 있으면 최대 체력 0으로 남고 Begin이 거절한다
            if (catalog.TryGetBoss(bossId, out BossDefinition boss))
            {
                _maxHealth = boss.HealthRef * referenceAttack * healthScale;
                _defense   = boss.Defense;
            }

            _health = _maxHealth;

            var dependencies = new BossBrainDependencies
            {
                Catalog  = catalog,
                Self     = this,
                Vitals   = this,
                Targets  = targets,
                Damage   = damage,
                Patterns = new WolbaekPatternFactory(referenceHealth, this, this, this),
                Effects  = effects,
                Control  = this,
                Random   = random
            };

            _brain = new BossBrain(bossId, dependencies);
            _brain.DirectControlApplied += OnDirectControlApplied;
        }

        /// <summary> 등장 대기 없이 전투를 연다, 정의가 없거나 수치가 비었으면 거짓이고 전투를 열지 않는다 </summary>
        public bool Begin(double serverTime)
        {
            if (!(_maxHealth > 0f))
            {
                Debug.LogError($"[Boss] 최대 체력이 0이라 전투를 열지 않는다, combatId={_combatId}");
                return false;
            }

            return _brain.Begin(serverTime);
        }

        /// <summary> 플레이어 기술과 공격이 끝난 뒤 한 번 부른다 </summary>
        public void Tick(double serverTime)
        {
            _brain.Tick(serverTime);
            _hasPendingControl = false;
        }

        /// <summary> 세션이 끝났을 때 부른다, 같은 시각에 바로 정리해 남은 타격이 다음 틱으로 밀리지 않게 한다 </summary>
        public void EndCombat(double serverTime)
        {
            _brain.RequestCombatEnd();
            Tick(serverTime);
        }

        /// <summary> 세션이 보스 처치를 받아들였다 </summary>
        public void MarkDefeatReported()
        {
            _defeatReported = true;
        }

        public void ApplyDamage(float appliedAmount, bool isLethal, double serverTime)
        {
            if (_lifeState != EParticipantLifeState.Alive)
            {
                return;
            }

            if (isLethal)
            {
                _health    = 0f;
                _lifeState = EParticipantLifeState.Dead;
                return;
            }

            _health = Mathf.Max(0f, _health - appliedAmount);
        }

        // 한 틱에 여러 속박이 오면 첫 건만 남긴다. 두뇌가 한 패턴에 한 번만 받으므로 나머지는 어차피 버려진다
        public void ApplyRoot(string sourceId, float duration, double serverTime)
        {
            if ((!AllowsRoot) || (_hasPendingControl))
            {
                return;
            }

            _pendingControl    = new BossDirectControl(sourceId, duration);
            _hasPendingControl = true;
        }

        public bool TryConsumeDirectControl(out BossDirectControl control)
        {
            control = _pendingControl;
            if (!_hasPendingControl)
            {
                return false;
            }

            _hasPendingControl = false;
            return true;
        }

        // 예고 방향으로 몸을 돌려야 다음 선택의 방향 구간과 플레이어 공격의 후방 판정이 조준과 맞는다.
        // 부채꼴과 직사각형의 예고 시작은 매개변수 형식이 같아 인터페이스별로 나눠 구현한다
        void IBossFanTelegraphListener.OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction,
            float radius, float arcAngle, float telegraphSeconds)
        {
            Face(direction);
        }

        void IBossLineTelegraphListener.OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction,
            float length, float width, float telegraphSeconds)
        {
            Face(direction);
        }

        public void OnTelegraphStarted(int castInstanceId, Vector3 center, float radius, float telegraphSeconds)
        {
        }

        public void OnAimChanged(int castInstanceId, Vector3 direction)
        {
            Face(direction);
        }

        public void OnDirectionLocked(int castInstanceId, Vector3 direction)
        {
            Face(direction);
        }

        public void OnHit(int castInstanceId, int hitCount)
        {
        }

        public void OnCancelled(int castInstanceId, EBossInterruptReason reason)
        {
        }

        private void Face(Vector3 direction)
        {
            _forward = Flatten(direction, _forward);
        }

        // 속박 중 정지 여부는 기획 미정이라 적용 사실만 남긴다. 제어 적용은 드물어서 이때만 문자열을 만든다
        // TODO(민성): 속박 중 이동, 회전, 공격 정지가 정해지면 여기서 보스 동작에 붙인다
        private void OnDirectControlApplied(BossDirectControlApplied applied)
        {
            Debug.Log($"[Boss] 직접 제어 적용, source={applied.Control.SourceId}, seconds={applied.AppliedSeconds}, " +
                $"cancelled={applied.CancelledPattern}, pattern={applied.PatternId}");
        }

        private static Vector3 Flatten(Vector3 direction, Vector3 fallback)
        {
            direction.y = 0f;
            float sqrMagnitude = direction.sqrMagnitude;
            if (sqrMagnitude <= DIRECTION_EPSILON)
            {
                return fallback;
            }

            return direction / Mathf.Sqrt(sqrMagnitude);
        }

        private static bool IsPositiveFinite(float value)
        {
            return (value > 0f) && (!float.IsInfinity(value));
        }
    }
}
