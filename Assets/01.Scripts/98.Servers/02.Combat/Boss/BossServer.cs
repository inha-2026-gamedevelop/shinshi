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
    // 보스 두뇌(BossBrain)가 읽고 쓰는 서버 쪽 몸체다. 체력, 생사, 무적, 받은 속박, 위치를 한곳에 두어 두뇌와 피해 판정이
    // 같은 상태를 본다. CombatServerRuntime이 만들고 틱마다 Tick을 부르며, 레지스트리에는 적 진영으로 등록된다.
    //
    // 치명타는 IDamageable 계약대로 그 자리에서 Dead로 바꾼다. 체력 0인 채 Alive로 남기면 조회 후보로 남아
    // 플레이어 투사체를 흡수하고 돌진을 멈추기 때문이다. 사망을 미루는 정책(최종 발악)이 생기면 이 전이를 다시 정한다.
    //
    // 받은 속박은 한 칸에 담고 Tick 끝에 비운다. 두뇌는 한 틱에 한 건만 꺼내고 페이즈 전환 틱에는 꺼내지 않으므로,
    // 비우지 않으면 지난 틱의 속박이 다음 패턴의 허용 구간에서 뒤늦게 적용된다.
    //
    // 월백 여섯 패턴의 창구를 모두 이 몸체가 맡는다. 돌진은 이동 창구(Move, GetClearDistance)로 몸체를 옮기고, 투사체와 장판은
    // 이 몸체가 만든 지속물 관리자 둘을 팩토리와 수명 관리(Effects)에 같은 객체로 넘긴다. 지속물의 Tick은 BossCombat이 부르고
    // 피해는 패턴이 넘기는 게이트(BossDamageScaler를 거친 경로)로만 나가므로 여기서 배율을 다시 곱하지 않는다.
    //
    // 패턴 알림은 정면 갱신에 쓰고, 표현에 필요한 사건(IBossEventListener)으로 바꿔 복제 상태(BossState)에 넘긴다.
    // 시각은 틱 시각이나 FSM 단계 경계인 서버 시각만 쓴다. 표현 값(색, 진행도)은 만들지 않는다
    /// <summary> 서버의 보스 개체, 피해를 받고 두뇌에 체력과 직접 제어를 넘기며 몸체 이동과 공격 사건 전달을 맡는다 </summary>
    public sealed class BossServer : IDamageable, ISkillControlTarget, IBossVitals, IBossControlSource, IBossMovement,
        IBossFanTelegraphListener, IBossCircleTelegraphListener, IBossLineTelegraphListener, IBossDashTelegraphListener,
        IBossProjectileTelegraphListener, IBossProjectileListener, IBossAreaTelegraphListener, IBossAreaListener,
        IBossCastListener
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float ROOT_DURATION_SCALE = 1f;
        private const float DIRECTION_EPSILON   = 0.0001f;
        private const float HALF                = 0.5f;

        private readonly string                _bossId;
        private readonly int                   _combatId;
        private readonly Vector3               _arenaCenter;
        private readonly float                 _arenaRadius;
        private readonly float                 _maxHealth;
        private readonly float                 _defense;
        private readonly IBossWallQuery        _terrain;
        private readonly BossProjectileManager _projectiles;
        private readonly BossAreaManager       _areas;
        private readonly BossBrain             _brain;

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

        /// <summary> 등장 대기나 전투 중이라 틱과 종료 요청을 받는다 </summary>
        public bool IsActive => (_brain.FlowState == EBossFlowState.Appearing) || (_brain.FlowState == EBossFlowState.Combat);

        /// <summary> 두뇌가 사망을 확정했는데 세션에 아직 알리지 않았다 </summary>
        public bool HasUnreportedDefeat => (_brain.IsBossDeathRecorded) && (!_defeatReported);

        public bool IsBossDeathRecorded => _brain.IsBossDeathRecorded;
        public bool IsCombatEndRecorded => _brain.IsCombatEndRecorded;

        /// <summary> 날고 있는 투사체 수 </summary>
        public int ActiveProjectileCount => _projectiles.ActiveCount;

        // 살아 있고 전투 중일 때만 몸체가 움직인다. 취소, 사망, 종료 뒤에 늦게 온 이동 요청은 무시한다
        private bool CanMove => (_lifeState == EParticipantLifeState.Alive) && (_brain.FlowState == EBossFlowState.Combat);

        /****************************************
        *                Methods
        ****************************************/

        // referenceAttack, referenceHealth는 전장 지역의 Aref, Href이고 healthScale은 출정 시작 때 고정한 보스 체력 배율이다.
        // arenaCenter는 원형 전장의 중심이고 반경은 보스 정의의 전장 지름에서 읽는다. terrain은 정적 지형 벽 검사이며
        // 비우면 전장 경계만 본다. targets는 패턴과 지속물 관리자가 함께 쓰는 전투 대상 조회다
        // TODO(민성): 두 번째 보스가 생기면 패턴 팩토리를 보스 ID로 고른다. 지금은 월백만 있다
        public BossServer(string bossId, int combatId, Vector3 position, Vector3 forward, Vector3 arenaCenter,
            float referenceAttack, float referenceHealth, float healthScale,
            IBossPatternCatalog catalog, ICombatTargetQuery targets, IDamageResolver damage,
            IBossWallQuery terrain, IBossRandom random)
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

            // 팩토리와 수명 관리에 같은 관리자 객체를 넘겨야 발사, 활성한 지속물이 정리와 Tick을 받는다
            _projectiles = new BossProjectileManager(targets, this, this);
            _areas       = new BossAreaManager(targets, this);

            var dependencies = new BossBrainDependencies
            {
                Catalog  = catalog,
                Self     = this,
                Vitals   = this,
                Targets  = targets,
                Damage   = damage,
                Patterns = new WolbaekPatternFactory(referenceHealth, this, this, this, this, this, _projectiles, this,
                    _areas, this),
                Effects  = new BossEffectComposite(_projectiles, _areas),
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

        /// <summary> 플레이어 기술과 공격이 끝난 뒤 한 번 부른다 </summary>
        public void Tick(double serverTime)
        {
            _tickTime = serverTime;
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
            var start = BossTelegraphStart.Circle(_combatId, castInstanceId, _brain.ActivePatternId, center, radius,
                _tickTime, telegraphSeconds);
            _events?.OnTelegraphStarted(in start);
        }

        void IBossAreaTelegraphListener.OnCancelled(int castInstanceId, int areaId, EBossInterruptReason reason)
        {
            CancelCast(castInstanceId);
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
            _events?.OnAreaEnded(areaId, (end == EBossAreaEnd.Expired) ? EBossAreaFate.Expired : EBossAreaFate.Cancelled);
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
