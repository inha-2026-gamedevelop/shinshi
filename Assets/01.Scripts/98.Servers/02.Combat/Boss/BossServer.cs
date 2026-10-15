// System
using System;
using System.Collections.Generic;
using System.Text;

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
    // 치명타는 사망 정책(BossGimmickDeathPolicy)이 확정할 때만 그 자리에서 Dead로 바꾼다. 붙은 기믹이 없으면 정책이 곧바로 참이라
    // IDamageable 계약대로 즉시 Dead다. 체력 0인 채 Alive로 남기면 조회 후보로 남아 플레이어 투사체를 흡수하고 돌진을 멈추므로
    // 보류가 없는 보스를 그렇게 두지 않는다. 기믹이 사망을 보류하면(하드 월백의 0% 인계와 최후의 영역) 체력 0인 채 Alive로 남고,
    // 그동안은 무적이라 같은 치명타가 다시 적용되지 않는다. 보류가 풀린 틱에 두뇌가 사망을 확정하면 Tick이 Dead로 맞춘다.
    // 보류 중에도 몸체는 살아 있는 대상이라 조회에 남는다. 기믹이 이 몸체를 기준으로 삼기 때문이다.
    // 두뇌와 이 몸체는 같은 정책 객체를 읽는다. 따로 판단하면 한쪽만 사망으로 보는 틱이 생긴다.
    //
    // 체력 기믹 묶음(BossGimmickSet)은 AttachGimmicks로 전투를 열기 전에 한 번 받는다. 받는 피해 상한은 IDamageCapTarget으로
    // 피해 판정기에 내주고, 기믹 중 무적은 IsInvulnerable에 더한다. 묶음이 결계를 대신하면 결계 전개를 쓰지 않는다.
    //
    // 받은 속박은 한 칸에 담고 Tick 끝에 비운다. 두뇌는 한 틱에 한 건만 꺼내고 페이즈 전환 틱에는 꺼내지 않으므로,
    // 비우지 않으면 지난 틱의 속박이 다음 패턴의 허용 구간에서 뒤늦게 적용된다.
    //
    // 월백 1, 2페이즈 패턴의 창구를 모두 이 몸체가 맡는다. 돌진은 이동 창구(Move, GetClearDistance)로 몸체를 옮기고, 투사체, 장판,
    // 빔, 별형 빔은 이 몸체가 만든 지속물 관리자 넷을 팩토리와 수명 관리(Effects)에 같은 객체로 넘긴다. 지속물의 Tick은 BossCombat이 부르고
    // 피해는 패턴이 넘기는 게이트(BossDamageScaler를 거친 경로)로만 나가므로 여기서 배율을 다시 곱하지 않는다.
    //
    // 패턴 알림은 정면 갱신에 쓰고, 표현에 필요한 사건(IBossEventListener)으로 바꿔 복제 상태(BossState)에 넘긴다.
    // 시각은 틱 시각이나 FSM 단계 경계인 서버 시각만 쓴다. 표현 값(색, 진행도)은 만들지 않는다.
    // 무철 전용 패턴과 월백 하드 2페이즈 패턴의 알림은 이 몸체가 아니라 중계 둘(BossMucheolEventRelay, BossHardPhaseEventRelay)이 받아
    // 선택 계약(IBossMucheolEventListener, IBossHardPhaseEventListener)으로 보낸다. 리스너를 더 구현하면 이 클래스가 사건 변환으로만 채워지기 때문이다.
    // 중계는 정면과 시전 번호만 이 몸체에 남기고(IBossEventRelayHost), 그 시전의 정상 종료는 이 몸체가 기본 사건으로 보낸다
    //
    // 패턴 팩토리는 보스 ID로 고른다. 무철은 1페이즈와 2페이즈 팩토리를 묶어 쓰고 실행 데이터는 임시 시작값(MucheolSortieData)이다.
    // 무철 돌진 유도 기믹도 여기서 만들어 두뇌에 더하지만, 맵이 지정 구조물을 알려 주기 전에는 발동하지 않는다.
    // 맞은 대상을 미는 창구(IBossHitDisplacer)도 이 몸체다. 막힘은 보스 이동과 같은 검사로 재고 실제 위치는 대상의 서버 이동이 옮긴다.
    // 50% 전환 뒤 인계 대기는 월백이면 결계 전개(BossBarrierDirector)가 끝날 때까지 이어지고, 결계가 없는 보스는 다음 틱에 바로 끝난다.
    // 결계 위험 구역은 완료 뒤에도 사망이나 전투 종료까지 남아 2페이즈 동안 피해를 낸다
    //
    // 순간 이동(IBossWarp)의 최종 권한도 이 몸체다. 착지 검사는 전장 경계, 정적 지형(IBossStandQuery), 살아 있는 플레이어와 소환물의 몸체
    // 순서이고 TryWarp가 패턴의 사전 검사를 믿지 않고 다시 한다. 경로 검사는 길찾기가 아니라 정적 지형을 향한 직선 한 번이다.
    // 출정 준비 검사(IsSortieReady)는 1, 2페이즈의 모든 정의가 데이터 지정과 생성 연결을 마쳤는지 본다.
    // 하드 난이도는 이 검사를 통과해야만 출정을 연다(CombatServerRuntime). 연결하지 않은 표시 수신처의 패턴은 만들 수 있다고 답하지 않는다
    /// <summary> 서버의 보스 개체, 피해를 받고 두뇌에 체력과 직접 제어를 넘기며 몸체 이동과 공격 사건 전달을 맡는다 </summary>
    public sealed class BossServer : IDamageable, ISkillControlTarget, IBossVitals, IBossControlSource, IBossMovement,
        IBossFanTelegraphListener, IBossCircleTelegraphListener, IBossLineTelegraphListener, IBossDashTelegraphListener,
        IBossProjectileTelegraphListener, IBossProjectileListener, IBossAreaTelegraphListener, IBossAreaListener,
        IBossCastListener, IBossArenaSource, IBossSweepTelegraphListener, IBossBeamTelegraphListener, IBossBeamListener,
        IBossStarTelegraphListener, IBossStarListener, IBossBarrierListener, IBossWarp, IBossEventRelayHost,
        IBossHitDisplacer, IDamageCapTarget
    {
        /****************************************
        *                Fields
        ****************************************/

        private const float ROOT_DURATION_SCALE = 1f;
        private const float DIRECTION_EPSILON   = 0.0001f;
        private const float HALF                = 0.5f;
        private const float PATH_EPSILON        = 0.001f; // m, 스윕 거리가 목표에 닿았는지 보는 부동소수 여유

        // 출정 준비 검사가 보는 페이즈 범위, 월백은 2페이즈까지다
        private const int SORTIE_FIRST_PHASE = 1;
        private const int SORTIE_LAST_PHASE  = 2;

        // 결계 전개를 쓰는 보스, 결계 설정이 카탈로그로 옮겨지면 정의 값으로 고른다
        private const string BARRIER_BOSS_ID = "boss_wolbaek";

        private readonly string                _bossId;
        private readonly int                   _combatId;
        private readonly Vector3               _arenaCenter;
        private readonly float                 _arenaRadius;
        private readonly float                 _maxHealth;
        private readonly float                 _defense;
        private readonly IBossWallQuery        _terrain;
        private readonly IBossStandQuery       _standQuery;   // 지형이 겹침 검사를 지원할 때만 있다
        private readonly ICombatTargetQuery    _targets;
        private readonly IBossPatternCatalog   _catalog;
        private readonly List<ICombatTarget>   _standBuffer;  // 착지 검사가 틱마다 불려도 할당하지 않도록 재사용한다
        private readonly BossProjectileManager _projectiles;
        private readonly BossAreaManager       _areas;
        private readonly BossBeamManager       _beams;
        private readonly BossStarManager       _stars;
        private readonly IBossPatternFactory   _patterns;
        private readonly BossBrain             _brain;
        private readonly BossBarrierDirector   _barrier;

        // 무철과 월백 하드 2페이즈의 패턴 알림을 선택 계약 사건으로 바꾸는 중계, 팩토리보다 먼저 만들어 수신처로 넘긴다
        private readonly BossMucheolEventRelay   _mucheolRelay;
        private readonly BossHardPhaseEventRelay _hardRelay;
        private readonly MucheolChargeGimmick    _chargeGimmick;   // 무철일 때만 있다
        private readonly IBossDisplaceLookup     _displaceTargets; // 없으면 아무도 밀지 않는다

        // 두뇌에 넘긴 것과 같은 객체다. 치명타 처리가 두뇌와 같은 보류를 읽는다
        private readonly BossGimmickDeathPolicy _deathPolicy;

        // AttachGimmicks가 채운다. 붙이지 않은 보스는 모두 비어 있어 상한 없음, 즉시 사망, 결계 전개 그대로다
        private BossGimmickSet         _gimmicks;
        private IBossDamageCap         _damageCap;
        private IBossFinalSuccess      _finalSuccess;
        private IBossGimmickStopSource _stopSource;
        private IBossGimmickReviveGate _reviveGate;
        private bool                   _gimmickAttachTried = false;
        private bool                   _barrierSuppressed  = false; // 기믹이 결계를 대신한다, _barrier는 만들어져 있어도 쓰지 않는다

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

        // 중계가 전용 사건으로 예고한 시전, 정상 종료를 기본 사건으로 보내는 데만 쓴다. 중단된 시전은 종료 알림이 오지 않아 다음 시전이 덮어쓴다
        private int _phaseCastId = CombatIds.NONE;

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

        // 등장 대기와 종료 뒤에는 피해와 공명을 받지 않는다. 기믹이 막는 구간(기믹 중 무적, 경계에 닿은 체력)과
        // 사망이 보류돼 체력 0인 채 살아 있는 동안에도 받지 않는다. 뒤의 것이 없으면 보류 중 같은 치명타가 틱마다 다시 적용된다
        public bool IsInvulnerable => (_lifeState != EParticipantLifeState.Alive) || (_brain.FlowState != EBossFlowState.Combat)
            || ((_damageCap != null) && (_damageCap.IsDamageBlocked)) || (IsHealthZero);

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
        public bool HasBarrier => ActiveBarrier != null;

        /// <summary> 결계 전개 진행 상태, 결계가 없는 보스는 Idle에 머문다 </summary>
        public EBossBarrierState BarrierState => (ActiveBarrier != null) ? ActiveBarrier.State : EBossBarrierState.Idle;

        /// <summary> 체력 기믹 묶음이 붙었다, 생성자가 더하는 무철 돌진 유도 기믹은 세지 않는다 </summary>
        public bool HasGimmicks => _gimmicks != null;

        /// <summary> 지금 공용 부활을 해도 되는가, 기믹이 없으면 참이다 </summary>
        public bool IsReviveWindowOpen => (_reviveGate == null) || (_reviveGate.IsReviveWindowOpen);

        // 결계는 생성자에서 만들어지고 기믹은 그 뒤에 붙으므로, 기믹이 결계를 대신하면 만들어 둔 결계를 없는 것으로 읽는다.
        // 결계를 읽는 곳은 모두 이 값을 거친다
        private BossBarrierDirector ActiveBarrier => _barrierSuppressed ? null : _barrier;

        // 살아 있고 전투 중일 때만 몸체가 움직인다. 취소, 사망, 종료 뒤에 늦게 온 이동 요청은 무시한다
        private bool CanMove => (_lifeState == EParticipantLifeState.Alive) && (_brain.FlowState == EBossFlowState.Combat);

        double IBossEventRelayHost.TickTime        => _tickTime;
        string IBossEventRelayHost.ActivePatternId => _brain.ActivePatternId;

        /****************************************
        *                Methods
        ****************************************/

        // referenceAttack, referenceHealth는 전장 지역의 Aref, Href이고 healthScale은 출정 시작 때 고정한 보스 체력 배율이다.
        // arenaCenter는 원형 전장의 중심이고 반경은 보스 정의의 전장 지름에서 읽는다. terrain은 정적 지형 벽 검사이며
        // 비우면 전장 경계만 본다. targets는 패턴과 지속물 관리자가 함께 쓰는 전투 대상 조회다.
        // barrierSettings는 결계 전개를 쓰는 보스(월백)의 설정이고 비우면 임시 기본값이다. 다른 보스에는 쓰지 않는다
        // connections는 전투 번호의 현재 접속 상태를 답하는 입력이고, 하드 2페이즈 P04(추적 장판)가 접속한 대상만 고르는 데 쓴다. 비우면 그 패턴을 만들지 않는다
        // displaceTargets는 밀 수 있는 대상을 전투 번호로 찾는 입력이고, 비우면 밀림이 필요한 패턴(무철 하드 P07)을 만들지 않는다
        public BossServer(string bossId, int combatId, Vector3 position, Vector3 forward, Vector3 arenaCenter,
            float referenceAttack, float referenceHealth, float healthScale,
            IBossPatternCatalog catalog, ICombatTargetQuery targets, IDamageResolver damage,
            IBossWallQuery terrain, IBossRandom random, BossBarrierSettings barrierSettings = null,
            IBossParticipantConnection connections = null, IBossDisplaceLookup displaceTargets = null)
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
            _standQuery  = terrain as IBossStandQuery;
            _targets     = targets;
            _catalog     = catalog;
            _standBuffer = new List<ICombatTarget>(Constants.Combat.MAX_COMBAT_TARGETS);

            _displaceTargets = displaceTargets;

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

            // 중계는 팩토리가 수신처로 받으므로 팩토리보다 먼저 만든다. 사건을 받을 복제 상태는 SetEventListener가 나중에 잇는다
            _mucheolRelay = new BossMucheolEventRelay(this);
            _hardRelay    = new BossHardPhaseEventRelay(this);
            _patterns     = CreatePatterns(bossId, referenceHealth, connections, out _chargeGimmick);

            // 결계 피해는 패턴과 같은 피해 경로(damage)로 나가고 기준 체력도 패턴과 같은 Href다
            if (bossId == BARRIER_BOSS_ID)
            {
                _barrier = new BossBarrierDirector(barrierSettings ?? new BossBarrierSettings(), this, this, targets, damage,
                    _arenaCenter, _arenaRadius, referenceHealth, this);
            }

            // 보류 창구는 기믹이 붙을 때 더한다. 비어 있는 동안에는 기본 정책(체력 0이면 사망)과 같다
            _deathPolicy = new BossGimmickDeathPolicy(new ImmediateBossDeathPolicy());

            var dependencies = new BossBrainDependencies
            {
                Catalog     = catalog,
                Self        = this,
                Vitals      = this,
                Targets     = targets,
                Damage      = damage,
                Patterns    = _patterns,
                Effects     = new BossEffectComposite(_projectiles, _areas, _beams, _stars),
                Control     = this,
                Random      = random,
                Casts       = this,
                DeathPolicy = _deathPolicy
            };

            _brain = new BossBrain(bossId, dependencies);
            _brain.DirectControlApplied += OnDirectControlApplied;

            // 기믹은 전투가 열리기 전에만 받는다
            if (_chargeGimmick != null)
            {
                _brain.AddGimmick(_chargeGimmick);
            }
        }

        // 무철, 하드 페이즈 사건은 수신처가 그 선택 계약도 구현할 때만 나간다. 구현하지 않으면 중계의 수신처가 비어 보내지 않는다
        /// <summary> 공격 사건을 받을 복제 상태를 연결한다, 비우면 사건을 보내지 않는다 </summary>
        public void SetEventListener(IBossEventListener listener)
        {
            _events = listener;
            _mucheolRelay.SetListener(listener as IBossMucheolEventListener);
            _hardRelay.SetListener(listener as IBossHardPhaseEventListener);
        }

        // 두뇌가 기믹을 전투 시작 전에만 받으므로 Begin 전에만 받는다. 두 번째 호출은 첫 호출이 실패했어도 거절한다.
        // 묶음의 빈 칸은 두뇌에 손대기 전에 거르지만, 두뇌가 도중에 거절하면 이미 더한 기믹을 되돌릴 수 없다. 그때는 상한과 보류를 잇지 않고
        // 거짓을 돌려주며, 일부 기믹만 두뇌에 남은 몸체가 되므로 호출 쪽은 이 보스로 전투를 열지 말고 버려야 한다
        /// <summary> 체력 기믹 묶음을 붙인다, 전투를 열기 전에 한 번만 받고 거짓이면 이 몸체를 쓰지 않는다 </summary>
        public bool AttachGimmicks(BossGimmickSet set)
        {
            if ((set == null) || (_gimmickAttachTried) || (_brain.FlowState != EBossFlowState.Inactive))
            {
                return false;
            }

            _gimmickAttachTried = true;

            for (int i = 0; i < set.Gimmicks.Count; ++i)
            {
                if (set.Gimmicks[i] == null)
                {
                    Debug.LogError($"[Boss] 기믹 묶음의 {i}번 기믹이 비어 있어 붙이지 않는다, combatId={_combatId}");
                    return false;
                }
            }

            for (int i = 0; i < set.DeathHolds.Count; ++i)
            {
                if (set.DeathHolds[i] == null)
                {
                    Debug.LogError($"[Boss] 기믹 묶음의 {i}번 사망 보류 창구가 비어 있어 붙이지 않는다, combatId={_combatId}");
                    return false;
                }
            }

            for (int i = 0; i < set.Gimmicks.Count; ++i)
            {
                if (!_brain.AddGimmick(set.Gimmicks[i]))
                {
                    Debug.LogError($"[Boss] 두뇌가 {i}번 기믹을 거절했다, 앞선 {i}개는 이미 더해져 이 몸체를 쓸 수 없다, " +
                        $"combatId={_combatId}");
                    return false;
                }
            }

            for (int i = 0; i < set.DeathHolds.Count; ++i)
            {
                _deathPolicy.AddHold(set.DeathHolds[i]);
            }

            _gimmicks          = set;
            _damageCap         = set.DamageCap;
            _finalSuccess      = set.FinalSuccess;
            _stopSource        = set.StopSource;
            _reviveGate        = set.ReviveGate;
            _barrierSuppressed = set.ReplacesBarrier;
            return true;
        }

        /// <summary> 기믹의 정지나 전멸 아닌 실패를 읽는다, 기믹이 없거나 멈춘 것이 없으면 거짓이다 </summary>
        public bool TryGetGimmickStop(out EBossGimmickStop kind, out ESessionEndReason failureReason, out string detail)
        {
            if (_stopSource == null)
            {
                kind          = EBossGimmickStop.None;
                failureReason = default;
                detail        = string.Empty;
                return false;
            }

            return _stopSource.TryGetStop(out kind, out failureReason, out detail);
        }

        // 기믹은 체력 비율이 0에 닿았는지로 판단하므로 부동소수 오차로 양수 체력이 남을 수 있다. 남으면 보류가 풀려도
        // 기본 정책이 사망을 확정하지 않아 보스가 죽지 않으므로, 성공을 내주는 순간 체력을 0으로 맞춘다
        /// <summary> 하드 최종 성공을 한 번 소비한다, 참이면 보스 체력을 0으로 확정해 다음 두뇌 틱에 사망이 확정된다 </summary>
        public bool TryConsumeFinalSuccess(out BossFinalSuccess success)
        {
            if ((_finalSuccess == null) || (!_finalSuccess.TryConsumeFinalSuccess(out success)))
            {
                success = default;
                return false;
            }

            _health = 0f;
            return true;
        }

        /// <summary> 기믹이 정한 부활 지점을 묻는다, 기믹이 없으면 NotManaged다 </summary>
        public EBossRevivePoint TryGetGimmickRevivePoint(int combatId, out Vector3 point)
        {
            if (_reviveGate == null)
            {
                point = default;
                return EBossRevivePoint.NotManaged;
            }

            return _reviveGate.TryGetRevivePoint(combatId, out point);
        }

        // 기믹이 없으면 그대로 돌려준다. 최대 체력은 이 몸체가 알고 판정기는 모르므로 여기서 채운다
        public float CapIncomingDamage(float currentHealth, float damage)
        {
            return (_damageCap == null) ? damage : _damageCap.CapDamage(currentHealth, _maxHealth, damage);
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

            // 보류가 풀려 두뇌가 이 틱에 사망을 확정했으면 몸체도 맞춘다. 치명타 때 바로 Dead가 된 보스는 이미 Dead라 지나간다
            if ((_brain.IsBossDeathRecorded) && (_lifeState == EParticipantLifeState.Alive))
            {
                _lifeState = EParticipantLifeState.Dead;
            }

            TickBarrier(serverTime);
            _hasPendingControl = false;
        }

        /// <summary> 패턴 정의를 이 몸체의 팩토리가 만들 수 있는지, 2페이즈 창구 연결 확인과 진단용이다 </summary>
        public bool CanCreatePattern(PatternDefinition definition)
        {
            return (_patterns is IBossPatternAvailability availability) && (availability.CanCreate(definition));
        }

        // 출정 시작 때 한 번만 부르므로 목록과 문자열을 만든다. 카탈로그가 난이도로 걸러 모은 1, 2페이즈 정의를 모두 본다:
        // 각 페이즈에 정의가 하나 이상, 카탈로그가 데이터 준비를 답하면(IBossPatternReadiness) 모두 준비됨, 그리고 모두 이 몸체가 만들 수 있어야 한다
        /// <summary> 이 보스로 출정을 열 수 있는가, 거짓이면 부족한 페이즈와 정의 ID를 reason에 담는다 </summary>
        public bool IsSortieReady(out string reason)
        {
            var                   definitions = new List<PatternDefinition>();
            IBossPatternReadiness readiness   = _catalog as IBossPatternReadiness;
            StringBuilder         builder     = null;

            for (int phase = SORTIE_FIRST_PHASE; phase <= SORTIE_LAST_PHASE; ++phase)
            {
                _catalog.CollectPatterns(_bossId, phase, definitions);
                if (definitions.Count == 0)
                {
                    builder = BeginFailure(builder);
                    builder.Append("phase ").Append(phase).Append(": 정의 없음");
                    continue;
                }

                for (int i = 0; i < definitions.Count; ++i)
                {
                    PatternDefinition definition = definitions[i];
                    bool              isReady    = (readiness == null) || (readiness.IsPatternReady(_bossId, definition));
                    bool              canCreate  = CanCreatePattern(definition);
                    if ((isReady) && (canCreate))
                    {
                        continue;
                    }

                    builder = BeginFailure(builder);
                    builder.Append("phase ").Append(phase).Append(' ').Append(definition.DefinitionId).Append(": ");
                    if (!isReady)
                    {
                        builder.Append("데이터 미지정");
                    }

                    if ((!isReady) && (!canCreate))
                    {
                        builder.Append(", ");
                    }

                    if (!canCreate)
                    {
                        builder.Append("생성 불가");
                    }
                }
            }

            reason = (builder == null) ? string.Empty : builder.ToString();
            return builder == null;
        }

        /// <summary> 늦은 접속 진단용 결계 현재 값, 결계가 없거나 예고 전, 정리 뒤에는 거짓이다 </summary>
        public bool TryGetBarrierSnapshot(out BossBarrierStart start)
        {
            BossBarrierDirector barrier = ActiveBarrier;
            if (barrier == null)
            {
                start = default;
                return false;
            }

            return barrier.TryGetSnapshot(out start);
        }

        // 지정 구조물이 필요한 수만큼 정해지기 전에는 기믹이 발동하지 않는다. 구조물 식별자는 맵이 정하며 돌진 구조물 조회가 쓰는 번호와 같아야 한다
        /// <summary> 무철 돌진 유도 기믹이 부숴야 하는 지정 구조물을 정한다, 발동 전에만 받고 무철이 아니거나 식별자가 어긋나면 거짓이다 </summary>
        public bool SetChargeGimmickStructures(IReadOnlyList<int> structureIds)
        {
            return (_chargeGimmick != null) && (_chargeGimmick.SetDesignatedStructures(structureIds));
        }

        // castInstanceId는 구조물을 부순 돌진의 시전 번호다. 넘기면 확인이 늦게 와도 그 시전의 회복 종료 경계에서 그로기가 시작된다
        /// <summary> 서버가 확인한 지정 구조물 파괴를 무철 돌진 유도 기믹에 알린다, 진행도가 올랐으면 참이다 </summary>
        public bool ConfirmChargeStructureDestroyed(int structureId, double serverTime, int castInstanceId = CombatIds.NONE)
        {
            return (_chargeGimmick != null)
                && (_chargeGimmick.ConfirmStructureDestroyed(structureId, serverTime, castInstanceId));
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

            // 체력을 먼저 0으로 만든 뒤 정책에 묻는다. 보류와 기본 정책이 모두 이 몸체의 체력을 읽는다.
            // 보류 중이면 Alive로 남기고 Tick이 두뇌의 사망 확정에 맞춰 Dead로 바꾼다
            if (isLethal)
            {
                _health = 0f;
                if (_deathPolicy.IsDeathConfirmed(this, serverTime))
                {
                    _lifeState = EParticipantLifeState.Dead;
                }

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
        *              Displace
        ****************************************/

        // 막힘은 보스 이동과 같은 검사(전장 경계와 정적 지형)를 대상의 몸체 반경으로 잰다. 위치를 실제로 바꾸는 것은 대상의 서버 이동이라
        // 여기서는 목적지만 넘긴다. 패턴이 넘긴 방향을 믿지 않고 수평 단위 벡터로 다시 맞춘다
        public float TryPush(int combatId, Vector3 direction, float maxDistance)
        {
            if ((!CanMove) || (_displaceTargets == null))
            {
                return 0f;
            }

            if ((!_displaceTargets.TryGetDisplaceTarget(combatId, out IBossDisplaceTarget target)) || (target == null)
                || (!target.CanBeDisplaced))
            {
                return 0f;
            }

            direction.y = 0f;
            float sqrMagnitude = direction.sqrMagnitude;
            if (!(sqrMagnitude > DIRECTION_EPSILON))
            {
                return 0f;
            }

            direction /= Mathf.Sqrt(sqrMagnitude);

            Vector3 origin   = target.Position;
            float   distance = GetClearDistance(origin, direction, maxDistance, target.BodyRadius);
            if (!(distance > 0f))
            {
                return 0f;
            }

            target.Displace(origin + (direction * distance), _tickTime);
            return distance;
        }

        /****************************************
        *                Warp
        ****************************************/

        // 전장 경계(몸체 반경만큼 안쪽), 정적 지형, 살아 있는 플레이어와 소환물 몸체 순서로 보고 앞 검사에서 걸리면 뒤는 하지 않는다.
        // 지형 쪽은 IBossStandQuery일 때만 본다(스윕은 시작부터 겹친 벽을 못 잡는다)
        public bool CanStandAt(Vector3 point)
        {
            // NaN, 무한대는 아래 비교가 모두 거짓이 되어 통과하지만 위치에 들어가면 안 되므로 먼저 거른다
            float sum = point.x + point.y + point.z;
            if ((float.IsNaN(sum)) || (float.IsInfinity(sum)))
            {
                return false;
            }

            if (!IsInsideStandArea(point))
            {
                return false;
            }

            if ((_standQuery != null) && (!_standQuery.CanStand(point, BodyRadius)))
            {
                return false;
            }

            return !OverlapsOtherBody(point);
        }

        // 길찾기(NavMesh)가 아니라 정적 지형을 향한 직선 검사다. 서버 NavMesh는 플레이어, 동료 이동용이고 보스 몸체 크기와 틱 예산이 정해져 있지 않다.
        // 호출당 스윕 한 번이다. 목표 위치는 대상(플레이어)이 서 있는 자리라 몸체 반경만큼 못 미친 곳까지 닿으면 도달로 본다.
        // 전장 경계는 출발점만 본다: 원은 볼록이라 안쪽 두 점 사이 직선이 밖으로 나가지 않고, 경계에 붙은 대상까지 경로를 경계로 막을 이유가 없다
        public bool HasPath(Vector3 from, Vector3 to)
        {
            if (!IsInsideStandArea(from))
            {
                return false;
            }

            Vector3 delta = to - from;
            delta.y = 0f;
            float distance = delta.magnitude;
            if ((float.IsNaN(distance)) || (float.IsInfinity(distance)))
            {
                return false;
            }

            // 이미 몸체 반경 안이면 더 갈 곳이 없고, 지형 검사가 없으면 막을 것이 없다
            float reach = distance - BodyRadius;
            if ((_terrain == null) || (!(reach > PATH_EPSILON)))
            {
                return true;
            }

            float clear = _terrain.GetClearDistance(from, delta / distance, reach, BodyRadius);
            return clear >= (reach - PATH_EPSILON);
        }

        // 패턴의 사전 검사를 믿지 않고 여기서 다시 확인한다. 실패하면 위치는 그대로다
        public bool TryWarp(Vector3 point, out Vector3 landed)
        {
            if ((!CanMove) || (!CanStandAt(point)))
            {
                landed = _position;
                return false;
            }

            _position = point;
            landed    = _position;
            return true;
        }

        // 경계 위는 포함한다. 비교는 거리 제곱으로 해 제곱근을 피한다
        private bool IsInsideStandArea(Vector3 point)
        {
            float   limit  = Mathf.Max(0f, _arenaRadius - BodyRadius);
            Vector3 offset = new Vector3(point.x - _arenaCenter.x, 0f, point.z - _arenaCenter.z);
            return offset.sqrMagnitude <= (limit * limit);
        }

        // 몸체가 겹치는가, 닿기만 한 것은 겹침이 아니다. 조회 반경은 가장 큰 몸체 반경으로 넉넉히 잡고 대상별 반경으로 다시 거른다
        private bool OverlapsOtherBody(Vector3 point)
        {
            _targets.GetLivingInRadius(point, BodyRadius + Constants.Combat.MAX_BODY_RADIUS, ECombatFaction.Player, true,
                _standBuffer);

            bool overlaps = false;
            for (int i = 0; i < _standBuffer.Count; ++i)
            {
                ICombatTarget other = _standBuffer[i];
                if (other.CombatId == _combatId)
                {
                    continue;
                }

                float dx     = other.Position.x - point.x;
                float dz     = other.Position.z - point.z;
                float minGap = BodyRadius + other.BodyRadius;
                if (((dx * dx) + (dz * dz)) < (minGap * minGap))
                {
                    overlaps = true;
                    break;
                }
            }

            _standBuffer.Clear();
            return overlaps;
        }

        private static StringBuilder BeginFailure(StringBuilder builder)
        {
            if (builder == null)
            {
                return new StringBuilder();
            }

            return builder.Append("; ");
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

        // 발사, 활성으로 이미 예고가 끝난 시전은 알릴 것이 없다. 중계가 전용 사건으로 예고한 시전은 복원 기록이 이 종료 사건으로 지워진다.
        // 한 시전이 두 번호에 모두 걸려도 종료 사건은 한 번만 보낸다
        public void OnCastFinished(int castInstanceId, double serverTime)
        {
            bool isPhaseCast = castInstanceId == _phaseCastId;
            if ((castInstanceId != _castId) && (!isPhaseCast))
            {
                return;
            }

            if (isPhaseCast)
            {
                _phaseCastId = CombatIds.NONE;
            }

            ClearCast(castInstanceId);
            _events?.OnFinished(castInstanceId);
        }

        void IBossEventRelayHost.Face(Vector3 direction)
        {
            Face(direction);
        }

        // 발동 알림(OnCastExecuted)은 _castId만 보므로 이 시전에는 나가지 않는다. 타격 확정은 중계가 타별 사건으로 보낸다
        void IBossEventRelayHost.BeginPhaseCast(int castInstanceId)
        {
            _phaseCastId = castInstanceId;
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
        // TODO(기웅): 속박 중 이동, 회전, 공격 정지가 정해지면 여기서 보스 동작에 붙인다
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
        // 생성자가 두뇌보다 먼저 부르므로 _brain을 읽지 않는다. 지속물 관리자와 중계 둘은 이미 만들어져 있어야 한다.
        // 월백에는 워프 창구(이 몸체)와 접속 상태 입력을 함께 넘긴다. 하드 2페이즈의 별 단계, 격자, 연쇄 부채꼴, 추적 장판, 난사, 동시 폭발 표시 수신처는
        // 하드 중계다. 중계는 늘 있으므로 팩토리가 수신처 없는 패턴을 만드는 일이 없다. 하드 P07은 원형 예고(this)가 수신처다
        private IBossPatternFactory CreatePatterns(string bossId, float referenceHealth, IBossParticipantConnection connections,
            out MucheolChargeGimmick chargeGimmick)
        {
            chargeGimmick = null;
            if (bossId == MucheolPatternFactory.BOSS_ID)
            {
                return CreateMucheolPatterns(referenceHealth, out chargeGimmick);
            }

            return new WolbaekPatternFactory(referenceHealth, this, this, this, this, this, _projectiles, this,
                _areas, this, this, _beams, this, this, _stars, this, warp: this, starStageListener: _hardRelay,
                gridListener: _hardRelay, chainFanListener: _hardRelay, hardAreaListener: _hardRelay, connections: connections,
                barrageListener: _hardRelay, burstListener: _hardRelay);
        }

        // 1페이즈의 부채꼴, 돌진, 원형 예고는 월백과 같은 기본 알림이라 이 몸체가 받고, 콤보와 돌진 결과, 2페이즈 전부는 무철 중계가 받는다.
        // 돌진 구조물 조회는 맵 쪽 구현이 아직 없어 넘기지 않으므로 P06은 벽과 전장 경계에만 막힌다.
        // 밀림 창구는 대상 조회가 있을 때만 넘긴다. 없이 넘기면 팩토리가 하드 P07을 만들고 밀림 규칙이 빠진 채 시전된다.
        //
        // 기믹에는 묶음이 아니라 1페이즈 팩토리를 넘긴다. 기믹은 받은 팩토리가 시전 추적 수신자를 밝히는지(IBossChargeCastTrackerSource)로
        // 자신이 연결됐는지 확인하는데 묶음은 그 계약을 전달하지 않아, 묶음을 넘기면 기믹이 발동하지 않는다. P06 생성 가능 여부도 1페이즈 팩토리가 답한다
        private IBossPatternFactory CreateMucheolPatterns(float referenceHealth, out MucheolChargeGimmick chargeGimmick)
        {
            var phaseOne = new MucheolPatternFactory(referenceHealth, this, this, this, this, MucheolSortieData.CreateComboSpec(),
                _mucheolRelay, MucheolSortieData.CreateChargeSpec(), null, _mucheolRelay);

            IBossHitDisplacer displacer = (_displaceTargets != null) ? this : null;
            var phaseTwo = new MucheolPhase2PatternFactory(referenceHealth, MucheolSortieData.CreatePhaseTwoSpec(), this, _catalog,
                this, _mucheolRelay, _mucheolRelay, displacer, _mucheolRelay);

            chargeGimmick = new MucheolChargeGimmick(this, _catalog, phaseOne, _bossId, MucheolSortieData.CreateChargeGimmickSpec());
            phaseOne.SetChargeCastTracker(chargeGimmick);

            return new MucheolPatternFactoryChain(phaseOne, phaseTwo);
        }

        // 페이즈 전환 뒤 인계 대기는 결계 전개 같은 별도 진행이 끝나기를 기다리는 자리다. 결계가 없는 보스는 전환한 틱의 다음 틱
        // 처음에 바로 인계를 마쳐 그 틱부터 2페이즈 선택을 연다. 결계가 있으면 전개 시퀀스가 끝난 뒤에만 인계한다
        private void CompleteHandoffIfReady()
        {
            if (_brain.PhaseState != EBossPhaseState.AwaitingHandoff)
            {
                return;
            }

            BossBarrierDirector barrier = ActiveBarrier;
            if ((barrier != null) && (!barrier.IsCompleted))
            {
                return;
            }

            _brain.CompleteHandoff();
        }

        // 두뇌가 전환한 틱에 바로 결계를 시작하고, 완료한 틱에 바로 인계해 2페이즈 선택이 한 틱 늦지 않게 한다.
        // 전투가 끝났으면(사망, 종료 요청) 진행 중이든 완료 뒤든 위험 구역을 정리한다
        private void TickBarrier(double serverTime)
        {
            BossBarrierDirector barrier = ActiveBarrier;
            if (barrier == null)
            {
                return;
            }

            if (_brain.FlowState != EBossFlowState.Combat)
            {
                barrier.Cancel(_brain.IsBossDeathRecorded ? EBossInterruptReason.Death : EBossInterruptReason.CombatEnd);
                return;
            }

            barrier.Tick(serverTime, _brain.PhaseState == EBossPhaseState.AwaitingHandoff);
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
