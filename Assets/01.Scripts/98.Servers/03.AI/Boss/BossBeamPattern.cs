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
    // 줄 수와 좌우 거리와 폭과 예고 시간과 유지 시간과 피해 간격과 피해 배수는 모두 패턴 정의에서 읽는다. 이 클래스에는 밸런스 값이 없다
    //
    // 이 객체는 배치 고정과 예고와 설치만 맡는다. 활성된 빔의 수명과 주기 피해는 빔 관리자가 맡고, 유지 시간(4초)이 공격 FSM의 실행 시간과 같아 패턴은 실행이 끝날 때까지 다음 선택 없이 머문다
    //   첫 타격은 활성 순간 이 패턴이 아니라 관리자 Tick이 처리한다. 두 곳에서 중복해 맞는 일이 없다
    //
    // 시작 순간 고정(이후 타깃이 움직이거나 죽어도, 보스가 움직이거나 돌아도 바꾸지 않는다)
    //   전장 중심과 반경은 전장 입력(IBossArenaSource)에서 얻는다. 보스 위치를 전장 중심으로 보지 않는다. 얻지 못하면 시작을 거절한다
    //   기준 방향은 보스에서 유효 타깃을 향한 수평 방향이다. 유효 타깃을 조회하지 못하면 시작을 거절한다(임의 방향으로 대신 공격하지 않는다)
    //   타깃과 보스가 같은 수평 위치라 방향을 구할 수 없으면 보스의 유효 정면을 쓴다
    //   빔은 전장 중심에서 기준 방향의 좌우 offset에 폭 width로 놓이고 전장 원과의 교집합이 피해 영역이다(BossBeamGeometry)
    // 슬롯은 빔마다 예약하고 하나라도 실패하면 이미 예약한 슬롯을 모두 돌려주고 시작을 거절한다. 거절하면 예고 알림도 없고 두뇌가 기록 없이 다시 고른다
    // 빔마다 서버가 발급한 빔 번호를 갖고 시전 번호는 함께 쓴다. 예고와 활성과 취소와 슬롯 반환은 모두 빔 번호 단위다
    /// <summary> 월백 필드 절단 여우불 빔, 예고 시작 순간 평행한 빔 여러 줄의 배치를 고정하고 예고가 끝나면 빔을 활성한다 </summary>
    public sealed class BossBeamPattern : BossPatternBase, IBossPatternStartResult
    {
        /****************************************
        *              Constants
        ****************************************/

        // 한 시전이 설치할 수 있는 빔 수의 기술적 상한, 실제 수는 정의의 BeamCount가 정한다
        private const int   MAX_BEAMS_PER_CAST = 4;
        private const float MIN_DIRECTION_SQR  = 0.000001f; // m^2, 이보다 가까우면 방향을 정할 수 없다

        /****************************************
        *                Fields
        ****************************************/

        // 빔 한 줄의 계획, 배열에 담아 제자리에서 고친다
        private struct BeamPlan
        {
            public int           BeamId;
            public BossBeamShape Shape;
            public bool          IsReserved;
            public bool          IsActivated;
        }

        private readonly float                      _referenceHealth;
        private readonly IBossBeamHost              _host;
        private readonly IBossArenaSource           _arena;
        private readonly IBossBeamTelegraphListener _listener;
        private readonly BeamPlan[]                 _plans = new BeamPlan[MAX_BEAMS_PER_CAST];

        private int     _planCount;
        private Vector3 _baseDirection;
        private bool    _isRejected;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 시작 순간 고정한 기준 방향, 수평 단위 벡터 </summary>
        public Vector3 BaseDirection => _baseDirection;

        /// <summary> 이 시전이 예약한 빔 수 </summary>
        public int BeamCount => _planCount;

        /// <summary> 빔을 하나라도 활성했다 </summary>
        public bool IsActivated
        {
            get
            {
                for (int i = 0; i < _planCount; ++i)
                {
                    if (_plans[i].IsActivated)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary> 전장 정보나 유효 타깃이나 빈 슬롯이 없어 시작하지 못했다 </summary>
        public bool IsStartRejected => _isRejected;

        /****************************************
        *                Methods
        ****************************************/

        // referenceHealth는 지역과 난이도에 맞는 Href다. 정하는 쪽은 서버 연결이다
        public BossBeamPattern(float referenceHealth, IBossBeamHost host, IBossArenaSource arena,
            IBossBeamTelegraphListener listener = null)
        {
            _referenceHealth = referenceHealth;
            _host            = host ?? throw new ArgumentNullException(nameof(host));
            _arena           = arena ?? throw new ArgumentNullException(nameof(arena));
            _listener        = listener;
        }

        /// <summary> 이 시전이 예약한 i번째 빔의 번호 </summary>
        public int GetBeamId(int index)
        {
            return _plans[index].BeamId;
        }

        /// <summary> 이 시전이 예약한 i번째 빔의 기하, 시작 때 고정한 값이다 </summary>
        public BossBeamShape GetBeamShape(int index)
        {
            return _plans[index].Shape;
        }

        protected override void OnBegin(double serverTime)
        {
            PatternDefinition definition = Context.Definition;

            _planCount  = 0;
            _isRejected = true;

            if ((!_arena.TryGetArena(out Vector3 arenaCenter, out float arenaRadius)) || (arenaRadius <= 0f)
                || (!TryGetValidTarget(out ICombatTarget target)))
            {
                return;
            }

            Vector3 toTarget = target.Position - Context.Self.Position;
            toTarget.y = 0f;
            _baseDirection = (toTarget.sqrMagnitude >= MIN_DIRECTION_SQR)
                ? toTarget.normalized
                : Flatten(Context.Self.Forward, Vector3.forward);

            int count = Mathf.Clamp(definition.BeamCount, 1, MAX_BEAMS_PER_CAST);
            for (int i = 0; i < count; ++i)
            {
                int beamId = Context.Damage.IssueInstanceId();
                if (!_host.TryReserve(Context.CastInstanceId, beamId, definition.DefinitionId))
                {
                    ReleaseAll();
                    return;
                }

                _plans[_planCount] = new BeamPlan
                {
                    BeamId     = beamId,
                    Shape      = BossBeamGeometry.Place(arenaCenter, arenaRadius, _baseDirection, i, count,
                        definition.BeamLateralOffset, definition.Shape.Width),
                    IsReserved = true
                };
                ++_planCount;
            }

            _isRejected = false;

            // 모든 슬롯을 예약한 뒤에만 예고를 알린다. 거절된 시전은 표현에 아무것도 남기지 않는다
            for (int i = 0; i < _planCount; ++i)
            {
                _listener?.OnTelegraphStarted(Context.CastInstanceId, _plans[i].BeamId, in _plans[i].Shape,
                    definition.TelegraphSeconds, definition.ActiveSeconds, definition.BeamTickInterval);
            }
        }

        protected override void OnEnterState(EBossAttackState state, double serverTime)
        {
            if (state == EBossAttackState.Execute)
            {
                Activate(serverTime);
            }
        }

        // 활성 전에 끊긴 빔만 예약을 돌려주고 알린다. 활성한 뒤의 빔 소멸은 빔 관리자가 맡는다
        protected override void OnCancelled(EBossInterruptReason reason)
        {
            for (int i = 0; i < _planCount; ++i)
            {
                if (_plans[i].IsActivated || (!_plans[i].IsReserved))
                {
                    continue;
                }

                _host.ReleaseReservation(_plans[i].BeamId);
                _plans[i].IsReserved = false;
                _listener?.OnCancelled(Context.CastInstanceId, _plans[i].BeamId, reason);
            }
        }

        // 시작 중 슬롯 부족으로 거절할 때 이미 예약한 슬롯을 돌려준다, 알림은 아직 나가지 않았으므로 취소 알림도 없다
        private void ReleaseAll()
        {
            for (int i = 0; i < _planCount; ++i)
            {
                _host.ReleaseReservation(_plans[i].BeamId);
                _plans[i].IsReserved = false;
            }

            _planCount = 0;
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

        // 한 시전에 한 번만 설치한다. 활성 시각은 예고가 끝난 경계(FSM 단계 경계)다
        private void Activate(double serverTime)
        {
            PatternDefinition definition = Context.Definition;
            EDamageFlags flags = definition.Guardable ? EDamageFlags.None : EDamageFlags.Unblockable;

            for (int i = 0; i < _planCount; ++i)
            {
                if (_plans[i].IsActivated || (!_plans[i].IsReserved))
                {
                    continue;
                }

                BossBeamSpec spec = new BossBeamSpec(Context.CastInstanceId, _plans[i].BeamId, Context.Self.CombatId,
                    definition.DefinitionId, in _plans[i].Shape, definition.ActiveSeconds, definition.BeamTickInterval,
                    definition.DamageRef * _referenceHealth, flags, serverTime, Context.Damage);

                _plans[i].IsActivated = _host.TryActivate(in spec);
                if (!_plans[i].IsActivated)
                {
                    _host.ReleaseReservation(_plans[i].BeamId);
                    _plans[i].IsReserved = false;
                }
            }
        }
    }
}
