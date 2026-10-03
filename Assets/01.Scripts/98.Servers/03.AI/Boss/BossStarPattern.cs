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
    // 꼭짓점 수와 배치 반경과 연결 칸 수와 폭과 회전 속도와 예고 시간과 유지 시간과 피해 간격과 피해 배수는 모두 패턴 정의에서 읽는다. 이 클래스에는 밸런스 값이 없다
    //
    // 이 객체는 배치 고정과 회전 방향 추첨과 예약과 예고와 활성만 맡는다. 활성 뒤 회전과 주기 피해는 별형 관리자가 맡고, 유지 시간(6초)이 공격 FSM의 실행 시간과 같아 패턴은 실행이 끝날 때까지 다음 선택 없이 머문다
    //   첫 타격은 활성 순간 이 패턴이 아니라 관리자 Tick이 처리한다. 두 곳에서 중복해 맞는 일이 없다
    //
    // 시작 순간 고정(이후 타깃이 움직이거나 죽어도, 보스가 움직이거나 돌아도 바꾸지 않는다)
    //   전장 중심과 반지름은 전장 입력(IBossArenaSource)에서 얻는다. 보스 위치를 전장 중심으로 보지 않는다. 얻지 못하거나 배치 반경이 전장보다 크면 시작을 거절한다
    //   초기 각도는 보스에서 유효 타깃을 향한 수평 방향이고 꼭짓점 0이 그 방향에 놓인다. 같은 수평 위치면 보스의 유효 정면을 쓴다. 유효 타깃을 조회하지 못하면 시작을 거절한다
    //   회전 방향은 두뇌와 같은 난수 의존성(BossPatternContext.Random)으로 좌우 각각 50%다. 난수가 없으면 임의로 대신하지 않고 시작을 거절한다
    //   활성 직전에 별을 다시 배치하지 않는다. 예고와 활성은 같은 배치를 쓰고 활성 순간부터 회전만 시작한다
    // 거절하면 예고도 알리지 않고 슬롯도 남기지 않으며 두뇌가 기록 없이 다시 고른다
    /// <summary> 월백 별형 회전 여우불 빔, 예고 시작 순간 별의 배치와 회전 방향을 고정하고 예고가 끝나면 활성한다 </summary>
    public sealed class BossStarPattern : BossPatternBase, IBossPatternStartResult
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float MIN_DIRECTION_SQR = 0.000001f; // m^2, 이보다 가까우면 방향을 정할 수 없다
        private const int   SIDE_COUNT        = 2;
        private const int   RIGHT_SIDE        = 1;

        /****************************************
        *                Fields
        ****************************************/

        private readonly float                      _referenceHealth;
        private readonly IBossStarHost              _host;
        private readonly IBossArenaSource           _arena;
        private readonly IBossStarTelegraphListener _listener;

        private BossStarShape _shape;
        private int           _starId;
        private bool          _isReserved;
        private bool          _isActivated;
        private bool          _isRejected;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 시작 순간 고정한 배치와 회전 값 </summary>
        public BossStarShape Shape => _shape;

        /// <summary> 이 시전의 구조 번호 </summary>
        public int StarId => _starId;

        public bool IsActivated => _isActivated;

        /// <summary> 전장 정보나 유효 타깃이나 난수나 슬롯이 없어 시작하지 못했다 </summary>
        public bool IsStartRejected => _isRejected;

        /****************************************
        *                Methods
        ****************************************/

        public BossStarPattern(float referenceHealth, IBossStarHost host, IBossArenaSource arena,
            IBossStarTelegraphListener listener = null)
        {
            _referenceHealth = referenceHealth;
            _host            = host ?? throw new ArgumentNullException(nameof(host));
            _arena           = arena ?? throw new ArgumentNullException(nameof(arena));
            _listener        = listener;
        }

        protected override void OnBegin(double serverTime)
        {
            PatternDefinition definition = Context.Definition;

            _isReserved  = false;
            _isActivated = false;
            _isRejected  = true;

            if ((!_arena.TryGetArena(out Vector3 arenaCenter, out float arenaRadius)) || (arenaRadius <= 0f)
                || (definition.StarRingRadius > arenaRadius) || (Context.Random == null)
                || (!TryGetValidTarget(out ICombatTarget target)))
            {
                return;
            }

            Vector3 toTarget = target.Position - Context.Self.Position;
            toTarget.y = 0f;
            Vector3 baseDirection = (toTarget.sqrMagnitude >= MIN_DIRECTION_SQR)
                ? toTarget.normalized
                : Flatten(Context.Self.Forward, Vector3.forward);

            _starId = Context.Damage.IssueInstanceId();
            if (!_host.TryReserve(Context.CastInstanceId, _starId, definition.DefinitionId))
            {
                return;
            }

            bool rotatesRight = Context.Random.NextInt(SIDE_COUNT) == RIGHT_SIDE;
            _shape = BossStarGeometry.Place(arenaCenter, arenaRadius, baseDirection, definition.StarRingRadius,
                definition.StarVertexCount, definition.StarConnectStep, definition.Shape.Width, definition.StarRotationDegrees,
                rotatesRight);

            _isReserved = true;
            _isRejected = false;
            _listener?.OnTelegraphStarted(Context.CastInstanceId, _starId, in _shape, definition.TelegraphSeconds,
                definition.ActiveSeconds, definition.BeamTickInterval);
        }

        protected override void OnEnterState(EBossAttackState state, double serverTime)
        {
            if (state == EBossAttackState.Execute)
            {
                Activate(serverTime);
            }
        }

        // 활성 전에 끊겼으면 예약을 돌려주고 알린다. 활성한 뒤의 소멸은 별형 관리자가 맡는다
        protected override void OnCancelled(EBossInterruptReason reason)
        {
            if (_isActivated || (!_isReserved))
            {
                return;
            }

            _host.ReleaseReservation(_starId);
            _isReserved = false;
            _listener?.OnCancelled(Context.CastInstanceId, _starId, reason);
        }

        private bool TryGetValidTarget(out ICombatTarget target)
        {
            if (Context.Targets.TryGet(Context.TargetCombatId, out target)
                && (target.LifeState == EParticipantLifeState.Alive) && (!target.IsSummon)
                && (target.Faction == ECombatFaction.Player))
            {
                return true;
            }

            target = null;
            return false;
        }

        private static Vector3 Flatten(Vector3 direction, Vector3 fallback)
        {
            direction.y = 0f;
            return (direction.sqrMagnitude < MIN_DIRECTION_SQR) ? fallback : direction.normalized;
        }

        // 한 시전에 한 번만 활성한다. 활성 시각은 예고가 끝난 경계(FSM 단계 경계)다
        private void Activate(double serverTime)
        {
            if (_isActivated || (!_isReserved))
            {
                return;
            }

            PatternDefinition definition = Context.Definition;
            EDamageFlags flags = definition.Guardable ? EDamageFlags.None : EDamageFlags.Unblockable;

            BossStarSpec spec = new BossStarSpec(Context.CastInstanceId, _starId, Context.Self.CombatId, definition.DefinitionId,
                in _shape, definition.ActiveSeconds, definition.BeamTickInterval, definition.DamageRef * _referenceHealth, flags,
                serverTime, Context.Damage);

            _isActivated = _host.TryActivate(in spec);
            if (!_isActivated)
            {
                _host.ReleaseReservation(_starId);
                _isReserved = false;
            }
        }
    }
}
