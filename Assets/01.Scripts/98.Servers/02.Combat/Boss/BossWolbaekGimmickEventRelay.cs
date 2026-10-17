// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Combat
{
    // 월백 체력 기믹 4종의 서버 알림을 표현용 사건(IBossWolbaekGimmickEventListener)으로 바꿔 보낸다. 하드 페이즈 중계(BossHardPhaseEventRelay)와 같은 방식이다.
    // 표현에 쓰지 않는 적중 수, 거리, 비율, 남은 수와 서버 전용 중단 사유는 버리고, 실패, 정지, 정리는 모두 정리 사건 하나로 보낸다.
    // 기믹이 넘긴 목록은 서버가 다음 차례에 다시 쓰는 버퍼일 수 있어, 사건과 복원 기록이 들고 있도록 시작 순간에 복사한다.
    // 봉인 차례 판정의 파괴 핵은 알림에 수만 있어 BindSeal로 받은 기믹에서 핵마다 읽어 비트 마스크로 싣는다.
    // 색 결계 성공에는 성공 순간의 페이즈 세대를 싣는다. 몸체가 틱이 끝난 뒤 세대를 올리므로 같은 틱의 전환보다 앞선 값이다.
    // 워프 출발은 몸체가 착지 예고보다 먼저 NotifyWarpDeparted로 넘긴다.
    //
    // 리스너마다 알림 이름이 겹치는 것(OnCounterEnded 등)이 있어 모두 인터페이스별로 나눠 구현한다
    /// <summary> 월백 체력 기믹 알림을 복제 상태가 받는 월백 기믹 사건으로 바꾸는 중계 </summary>
    public sealed class BossWolbaekGimmickEventRelay : IWolbaekTalismanListener, IWolbaekColorWardListener,
        IWolbaekFoxfireSealListener, IWolbaekFinalAreaListener
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int MASK_BITS = 8;

        /****************************************
        *                Fields
        ****************************************/

        private readonly IBossEventRelayHost _host;

        private IBossWolbaekGimmickEventListener _sink;
        private WolbaekFoxfireSeal               _seal; // 파괴 핵을 읽을 봉인 기믹, 없으면 마스크는 0이다

        /****************************************
        *                Methods
        ****************************************/

        public BossWolbaekGimmickEventRelay(IBossEventRelayHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        /// <summary> 사건을 받을 수신처를 바꾼다, 비우면 사건을 보내지 않는다 </summary>
        public void SetListener(IBossWolbaekGimmickEventListener listener)
        {
            _sink = listener;
        }

        /// <summary> 차례 판정의 파괴 핵을 읽을 봉인 기믹을 잇는다 </summary>
        public void BindSeal(WolbaekFoxfireSeal seal)
        {
            _seal = seal;
        }

        /// <summary> 워프 직전 출발 위치를 알린다, 몸체가 착지 원형 예고보다 먼저 부른다 </summary>
        public void NotifyWarpDeparted(int castInstanceId, Vector3 departure, double serverTime)
        {
            _sink?.OnWarpDeparted(castInstanceId, departure, serverTime);
        }

        /****************************************
        *            75% Talisman
        ****************************************/

        void IWolbaekTalismanListener.OnFormationStarted(in WolbaekTalismanStart start)
        {
            if (_sink == null)
            {
                return;
            }

            var copy = new WolbaekTalismanStart(start.BossCombatId, start.CastInstanceId, start.StartServerTime,
                start.TransitionEndServerTime, start.HoldSeconds, start.Center, Copy(start.Structures));
            _sink.OnTalismanStarted(in copy);
        }

        void IWolbaekTalismanListener.OnHoldStarted(int castInstanceId, double serverTime)
        {
            _sink?.OnTalismanHoldStarted(castInstanceId, serverTime);
        }

        void IWolbaekTalismanListener.OnHoldReset(int castInstanceId, double serverTime, EWolbaekHoldReset reason, int combatId)
        {
            _sink?.OnTalismanHoldReset(castInstanceId, serverTime, reason, combatId);
        }

        void IWolbaekTalismanListener.OnAttackTelegraphed(int castInstanceId, in WolbaekTalismanAttack attack)
        {
            _sink?.OnTalismanAttackTelegraphed(castInstanceId, in attack);
        }

        void IWolbaekTalismanListener.OnAttackStruck(int castInstanceId, int attackId, double strikeServerTime, int hitCount)
        {
            _sink?.OnTalismanAttackStruck(castInstanceId, attackId, strikeServerTime);
        }

        void IWolbaekTalismanListener.OnAttackCancelled(int castInstanceId, int attackId)
        {
            _sink?.OnTalismanAttackCancelled(castInstanceId, attackId);
        }

        void IWolbaekTalismanListener.OnFormationSucceeded(int castInstanceId, double serverTime, double counterEndServerTime)
        {
            _sink?.OnTalismanSucceeded(castInstanceId, serverTime, counterEndServerTime);
        }

        void IWolbaekTalismanListener.OnCounterEnded(int castInstanceId, double serverTime)
        {
            _sink?.OnTalismanCounterEnded(castInstanceId);
        }

        void IWolbaekTalismanListener.OnFormationFailed(int castInstanceId, double serverTime, EWolbaekFormationFail reason)
        {
            _sink?.OnTalismanCleared(castInstanceId);
        }

        void IWolbaekTalismanListener.OnFormationHalted(int castInstanceId, double serverTime, EWolbaekHaltReason reason)
        {
            _sink?.OnTalismanCleared(castInstanceId);
        }

        void IWolbaekTalismanListener.OnFormationCleared(int castInstanceId, EBossInterruptReason reason)
        {
            _sink?.OnTalismanCleared(castInstanceId);
        }

        /****************************************
        *            50% Color Ward
        ****************************************/

        void IWolbaekColorWardListener.OnWardStarted(in WolbaekWardStart start)
        {
            if (_sink == null)
            {
                return;
            }

            var copy = new WolbaekWardStart(start.BossCombatId, start.CastInstanceId, start.StartServerTime, start.GatherCenter,
                start.GatherRadius, start.GatherHoldSeconds, start.WardRadius, start.LinkWarnDistance, start.LinkDamageDistance,
                Copy(start.Members));
            _sink.OnWardStarted(in copy);
        }

        void IWolbaekColorWardListener.OnGatherHoldStarted(int castInstanceId, double serverTime)
        {
            _sink?.OnGatherHoldStarted(castInstanceId, serverTime);
        }

        void IWolbaekColorWardListener.OnGatherHoldReset(int castInstanceId, double serverTime, EWolbaekHoldReset reason,
            int combatId)
        {
            _sink?.OnGatherHoldReset(castInstanceId);
        }

        void IWolbaekColorWardListener.OnGatherCompleted(int castInstanceId, double serverTime)
        {
            _sink?.OnGatherCompleted(castInstanceId);
        }

        void IWolbaekColorWardListener.OnStageStarted(int castInstanceId, in WolbaekWardStage stage)
        {
            _sink?.OnStageStarted(castInstanceId, in stage);
        }

        void IWolbaekColorWardListener.OnStrikeTelegraphed(int castInstanceId, in WolbaekWardStrike strike)
        {
            _sink?.OnStrikeTelegraphed(castInstanceId, in strike);
        }

        void IWolbaekColorWardListener.OnStrikeResolved(int castInstanceId, int stageIndex, int strikeIndex,
            double strikeServerTime, int hitCount, int protectedCount)
        {
            _sink?.OnStrikeResolved(castInstanceId, stageIndex, strikeIndex, strikeServerTime);
        }

        void IWolbaekColorWardListener.OnLinkDamaged(int castInstanceId, double serverTime, int combatIdA, int combatIdB,
            float distance, float rate)
        {
            _sink?.OnLinkDamaged(castInstanceId, serverTime);
        }

        void IWolbaekColorWardListener.OnGraceStarted(int castInstanceId, int combatId, double graceEndServerTime)
        {
            _sink?.OnGraceStarted(castInstanceId, combatId, graceEndServerTime);
        }

        void IWolbaekColorWardListener.OnGraceEnded(int castInstanceId, double serverTime, int combatId, EWolbaekGraceEnd reason)
        {
            _sink?.OnGraceEnded(castInstanceId, combatId);
        }

        void IWolbaekColorWardListener.OnReviveWindowOpened(int castInstanceId, double serverTime)
        {
            _sink?.OnReviveWindowOpened(castInstanceId);
        }

        void IWolbaekColorWardListener.OnWardSucceeded(int castInstanceId, double serverTime)
        {
            _sink?.OnWardSucceeded(castInstanceId, _host.PhaseGeneration);
        }

        void IWolbaekColorWardListener.OnWardFailed(int castInstanceId, double serverTime, EWolbaekWardFail reason)
        {
            _sink?.OnWardCleared(castInstanceId);
        }

        void IWolbaekColorWardListener.OnWardHalted(int castInstanceId, double serverTime, EWolbaekWardHaltReason reason)
        {
            _sink?.OnWardCleared(castInstanceId);
        }

        void IWolbaekColorWardListener.OnWardCleared(int castInstanceId, EBossInterruptReason reason)
        {
            _sink?.OnWardCleared(castInstanceId);
        }

        /****************************************
        *            25% Foxfire Seal
        ****************************************/

        void IWolbaekFoxfireSealListener.OnSealStarted(in WolbaekSealStart start)
        {
            _sink?.OnSealStarted(in start);
        }

        void IWolbaekFoxfireSealListener.OnTurnStarted(int castInstanceId, in WolbaekSealTurn turn)
        {
            if (_sink == null)
            {
                return;
            }

            var copy = new WolbaekSealTurn(turn.Index, turn.StartServerTime, turn.TrackStartServerTime, turn.LockServerTime,
                turn.ResolveServerTime, turn.EndServerTime, Copy(turn.Cores), Copy(turn.Foxfires));
            _sink.OnSealTurnStarted(castInstanceId, in copy);
        }

        void IWolbaekFoxfireSealListener.OnTrackingStarted(int castInstanceId, int turnIndex, double serverTime)
        {
            _sink?.OnSealTrackingStarted(castInstanceId, turnIndex, serverTime);
        }

        void IWolbaekFoxfireSealListener.OnFoxfireDetached(int castInstanceId, int turnIndex, int foxfireIndex,
            double serverTime, Vector3 position)
        {
            _sink?.OnSealFoxfireDetached(castInstanceId, turnIndex, foxfireIndex, serverTime, position);
        }

        void IWolbaekFoxfireSealListener.OnSweepTelegraphed(int castInstanceId, in WolbaekSealSweep sweep)
        {
            _sink?.OnSealSweepTelegraphed(castInstanceId, in sweep);
        }

        void IWolbaekFoxfireSealListener.OnSweepResolved(int castInstanceId, int turnIndex, int sweepIndex, double serverTime,
            int hitCount)
        {
            _sink?.OnSealSweepResolved(castInstanceId, turnIndex, sweepIndex, serverTime);
        }

        void IWolbaekFoxfireSealListener.OnFoxfiresLocked(int castInstanceId, int turnIndex, double serverTime,
            IReadOnlyList<Vector3> centers)
        {
            _sink?.OnSealFoxfiresLocked(castInstanceId, turnIndex, serverTime, Copy(centers));
        }

        void IWolbaekFoxfireSealListener.OnTurnResolved(int castInstanceId, int turnIndex, double serverTime, int destroyedCores,
            int totalCores, int playerHits, bool succeeded, int successCount)
        {
            _sink?.OnSealTurnResolved(castInstanceId, turnIndex, serverTime, successCount, BuildCoreMask(totalCores));
        }

        void IWolbaekFoxfireSealListener.OnSealSucceeded(int castInstanceId, double serverTime, double counterEndServerTime)
        {
            _sink?.OnSealSucceeded(castInstanceId, serverTime, counterEndServerTime);
        }

        void IWolbaekFoxfireSealListener.OnCounterEnded(int castInstanceId, double serverTime, bool reachedNextBoundary)
        {
            _sink?.OnSealCounterEnded(castInstanceId);
        }

        void IWolbaekFoxfireSealListener.OnSealFailed(int castInstanceId, double serverTime, EWolbaekSealFail reason)
        {
            _sink?.OnSealCleared(castInstanceId);
        }

        void IWolbaekFoxfireSealListener.OnSealHalted(int castInstanceId, double serverTime, EWolbaekSealHaltReason reason)
        {
            _sink?.OnSealCleared(castInstanceId);
        }

        void IWolbaekFoxfireSealListener.OnSealCleared(int castInstanceId, EBossInterruptReason reason)
        {
            _sink?.OnSealCleared(castInstanceId);
        }

        /****************************************
        *          Hard 0% Final Area
        ****************************************/

        void IWolbaekFinalAreaListener.OnFinalStarted(in WolbaekFinalStart start)
        {
            _sink?.OnFinalStarted(in start);
        }

        void IWolbaekFinalAreaListener.OnOuterFloorRemoved(int castInstanceId, double serverTime, IReadOnlyList<int> fallen)
        {
            _sink?.OnFinalOuterFloorRemoved(castInstanceId, serverTime);
        }

        // 추락 사망은 참가자 생사 복제가 이미 보여 준다. 기믹 표현에 따로 쓰는 곳이 없어 보내지 않는다
        void IWolbaekFinalAreaListener.OnParticipantsFell(int castInstanceId, double serverTime, IReadOnlyList<int> fallen)
        {
        }

        void IWolbaekFinalAreaListener.OnYellowsSpawned(int castInstanceId, double serverTime,
            IReadOnlyList<WolbaekFinalYellow> yellows)
        {
            _sink?.OnFinalYellowsSpawned(castInstanceId, Copy(yellows));
        }

        void IWolbaekFinalAreaListener.OnYellowDestroyed(int castInstanceId, int yellowIndex, double serverTime, int remaining)
        {
            _sink?.OnFinalYellowDestroyed(castInstanceId, yellowIndex, serverTime);
        }

        void IWolbaekFinalAreaListener.OnBundleStarted(int castInstanceId, in WolbaekFinalBundle bundle)
        {
            if (_sink == null)
            {
                return;
            }

            var copy = new WolbaekFinalBundle(bundle.Index, bundle.StartServerTime, bundle.LockServerTime, bundle.BlastServerTime,
                Copy(bundle.Reds));
            _sink.OnFinalBundleStarted(castInstanceId, in copy);
        }

        void IWolbaekFinalAreaListener.OnRedDetached(int castInstanceId, int bundleIndex, int redIndex, double serverTime,
            Vector3 position)
        {
            _sink?.OnFinalRedDetached(castInstanceId, bundleIndex, redIndex, serverTime, position);
        }

        void IWolbaekFinalAreaListener.OnRedsLocked(int castInstanceId, int bundleIndex, double serverTime,
            IReadOnlyList<Vector3> centers)
        {
            _sink?.OnFinalRedsLocked(castInstanceId, bundleIndex, serverTime, Copy(centers));
        }

        void IWolbaekFinalAreaListener.OnBundleResolved(int castInstanceId, int bundleIndex, double serverTime, int singleHits,
            int doubleKills)
        {
            _sink?.OnFinalBundleResolved(castInstanceId, bundleIndex, serverTime);
        }

        void IWolbaekFinalAreaListener.OnFinalSucceeded(int castInstanceId, double serverTime)
        {
            _sink?.OnFinalSucceeded(castInstanceId, serverTime);
        }

        void IWolbaekFinalAreaListener.OnFinalFailed(int castInstanceId, double serverTime, EWolbaekFinalFail reason)
        {
            _sink?.OnFinalCleared(castInstanceId);
        }

        void IWolbaekFinalAreaListener.OnFinalHalted(int castInstanceId, double serverTime, EWolbaekFinalHaltReason reason)
        {
            _sink?.OnFinalCleared(castInstanceId);
        }

        void IWolbaekFinalAreaListener.OnFinalCleared(int castInstanceId, EBossInterruptReason reason)
        {
            _sink?.OnFinalCleared(castInstanceId);
        }

        /****************************************
        *               Shared
        ****************************************/

        // 비트 수를 넘는 핵은 싣지 못한다. 복제 상한(BOSS_WOLBAEK_MAX_CORES)과 같은 8이라 그보다 많은 차례는 보내기 전에 걸러진다
        private byte BuildCoreMask(int totalCores)
        {
            if (_seal == null)
            {
                return 0;
            }

            int count = Mathf.Min(totalCores, MASK_BITS);
            int mask  = 0;
            for (int i = 0; i < count; ++i)
            {
                if (_seal.IsCoreDestroyed(i))
                {
                    mask |= 1 << i;
                }
            }

            return (byte)mask;
        }

        // 사건이 시작 순간 한 번 만드는 복사본이다. 차례와 묶음은 몇 초에 한 번이라 할당을 받아들인다
        private static T[] Copy<T>(IReadOnlyList<T> values)
        {
            if (values == null)
            {
                return Array.Empty<T>();
            }

            var copy = new T[values.Count];
            for (int i = 0; i < copy.Length; ++i)
            {
                copy[i] = values[i];
            }

            return copy;
        }
    }
}
