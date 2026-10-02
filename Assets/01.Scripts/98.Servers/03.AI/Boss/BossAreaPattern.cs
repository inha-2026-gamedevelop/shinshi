// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;

using PatternDefinition = Shinshi.Data.BossPatternCatalogSO.PatternDefinition;

namespace Shinshi.AI
{
    // 시간과 반경과 지속 시간과 간격과 동시 상한과 피해 배수는 모두 패턴 정의에서 읽는다. 이 클래스에는 밸런스 값이 없다
    // 이 객체는 위치 고정과 예고와 설치만 맡는다. 활성된 장판의 수명과 주기 피해는 패턴이 끝나도 장판 관리자가 이어서 처리하므로
    // 지속 시간(6초)은 공격 FSM의 실행 시간이 아니고, 취소와 정상 종료(Finish)는 이미 활성된 장판을 건드리지 않는다
    //
    // 대상 수는 패턴 정의의 AreaTargetCount가 정한다. 0이나 1이면 선택된 타깃 한 명(조회가 안 되면 보스 위치)의 위치에 장판 하나이고 1페이즈 P05가 이 경우다
    // 2 이상이면 서로 다른 생존 플레이어에게 장판을 하나씩 동시에 설치한다
    //   첫 대상은 두뇌가 고른 타깃이고, 나머지는 남은 생존 참가 플레이어 중 두뇌와 같은 난수 의존성으로 균등하게 뽑는다. 같은 플레이어는 두 번 뽑지 않는다
    //   소환물은 설치 대상이 아니다(범위 피해는 받는다). 위치를 얻지 못한 대상에게는 설치하지 않고 보스 위치로 대신하지도 않는다
    //   슬롯은 장판마다 한 개씩 보스 전체 상한(MaxConcurrentAreas) 안에서 예약한다. 빈 슬롯이 모자라면 설치 수가 줄고, 하나도 없으면 시작을 거절해 두뇌가 기록 없이 다시 고르게 한다
    //   장판마다 서버가 발급한 장판 번호를 갖고 시전 번호는 함께 쓴다. 예고와 활성과 취소와 슬롯 반환은 모두 장판 번호 단위다
    /// <summary> 월백 지정 여우불 장판, 예고 시작 순간 대상의 위치를 고정하고 예고가 끝나면 장판을 활성한다 </summary>
    public sealed class BossAreaPattern : BossPatternBase, IBossPatternStartResult
    {
        /****************************************
        *              Constants
        ****************************************/

        // 한 시전이 설치할 수 있는 장판 수의 기술적 상한, 실제 수는 정의의 AreaTargetCount가 정한다
        private const int MAX_AREAS_PER_CAST = 4;

        /****************************************
        *                Fields
        ****************************************/

        // 장판 한 개의 계획, 배열에 담아 제자리에서 고친다
        private struct AreaPlan
        {
            public int     AreaId;
            public Vector3 Center;
            public bool    IsReserved;
            public bool    IsActivated;
        }

        private readonly float                     _referenceHealth;
        private readonly IBossAreaHost             _host;
        private readonly IBossAreaTelegraphListener _listener;
        private readonly AreaPlan[]                _plans   = new AreaPlan[MAX_AREAS_PER_CAST];
        private readonly List<ICombatTarget>       _players = new List<ICombatTarget>();

        private int  _planCount;
        private bool _isRejected;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 첫 장판의 중심, 예고 시작 때 고정한 값이고 이후 바뀌지 않는다 </summary>
        public Vector3 Center => (_planCount > 0) ? _plans[0].Center : Vector3.zero;

        /// <summary> 첫 장판의 슬롯을 예약했다, 예약이 실패하면 이 시전은 아무것도 하지 않는다 </summary>
        public bool IsReserved => (_planCount > 0) && _plans[0].IsReserved;

        /// <summary> 장판을 하나라도 활성했다 </summary>
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

        /// <summary> 이 시전이 예약한 장판 수 </summary>
        public int AreaCount => _planCount;

        /// <summary> 여러 대상 시전에서 대상이나 빈 슬롯이 없어 시작하지 못했다. 단일 대상 시전은 기존대로 거절하지 않는다 </summary>
        public bool IsStartRejected => _isRejected;

        /****************************************
        *                Methods
        ****************************************/

        // referenceHealth는 지역과 난이도에 맞는 Href다. 정하는 쪽은 서버 연결이다
        public BossAreaPattern(float referenceHealth, IBossAreaHost host, IBossAreaTelegraphListener listener = null)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            _referenceHealth = referenceHealth;
            _host            = host;
            _listener        = listener;
        }

        /// <summary> 이 시전이 예약한 i번째 장판의 번호 </summary>
        public int GetAreaId(int index)
        {
            return _plans[index].AreaId;
        }

        /// <summary> 이 시전이 예약한 i번째 장판의 중심 </summary>
        public Vector3 GetAreaCenter(int index)
        {
            return _plans[index].Center;
        }

        protected override void OnBegin(double serverTime)
        {
            PatternDefinition definition = Context.Definition;

            _planCount  = 0;
            _isRejected = false;

            if (definition.RawAreaTargetCount > 1)
            {
                PlanMultiple(definition);
                _isRejected = _planCount == 0;
                return;
            }

            PlanSingle(definition);
        }

