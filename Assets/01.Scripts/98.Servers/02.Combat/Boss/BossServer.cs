// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Common;

using BossDefinition    = Shinshi.Data.BossPatternCatalogSO.BossDefinition;
using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.Combat
{
    // 보스 두뇌(BossBrain)가 읽고 쓰는 서버 쪽 몸체다. 체력, 생사, 무적, 받은 속박, 위치를 한곳에 두어 두뇌와 피해 판정이
    // 같은 상태를 본다. CombatServerRuntime이 만들고 틱마다 Tick을 부르며, 레지스트리에는 적 진영으로 등록된다.
    //
    // 치명타는 IDamageable 계약대로 그 자리에서 Dead로 바꾼다. 체력 0인 채 Alive로 남기면 조회 후보로 남아
    // 플레이어 투사체를 흡수하고 돌진을 멈추기 때문이다. 사망을 미루는 정책(최종 발악)이 생기면 이 전이를 다시 정한다.
    //
    // 받은 속박은 한 칸에 담고 Tick 끝에 비운다. 두뇌는 한 틱에 한 건만 꺼내고 페이즈 전환 틱에는 꺼내지 않으므로,
    // 비우지 않으면 지난 틱의 속박이 다음 패턴의 허용 구간에서 뒤늦게 적용된다.
    //
    // 월백 1, 2페이즈 패턴의 창구를 모두 이 몸체가 맡는다. 돌진은 이동 창구(Move, GetClearDistance)로 몸체를 옮기고, 투사체, 장판,
    // 빔, 별형 빔은 이 몸체가 만든 지속물 관리자 넷을 팩토리와 수명 관리(Effects)에 같은 객체로 넘긴다. 지속물의 Tick은 BossCombat이 부르고
    // 피해는 패턴이 넘기는 게이트(BossDamageScaler를 거친 경로)로만 나가므로 여기서 배율을 다시 곱하지 않는다.
    //
    // 패턴 알림은 정면 갱신에 쓰고, 표현에 필요한 사건(IBossEventListener)으로 바꿔 복제 상태(BossState)에 넘긴다.
    // 시각은 틱 시각이나 FSM 단계 경계인 서버 시각만 쓴다. 표현 값(색, 진행도)은 만들지 않는다
    //
    // 패턴 팩토리는 보스 ID로 고른다. 무철은 패턴 없는 팩토리라 수치가 지정돼도 선택할 패턴이 없다.
    // 50% 전환 뒤 인계 대기는 월백이면 결계 전개(BossBarrierDirector)가 끝날 때까지 이어지고, 결계가 없는 보스는 다음 틱에 바로 끝난다.
    // 결계 위험 구역은 완료 뒤에도 사망이나 전투 종료까지 남아 2페이즈 동안 피해를 낸다
    /// <summary> 서버의 보스 개체, 피해를 받고 두뇌에 체력과 직접 제어를 넘기며 몸체 이동과 공격 사건 전달을 맡는다 </summary>
    public sealed class BossServer : IDamageable, ISkillControlTarget, IBossVitals, IBossControlSource, IBossMovement,
        IBossFanTelegraphListener, IBossCircleTelegraphListener, IBossLineTelegraphListener, IBossDashTelegraphListener,
        IBossProjectileTelegraphListener, IBossProjectileListener, IBossAreaTelegraphListener, IBossAreaListener,
        IBossCastListener, IBossArenaSource, IBossSweepTelegraphListener, IBossBeamTelegraphListener, IBossBeamListener,
        IBossStarTelegraphListener, IBossStarListener, IBossBarrierListener
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float ROOT_DURATION_SCALE = 1f;
        private const float DIRECTION_EPSILON   = 0.0001f;
        private const float HALF                = 0.5f;

        // 결계 전개를 쓰는 보스, 결계 설정이 카탈로그로 옮겨지면 정의 값으로 고른다
        private const string BARRIER_BOSS_ID = "boss_wolbaek";

        private readonly string                _bossId;
        private readonly int                   _combatId;
        private readonly Vector3               _arenaCenter;
        private readonly float                 _arenaRadius;
        private readonly float                 _maxHealth;
        private readonly float                 _defense;
        private readonly IBossWallQuery        _terrain;
        private readonly BossProjectileManager _projectiles;
        private readonly BossAreaManager       _areas;
        private readonly BossBeamManager       _beams;
        private readonly BossStarManager       _stars;
        private readonly IBossPatternFactory   _patterns;
        private readonly BossBrain             _brain;
        private readonly BossBarrierDirector   _barrier;

        private Vector3               _position;
        private Vector3               _forward;
        private float                 _health;
        private EParticipantLifeState _lifeState         = EParticipantLifeState.Alive;
        private BossDirectControl     _pendingControl;
        private bool                  _hasPendingControl = false;
        private bool                  _defeatReported    = false;
        private IBossEventListener    _events;
        private double                _tickTime;

        // 지금 예고 중인 시전 하나, 보스는 한 번에 한 시전만 한다. 조준 알림에 길이가 없는 패턴의 길이와 발동 알림의 형태를 여기서 채운다
        private int                 _castId    = CombatIds.NONE;
        private EBossTelegraphShape _castShape = EBossTelegraphShape.Fan;
        private float               _castLength;
        private bool                _castIsArea = false;

        // 반월 연쇄의 고정한 중심과 반경, 구역 예고 알림에 없어 시작 알림에서 받아 둔다. 시전은 한 번에 하나다
        private Vector3 _sweepOrigin;
        private float   _sweepRadius;

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

        /// <summary> 보스 정의 ID, 전투 번호와 다르다 </summary>
        public string BossId => _bossId;

        public Vector3 ArenaCenter => _arenaCenter;
        public float   ArenaRadius => _arenaRadius;

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

        public EBossFlowState  FlowState  => _brain.FlowState;
        public EBossPhaseState PhaseState => _brain.PhaseState;
        public int            Phase      => _brain.Phase;

        /// <summary> 등장 대기나 전투 중이라 틱과 종료 요청을 받는다 </summary>
        public bool IsActive => (_brain.FlowState == EBossFlowState.Appearing) || (_brain.FlowState == EBossFlowState.Combat);

        /// <summary> 두뇌가 사망을 확정했는데 세션에 아직 알리지 않았다 </summary>
        public bool HasUnreportedDefeat => (_brain.IsBossDeathRecorded) && (!_defeatReported);

        public bool IsBossDeathRecorded => _brain.IsBossDeathRecorded;
        public bool IsCombatEndRecorded => _brain.IsCombatEndRecorded;

        /// <summary> 날고 있는 투사체 수 </summary>
        public int ActiveProjectileCount => _projectiles.ActiveCount;

        /// <summary> 활성되어 피해를 내고 있는 빔 수 </summary>
        public int ActiveBeamCount => _beams.ActiveCount;

        /// <summary> 활성되어 회전하고 있는 별형 빔 구조 수 </summary>
        public int ActiveStarCount => _stars.ActiveCount;

        /// <summary> 50% 전환 뒤 결계 전개를 쓰는 보스다, 거짓이면 인계 대기는 다음 틱에 바로 끝난다 </summary>
        public bool HasBarrier => _barrier != null;

        /// <summary> 결계 전개 진행 상태, 결계가 없는 보스는 Idle에 머문다 </summary>
        public EBossBarrierState BarrierState => (_barrier != null) ? _barrier.State : EBossBarrierState.Idle;

        // 살아 있고 전투 중일 때만 몸체가 움직인다. 취소, 사망, 종료 뒤에 늦게 온 이동 요청은 무시한다
        private bool CanMove => (_lifeState == EParticipantLifeState.Alive) && (_brain.FlowState == EBossFlowState.Combat);

        /****************************************
        *                Methods
        ****************************************/

        // referenceAttack, referenceHealth는 전장 지역의 Aref, Href이고 healthScale은 출정 시작 때 고정한 보스 체력 배율이다.
        // arenaCenter는 원형 전장의 중심이고 반경은 보스 정의의 전장 지름에서 읽는다. terrain은 정적 지형 벽 검사이며
        // 비우면 전장 경계만 본다. targets는 패턴과 지속물 관리자가 함께 쓰는 전투 대상 조회다.
        // barrierSettings는 결계 전개를 쓰는 보스(월백)의 설정이고 비우면 임시 기본값이다. 다른 보스에는 쓰지 않는다
        public BossServer(string bossId, int combatId, Vector3 position, Vector3 forward, Vector3 arenaCenter,
            float referenceAttack, float referenceHealth, float healthScale,
            IBossPatternCatalog catalog, ICombatTargetQuery targets, IDamageResolver damage,
            IBossWallQuery terrain, IBossRandom random, BossBarrierSettings barrierSettings = null)
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

            if (targets == null)
            {
                throw new ArgumentNullException(nameof(targets));
            }

            if ((!IsPositiveFinite(referenceAttack)) || (!IsPositiveFinite(referenceHealth)) || (!IsPositiveFinite(healthScale)))
            {
                throw new ArgumentOutOfRangeException(nameof(referenceAttack), "지역 기준값과 체력 배율은 0보다 큰 유한값이다");
            }

            _bossId      = bossId;
            _combatId    = combatId;
            _position    = position;
            _forward     = Flatten(forward, Vector3.forward);
            _arenaCenter = arenaCenter;
            _terrain     = terrain;

            // 정의가 없거나 수치가 비어 있으면 최대 체력 0으로 남고 Begin이 거절한다
            if (catalog.TryGetBoss(bossId, out BossDefinition boss))
            {
                _maxHealth   = boss.HealthRef * referenceAttack * healthScale;
                _defense     = boss.Defense;
                _arenaRadius = boss.ArenaDiameter * HALF;
            }

            _health = _maxHealth;

            // 팩토리와 수명 관리에 같은 관리자 객체를 넘겨야 발사, 활성한 지속물이 정리와 Tick을 받는다.
            // 2페이즈 빔과 별형 빔도 같은 이유로 한 객체를 두 곳에 넘기며, 전장 입력은 이 몸체가 가진 중심과 반경이다
            _projectiles = new BossProjectileManager(targets, this, this);
            _areas       = new BossAreaManager(targets, this);
            _beams       = new BossBeamManager(targets, this);
            _stars       = new BossStarManager(targets, this);
            _patterns    = CreatePatterns(bossId, referenceHealth);

            // 결계 피해는 패턴과 같은 피해 경로(damage)로 나가고 기준 체력도 패턴과 같은 Href다
            if (bossId == BARRIER_BOSS_ID)
            {
                _barrier = new BossBarrierDirector(barrierSettings ?? new BossBarrierSettings(), this, this, targets, damage,
                    _arenaCenter, _arenaRadius, referenceHealth, this);
            }

            var dependencies = new BossBrainDependencies
            {
                Catalog  = catalog,
                Self     = this,
                Vitals   = this,
                Targets  = targets,
                Damage   = damage,
                Patterns = _patterns,
                Effects  = new BossEffectComposite(_projectiles, _areas, _beams, _stars),
                Control  = this,
                Random   = random,
                Casts    = this
            };

            _brain = new BossBrain(bossId, dependencies);
            _brain.DirectControlApplied += OnDirectControlApplied;
        }

        /// <summary> 공격 사건을 받을 복제 상태를 연결한다, 비우면 사건을 보내지 않는다 </summary>
        public void SetEventListener(IBossEventListener listener)
        {
            _events = listener;
        }

        /// <summary> 등장 대기 없이 전투를 연다, 정의가 없거나 수치가 비었으면 거짓이고 전투를 열지 않는다 </summary>
        public bool Begin(double serverTime)
        {
            if (!(_maxHealth > 0f))
            {
                Debug.LogError($"[Boss] 최대 체력이 0이라 전투를 열지 않는다, combatId={_combatId}");
                return false;
            }

            // 전장 지름이 비어 있으면 돌진, 예고, 투사체 길이가 모두 0이 되어 조용히 공격이 사라진다
            if (!(_arenaRadius > BodyRadius))
            {
                Debug.LogError($"[Boss] 전장 반경이 몸체 반경 이하라 전투를 열지 않는다, combatId={_combatId}, " +
                    $"arenaRadius={_arenaRadius}");
                return false;
            }

            _tickTime = serverTime;
            return _brain.Begin(serverTime);
        }

        // 두뇌가 이 틱에 사망이나 종료를 확정하면 같은 틱에 결계도 정리한다. 이후에는 IsActive가 거짓이라 Tick이 오지 않는다
        /// <summary> 플레이어 기술과 공격이 끝난 뒤 한 번 부른다 </summary>
        public void Tick(double serverTime)
        {
            _tickTime = serverTime;
            CompleteHandoffIfReady();
            _brain.Tick(serverTime);
            TickBarrier(serverTime);
            _hasPendingControl = false;
        }

        /// <summary> 패턴 정의를 이 몸체의 팩토리가 만들 수 있는지, 2페이즈 창구 연결 확인과 진단용이다 </summary>
        public bool CanCreatePattern(PatternDefinition definition)
        {
            return (_patterns is IBossPatternAvailability availability) && (availability.CanCreate(definition));
        }

        /// <summary> 늦은 접속 진단용 결계 현재 값, 결계가 없거나 예고 전, 정리 뒤에는 거짓이다 </summary>
        public bool TryGetBarrierSnapshot(out BossBarrierStart start)
        {
            if (_barrier == null)
            {
                start = default;
                return false;
            }

            return _barrier.TryGetSnapshot(out start);
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

        // 원형 전장 경계와 정적 지형 중 먼저 막히는 곳까지다. 투사체의 경로 길이도 같은 검사를 쓰므로 전장 경계가 벽처럼 동작한다
        public float GetClearDistance(Vector3 origin, Vector3 direction, float maxDistance, float bodyRadius)
        {
            if (maxDistance <= 0f)
            {
                return 0f;
            }

            float clear = Mathf.Min(maxDistance,
                BossArenaRules.DistanceToBoundary(_arenaCenter, _arenaRadius, origin, direction, bodyRadius));

            if (_terrain != null)
            {
                clear = Mathf.Min(clear, _terrain.GetClearDistance(origin, direction, maxDistance, bodyRadius));
            }

            return Mathf.Clamp(clear, 0f, maxDistance);
        }

        public Vector3 Move(Vector3 from, Vector3 to, float bodyRadius)
        {
            if (!CanMove)
            {
                return _position;
            }

            Vector3 delta    = to - from;
            float   distance = delta.magnitude;
            if (distance <= DIRECTION_EPSILON)
            {
                return _position;
            }

            Vector3 direction = delta / distance;
            _position = from + (direction * GetClearDistance(from, direction, distance, bodyRadius));
            return _position;
        }

        /****************************************
        *               P02 Fan
        ****************************************/

        // 예고 방향으로 몸을 돌려야 다음 선택의 방향 구간과 플레이어 공격의 후방 판정이 조준과 맞는다.
        // 여러 리스너에 같은 형식의 알림이 있어 모두 인터페이스별로 나눠 구현한다
        void IBossFanTelegraphListener.OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction,
            float radius, float arcAngle, float telegraphSeconds)
        {
            Face(direction);
            BeginCast(castInstanceId, EBossTelegraphShape.Fan, radius);
            var start = BossTelegraphStart.Fan(_combatId, castInstanceId, _brain.ActivePatternId, origin, direction, radius,
                arcAngle, _tickTime, telegraphSeconds);
            _events?.OnTelegraphStarted(in start);
        }

        void IBossFanTelegraphListener.OnAimChanged(int castInstanceId, Vector3 direction)
        {
            AimFixedLength(castInstanceId, direction);
        }

        void IBossFanTelegraphListener.OnDirectionLocked(int castInstanceId, Vector3 direction)
        {
            LockFixedLength(castInstanceId, direction);
        }

        // 발동은 적중 수와 상관없이 OnCastExecuted가 알린다
        void IBossFanTelegraphListener.OnHit(int castInstanceId, int hitCount)
        {
        }

        void IBossFanTelegraphListener.OnCancelled(int castInstanceId, EBossInterruptReason reason)
        {
            CancelCast(castInstanceId);
        }

        /****************************************
        *              P03 Circle
        ****************************************/

        void IBossCircleTelegraphListener.OnTelegraphStarted(int castInstanceId, Vector3 center, float radius,
            float telegraphSeconds)
        {
            BeginCast(castInstanceId, EBossTelegraphShape.Circle, radius);
            var start = BossTelegraphStart.Circle(_combatId, castInstanceId, _brain.ActivePatternId, center, radius,
                _tickTime, telegraphSeconds);
            _events?.OnTelegraphStarted(in start);
        }

        void IBossCircleTelegraphListener.OnHit(int castInstanceId, int hitCount)
        {
        }

        void IBossCircleTelegraphListener.OnCancelled(int castInstanceId, EBossInterruptReason reason)
        {
            CancelCast(castInstanceId);
        }

        /****************************************
        *               P06 Line
        ****************************************/

        void IBossLineTelegraphListener.OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction,
            float length, float width, float telegraphSeconds)
        {
            Face(direction);
            BeginCast(castInstanceId, EBossTelegraphShape.Line, length);
            var start = BossTelegraphStart.Line(_combatId, castInstanceId, _brain.ActivePatternId, origin, direction, length,
                width, _tickTime, telegraphSeconds);
            _events?.OnTelegraphStarted(in start);
        }

        void IBossLineTelegraphListener.OnAimChanged(int castInstanceId, Vector3 direction)
        {
            AimFixedLength(castInstanceId, direction);
        }

        void IBossLineTelegraphListener.OnDirectionLocked(int castInstanceId, Vector3 direction)
        {
            LockFixedLength(castInstanceId, direction);
        }

        void IBossLineTelegraphListener.OnHit(int castInstanceId, int hitCount)
        {
        }

        void IBossLineTelegraphListener.OnCancelled(int castInstanceId, EBossInterruptReason reason)
        {
            CancelCast(castInstanceId);
        }

        /****************************************
        *               P01 Dash
        ****************************************/

        void IBossDashTelegraphListener.OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction,
            float length, float width, float telegraphSeconds)
        {
            Face(direction);
            BeginCast(castInstanceId, EBossTelegraphShape.Dash, length);
            var start = BossTelegraphStart.Dash(_combatId, castInstanceId, _brain.ActivePatternId, origin, direction, length,
                width, _tickTime, telegraphSeconds);
            _events?.OnTelegraphStarted(in start);
        }

        void IBossDashTelegraphListener.OnAimChanged(int castInstanceId, Vector3 direction, float length)
        {
            AimWithLength(castInstanceId, direction, length);
        }

        void IBossDashTelegraphListener.OnDirectionLocked(int castInstanceId, Vector3 direction, float length)
        {
            LockWithLength(castInstanceId, direction, length);
        }

        void IBossDashTelegraphListener.OnDashStarted(int castInstanceId, Vector3 origin, Vector3 direction, float distance,
            float seconds, float speed, double startServerTime)
        {
            Face(direction);
            _events?.OnDashStarted(castInstanceId, distance, seconds, speed, startServerTime);
        }

        void IBossDashTelegraphListener.OnDashStopped(int castInstanceId, Vector3 position, bool stoppedByWall)
        {
            _events?.OnDashStopped(castInstanceId, position, stoppedByWall);
        }

        void IBossDashTelegraphListener.OnHit(int castInstanceId, int hitCount)
        {
        }

        void IBossDashTelegraphListener.OnCancelled(int castInstanceId, EBossInterruptReason reason)
        {
            CancelCast(castInstanceId);
        }

        /****************************************
        *            P04 Projectile
        ****************************************/

        void IBossProjectileTelegraphListener.OnTelegraphStarted(int castInstanceId, Vector3 origin, Vector3 direction,
            float length, float width, float telegraphSeconds)
        {
            Face(direction);
            BeginCast(castInstanceId, EBossTelegraphShape.Projectile, length);
            var start = BossTelegraphStart.Projectile(_combatId, castInstanceId, _brain.ActivePatternId, origin, direction,
                length, width, _tickTime, telegraphSeconds);
            _events?.OnTelegraphStarted(in start);
        }

        void IBossProjectileTelegraphListener.OnAimChanged(int castInstanceId, Vector3 direction, float length)
        {
            AimWithLength(castInstanceId, direction, length);
        }

        void IBossProjectileTelegraphListener.OnDirectionLocked(int castInstanceId, Vector3 direction, float length)
        {
            LockWithLength(castInstanceId, direction, length);
        }

        void IBossProjectileTelegraphListener.OnCancelled(int castInstanceId, EBossInterruptReason reason)
        {
            CancelCast(castInstanceId);
        }

        // 발사하면 같은 번호의 예고는 끝나고 투사체는 패턴이 끝나도 따로 남는다
        void IBossProjectileListener.OnProjectileLaunched(int castInstanceId, int projectileId, Vector3 origin, Vector3 direction,
            float speed, float width, float pathLength, double launchServerTime)
        {
            ClearCast(castInstanceId);
            var start = new BossProjectileStart(_combatId, projectileId, castInstanceId, origin, direction, speed, width, pathLength,
                launchServerTime);
            _events?.OnProjectileLaunched(in start);
        }

        void IBossProjectileListener.OnProjectileEnded(int castInstanceId, int projectileId, EBossProjectileEnd end, Vector3 position,
            int hitTargetId)
        {
            _events?.OnProjectileEnded(projectileId, ToFate(end), position);
        }

        /****************************************
        *               P05 Area
        ****************************************/

        void IBossAreaTelegraphListener.OnTelegraphStarted(int castInstanceId, int areaId, Vector3 center, float radius,
            float telegraphSeconds)
        {
            BeginCast(castInstanceId, EBossTelegraphShape.Circle, radius);
            _castIsArea = true;

            // 한 시전이 장판을 여럿 깔 수 있어(2페이즈 P04) 예고 번호는 장판 번호, 묶음 번호는 시전 번호로 보낸다.
            // 복원 기록과 표현은 활성 알림의 장판 번호로 예고를 끝내므로 같은 번호여야 예고가 남지 않는다
            var start = BossTelegraphStart.Circle(_combatId, areaId, _brain.ActivePatternId, center, radius,
                _tickTime, telegraphSeconds, castInstanceId);
            _events?.OnTelegraphStarted(in start);
        }

        void IBossAreaTelegraphListener.OnCancelled(int castInstanceId, int areaId, EBossInterruptReason reason)
        {
            ClearCast(castInstanceId);
            _events?.OnCancelled(areaId);
        }

        // 활성하면 같은 번호의 예고는 끝나고 장판은 패턴이 끝나도 따로 남는다
        void IBossAreaListener.OnAreaActivated(int castInstanceId, int areaId, Vector3 center, float radius, float durationSeconds,
            float tickInterval, double activateServerTime)
        {
            ClearCast(castInstanceId);
            var start = new BossAreaStart(_combatId, areaId, castInstanceId, center, radius, durationSeconds, tickInterval,
                activateServerTime);
            _events?.OnAreaActivated(in start);
        }

        void IBossAreaListener.OnAreaTick(int castInstanceId, int areaId, int hitIndex, double roundServerTime, int hitCount)
        {
            _events?.OnAreaTicked(areaId, hitIndex, roundServerTime);
        }

        void IBossAreaListener.OnAreaEnded(int castInstanceId, int areaId, EBossAreaEnd end, double endServerTime)
        {
            _events?.OnAreaEnded(areaId, ToAreaFate(end));
        }

        /****************************************
        *        P03 (Phase 2) Sweep
        ****************************************/

        // 반월 연쇄는 시전 예고(BeginCast)를 쓰지 않는다. 구역마다 따로 예고가 나가고 구역 폭발이 발동이라 시전 발동, 종료 알림과 겹치지 않는다
        void IBossSweepTelegraphListener.OnSweepStarted(int castInstanceId, Vector3 origin, Vector3 baseDirection, bool isRight,
            float radius, float totalArcAngle, int zoneCount, float telegraphSeconds, float blastSpanSeconds)
        {
            Face(baseDirection);
            _sweepOrigin = origin;
            _sweepRadius = radius;
            var start = new BossSweepStart(_combatId, castInstanceId, origin, baseDirection, isRight, radius, totalArcAngle,
                telegraphSeconds, blastSpanSeconds, _tickTime);
            _events?.OnSweepStarted(in start);
        }

        // 구역 번호를 예고 키로, 시전 번호를 묶음 번호로 실어 같은 시전의 구역 예고끼리 서로 끝내지 않게 한다
        void IBossSweepTelegraphListener.OnZoneScheduled(int castInstanceId, int zoneId, int zoneIndex, Vector3 centerDirection,
            float zoneArcAngle, float secondsUntilBlast)
        {
            var zone = BossTelegraphStart.Fan(_combatId, zoneId, _brain.ActivePatternId, _sweepOrigin, centerDirection,
                _sweepRadius, zoneArcAngle, _tickTime, secondsUntilBlast, castInstanceId);
            _events?.OnSweepZoneScheduled(in zone);
        }

        void IBossSweepTelegraphListener.OnZoneBlasted(int castInstanceId, int zoneId, int zoneIndex, int hitCount,
            double blastServerTime)
        {
            _events?.OnSweepZoneBlasted(zoneId, blastServerTime);
        }

        void IBossSweepTelegraphListener.OnCancelled(int castInstanceId, EBossInterruptReason reason)
        {
            _events?.OnSweepCancelled(castInstanceId);
        }

        /****************************************
        *       P02 (Phase 2) Field Beam
        ****************************************/

        // 빔은 줄마다 빔 번호가 키다. 시전 예고(BeginCast)를 쓰지 않으므로 시전 발동, 종료 알림은 이 시전에서 아무것도 보내지 않는다
        void IBossBeamTelegraphListener.OnTelegraphStarted(int castInstanceId, int beamId, in BossBeamShape shape,
            float telegraphSeconds, float durationSeconds, float tickInterval)
        {
            var start = new BossBeamStart(_combatId, beamId, castInstanceId, in shape, _tickTime, telegraphSeconds,
                durationSeconds, tickInterval);
            _events?.OnBeamTelegraphStarted(in start);
        }

        void IBossBeamTelegraphListener.OnCancelled(int castInstanceId, int beamId, EBossInterruptReason reason)
        {
            _events?.OnBeamCancelled(beamId);
        }

        void IBossBeamListener.OnBeamActivated(int castInstanceId, int beamId, float durationSeconds, float tickInterval,
            double activateServerTime)
        {
            _events?.OnBeamActivated(beamId, activateServerTime);
        }

        void IBossBeamListener.OnBeamTick(int castInstanceId, int beamId, int hitIndex, double roundServerTime, int hitCount)
        {
            _events?.OnBeamTicked(beamId, hitIndex, roundServerTime);
        }

        void IBossBeamListener.OnBeamEnded(int castInstanceId, int beamId, EBossAreaEnd end, double endServerTime)
        {
            _events?.OnBeamEnded(beamId, ToAreaFate(end));
        }

        /****************************************
        *      P01 (Phase 2) Rotating Star
        ****************************************/

        // 별형 구조는 구조 번호가 키다. 기준 방향으로 몸을 돌리는 것은 로컬 시험 연결(BossTelegraphLocalBridge)과 같다
        void IBossStarTelegraphListener.OnTelegraphStarted(int castInstanceId, int starId, in BossStarShape shape,
            float telegraphSeconds, float durationSeconds, float tickInterval)
        {
            Face(shape.BaseDirection);
            var start = new BossStarStart(_combatId, starId, castInstanceId, in shape, _tickTime, telegraphSeconds,
                durationSeconds, tickInterval);
            _events?.OnStarTelegraphStarted(in start);
        }

        void IBossStarTelegraphListener.OnCancelled(int castInstanceId, int starId, EBossInterruptReason reason)
        {
            _events?.OnStarCancelled(starId);
        }

        void IBossStarListener.OnStarActivated(int castInstanceId, int starId, float durationSeconds, float tickInterval,
            double activateServerTime)
        {
            _events?.OnStarActivated(starId, activateServerTime);
        }

        void IBossStarListener.OnStarTick(int castInstanceId, int starId, int hitIndex, double roundServerTime, int hitCount)
        {
            _events?.OnStarTicked(starId, hitIndex, roundServerTime);
        }

        void IBossStarListener.OnStarEnded(int castInstanceId, int starId, EBossAreaEnd end, double endServerTime)
        {
            _events?.OnStarEnded(starId, ToAreaFate(end));
        }

        /****************************************
        *            50% Barrier
        ****************************************/

        // 결계는 시전 예고(BeginCast)를 쓰지 않는다. 인계는 알림이 아니라 TickBarrier가 완료 여부를 보고 부른다
        void IBossBarrierListener.OnBarrierStarted(in BossBarrierStart start)
        {
            _events?.OnBarrierStarted(in start);
        }

        void IBossBarrierListener.OnBarrierActivated(int barrierId, double activateServerTime)
        {
            _events?.OnBarrierActivated(barrierId, activateServerTime);
        }

        void IBossBarrierListener.OnBarrierCompleted(int barrierId, double completeServerTime)
        {
            _events?.OnBarrierCompleted(barrierId, completeServerTime);
        }

        // 예고 전에 끊기면 번호가 0이고 클라이언트에 보낸 결계가 없으므로 알리지 않는다
        void IBossBarrierListener.OnBarrierCleared(int barrierId, EBossInterruptReason reason)
        {
            if (barrierId == CombatIds.NONE)
            {
                return;
            }

            _events?.OnBarrierCleared(barrierId);
        }

        /****************************************
        *               Arena
        ****************************************/

        // 전장 기준 패턴(2페이즈 P01, P02)의 입력이다. 반경은 몸체 반경을 빼지 않은 전장 원 그대로다
        public bool TryGetArena(out Vector3 center, out float radius)
        {
            center = _arenaCenter;
            radius = _arenaRadius;
            return _arenaRadius > 0f;
        }

        /****************************************
        *               Shared
        ****************************************/

        // 발동 알림은 한 번에 터지는 부채꼴, 원형, 직사각형에만 보낸다. 돌진, 투사체, 장판은 돌진 시작, 발사, 활성이 발동이다
        public void OnCastExecuted(int castInstanceId, double serverTime)
        {
            if (castInstanceId != _castId)
            {
                return;
            }

            // 장판은 활성 알림이 먼저 와서 시전을 비웠어야 한다. 남아 있으면 설치에 실패한 것이라 피해 없는 폭발 대신 중단으로 끝낸다
            if (_castIsArea)
            {
                CancelCast(castInstanceId);
                return;
            }

            if (IsSingleStrikeShape(_castShape))
            {
                _events?.OnStruck(castInstanceId, serverTime);
            }
        }

        // 발사, 활성으로 이미 예고가 끝난 시전은 알릴 것이 없다
        public void OnCastFinished(int castInstanceId, double serverTime)
        {
            if (castInstanceId != _castId)
            {
                return;
            }

            ClearCast(castInstanceId);
            _events?.OnFinished(castInstanceId);
        }

        private void BeginCast(int castInstanceId, EBossTelegraphShape shape, float length)
        {
            _castId     = castInstanceId;
            _castShape  = shape;
            _castLength = length;
            _castIsArea = false;
        }

        private void ClearCast(int castInstanceId)
        {
            if (castInstanceId == _castId)
            {
                _castId = CombatIds.NONE;
            }
        }

        // 예고 끝 경계에서 나온 조준 갱신도 틱 시각으로 싣는다. 표현은 이전 시각의 갱신만 거절하므로 순서가 지켜진다
        private void AimFixedLength(int castInstanceId, Vector3 direction)
        {
            Face(direction);
            if (castInstanceId != _castId)
            {
                return;
            }

            _events?.OnAimChanged(castInstanceId, direction, _castLength, _tickTime);
        }

        private void LockFixedLength(int castInstanceId, Vector3 direction)
        {
            Face(direction);
            if (castInstanceId != _castId)
            {
                return;
            }

            _events?.OnDirectionLocked(castInstanceId, direction, _castLength);
        }

        private void AimWithLength(int castInstanceId, Vector3 direction, float length)
        {
            Face(direction);
            if (castInstanceId != _castId)
            {
                return;
            }

            _castLength = length;
            _events?.OnAimChanged(castInstanceId, direction, length, _tickTime);
        }

        private void LockWithLength(int castInstanceId, Vector3 direction, float length)
        {
            Face(direction);
            if (castInstanceId != _castId)
            {
                return;
            }

            _castLength = length;
            _events?.OnDirectionLocked(castInstanceId, direction, length);
        }

        // 발동 뒤 회복 중의 취소도 그대로 보낸다. 표현은 발동한 시전의 취소를 무시하고 복원 기록은 그 시전을 지운다
        private void CancelCast(int castInstanceId)
        {
            ClearCast(castInstanceId);
            _events?.OnCancelled(castInstanceId);
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

        private static bool IsSingleStrikeShape(EBossTelegraphShape shape)
        {
            return (shape == EBossTelegraphShape.Fan) || (shape == EBossTelegraphShape.Circle)
                || (shape == EBossTelegraphShape.Line);
        }

        // 보스 ID로 팩토리를 고른다. 무철 외의 ID는 월백 팩토리로 두어 기존 월백 경로와 시험 보스 정의가 그대로 돈다.
        // 무철의 2연타(P05)와 돌진(P06)은 이 서버가 아직 연타 예고, 돌진 구조물 수신자를 구현하지 않아 넘기지 않으며 팩토리가 고르지 않는다
        private IBossPatternFactory CreatePatterns(string bossId, float referenceHealth)
        {
            if (bossId == MucheolPatternFactory.BOSS_ID)
            {
                return new MucheolPatternFactory(referenceHealth, this, this, this, this);
            }

            return new WolbaekPatternFactory(referenceHealth, this, this, this, this, this, _projectiles, this,
                _areas, this, this, _beams, this, this, _stars, this);
        }

        // 페이즈 전환 뒤 인계 대기는 결계 전개 같은 별도 진행이 끝나기를 기다리는 자리다. 결계가 없는 보스는 전환한 틱의 다음 틱
        // 처음에 바로 인계를 마쳐 그 틱부터 2페이즈 선택을 연다. 결계가 있으면 전개 시퀀스가 끝난 뒤에만 인계한다
        private void CompleteHandoffIfReady()
        {
            if (_brain.PhaseState != EBossPhaseState.AwaitingHandoff)
            {
                return;
            }

            if ((_barrier != null) && (!_barrier.IsCompleted))
            {
                return;
            }

            _brain.CompleteHandoff();
        }

        // 두뇌가 전환한 틱에 바로 결계를 시작하고, 완료한 틱에 바로 인계해 2페이즈 선택이 한 틱 늦지 않게 한다.
        // 전투가 끝났으면(사망, 종료 요청) 진행 중이든 완료 뒤든 위험 구역을 정리한다
        private void TickBarrier(double serverTime)
        {
            if (_barrier == null)
            {
                return;
            }

            if (_brain.FlowState != EBossFlowState.Combat)
            {
                _barrier.Cancel(_brain.IsBossDeathRecorded ? EBossInterruptReason.Death : EBossInterruptReason.CombatEnd);
                return;
            }

            _barrier.Tick(serverTime, _brain.PhaseState == EBossPhaseState.AwaitingHandoff);
            CompleteHandoffIfReady();
        }

        private static EBossAreaFate ToAreaFate(EBossAreaEnd end)
        {
            return (end == EBossAreaEnd.Expired) ? EBossAreaFate.Expired : EBossAreaFate.Cancelled;
        }

        private static EBossProjectileFate ToFate(EBossProjectileEnd end)
        {
            switch (end)
            {
                case EBossProjectileEnd.Hit:
                    return EBossProjectileFate.Hit;
                case EBossProjectileEnd.Wall:
                    return EBossProjectileFate.Wall;
                case EBossProjectileEnd.Range:
                    return EBossProjectileFate.Range;
                default:
                    return EBossProjectileFate.Cancelled;
            }
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