        protected override void OnEnterState(EBossAttackState state, double serverTime)
        {
            if (state == EBossAttackState.Execute)
            {
                Activate(serverTime);
            }
        }

        // 활성 전에 끊긴 장판만 예약을 돌려주고 알린다. 활성한 뒤의 장판 소멸은 장판 관리자가 맡는다
        protected override void OnCancelled(EBossInterruptReason reason)
        {
            for (int i = 0; i < _planCount; ++i)
            {
                if (_plans[i].IsActivated || (!_plans[i].IsReserved))
                {
                    continue;
                }

                _host.ReleaseReservation(_plans[i].AreaId);
                _plans[i].IsReserved = false;
                _listener?.OnCancelled(Context.CastInstanceId, _plans[i].AreaId, reason);
            }
        }

        // 1페이즈 P05, 예고 시작 순간 선택된 타깃의 서버 위치를 고정하고 타깃이 조회되지 않으면 보스 위치를 쓴다. 장판 번호는 시전 번호와 같다
        private void PlanSingle(PatternDefinition definition)
        {
            Vector3 center = Context.Targets.TryGet(Context.TargetCombatId, out ICombatTarget target)
                ? target.Position
                : Context.Self.Position;

            // 슬롯 예약, 선택 때 빈자리를 확인했어도 시작 순간에 다시 예약해 둔다. 실패하면 예고도 설치도 하지 않는다
            TryPlan(Context.CastInstanceId, center, definition.MaxConcurrentAreas, false);
        }

        // 서로 다른 생존 플레이어에게 하나씩, 위치를 얻은 대상에게만 예약한다. 슬롯이 가득 차면 거기서 멈춘다
        private void PlanMultiple(PatternDefinition definition)
        {
            int wanted = Math.Min(definition.AreaTargetCount, MAX_AREAS_PER_CAST);
            int firstId = CombatIds.NONE;

            if (Context.Targets.TryGet(Context.TargetCombatId, out ICombatTarget first) && IsValidTarget(first))
            {
                firstId = first.CombatId;
                if (!TryPlanTarget(first, definition))
                {
                    return;
                }
            }

            if ((_planCount >= wanted) || (Context.Random == null))
            {
                return;
            }

            // 첫 대상을 뺀 생존 참가 플레이어(미접속 생존자 포함, 소환물 제외)에서 균등하게 한 명씩 뽑아 서로 다른 대상을 만든다
            Context.Targets.GetLivingPlayers(_players);
            for (int i = _players.Count - 1; i >= 0; --i)
            {
                if ((_players[i].CombatId == firstId) || (!IsValidTarget(_players[i])))
                {
                    _players.RemoveAt(i);
                }
            }

            while ((_planCount < wanted) && (_players.Count > 0))
            {
                int index = Context.Random.NextInt(_players.Count);
                ICombatTarget chosen = _players[index];
                _players[index] = _players[_players.Count - 1];
                _players.RemoveAt(_players.Count - 1);

                if (!TryPlanTarget(chosen, definition))
                {
                    break;
                }
            }

            _players.Clear();
        }

        private bool TryPlanTarget(ICombatTarget target, PatternDefinition definition)
        {
            return TryPlan(Context.Damage.IssueInstanceId(), target.Position, definition.MaxConcurrentAreas, true);
        }

        // 슬롯을 예약하고 성공하면 예고를 알린다. 예약이 실패하면 아무것도 남기지 않아 슬롯이 새지 않는다
        private bool TryPlan(int areaId, Vector3 center, int maxConcurrent, bool limitIsTotal)
        {
            PatternDefinition definition = Context.Definition;

            bool isReserved = limitIsTotal
                ? _host.TryReserveWithinTotal(Context.CastInstanceId, areaId, definition.DefinitionId, maxConcurrent)
                : _host.TryReserve(Context.CastInstanceId, areaId, definition.DefinitionId, maxConcurrent);
            if (!isReserved)
            {
                return false;
            }

            _plans[_planCount] = new AreaPlan { AreaId = areaId, Center = center, IsReserved = true };
            ++_planCount;

            _listener?.OnTelegraphStarted(Context.CastInstanceId, areaId, center, definition.Shape.Range, definition.TelegraphSeconds);
            return true;
        }

        private static bool IsValidTarget(ICombatTarget target)
        {
            return (target.LifeState == EParticipantLifeState.Alive) && (!target.IsSummon) && (target.Faction == ECombatFaction.Player);
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

                BossAreaSpec spec = new BossAreaSpec(Context.CastInstanceId, _plans[i].AreaId, Context.Self.CombatId,
                    definition.DefinitionId, _plans[i].Center, definition.Shape.Range, definition.AreaDurationSeconds,
                    definition.AreaTickInterval, definition.DamageRef * _referenceHealth, flags, serverTime, Context.Damage,
                    definition.AreaSkipsMissedRounds);

                _plans[i].IsActivated = _host.TryActivate(in spec);
                if (!_plans[i].IsActivated)
                {
                    _host.ReleaseReservation(_plans[i].AreaId);
                    _plans[i].IsReserved = false;
                }
            }
        }
    }
}
