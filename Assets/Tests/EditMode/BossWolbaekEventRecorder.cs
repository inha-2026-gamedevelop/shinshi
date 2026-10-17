// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    // 서버 몸체가 보내는 기본 사건과 월백 기믹 사건을 온 순서대로 이름으로 적는다. 기본 사건은 원형 예고 시작만 적는다(워프 순서 확인용).
    // 월백 기믹 사건은 복제 상태(BossState)처럼 기록(BossWolbaekGimmickLedger)에도 그대로 넘겨 서버 쪽 복원 기록을 함께 만든다.
    // 색 결계 성공이 온 순간의 몸체 페이즈 세대를 generationSource로 함께 적는다
    /// <summary> 월백 기믹 사건을 순서대로 기록하는 시험용 수신처 </summary>
    public sealed class BossWolbaekEventRecorder : IBossEventListener, IBossWolbaekGimmickStateListener
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly List<string> _names = new List<string>();
        private readonly Func<ushort> _generationSource;

        /****************************************
        *              Properties
        ****************************************/

        public IReadOnlyList<string> Names => _names;

        /// <summary> 받은 사건으로 이어 가는 복원 기록, 복제 상태의 서버 기록과 같은 연결이다 </summary>
        public BossWolbaekGimmickLedger Ledger { get; } = new BossWolbaekGimmickLedger();

        public ushort WardSuccessGeneration { get; private set; }

        /// <summary> 색 결계 성공이 온 순간 몸체가 답한 페이즈 세대 </summary>
        public ushort GenerationAtWardSuccessCallback { get; private set; }

        public int WardSuccessCount { get; private set; }

        public Vector3 LastDeparture { get; private set; }

        public int LastDepartureCastId { get; private set; }

        public byte LastCoreMask { get; private set; }

        // 복원은 사건 기록과 섞지 않고 종류별 목록에만 담는다
        public List<BossWolbaekTalismanRestore> RestoredTalismans { get; } = new List<BossWolbaekTalismanRestore>();
        public List<BossWolbaekWardRestore>     RestoredWards     { get; } = new List<BossWolbaekWardRestore>();
        public List<BossWolbaekSealRestore>     RestoredSeals     { get; } = new List<BossWolbaekSealRestore>();
        public List<BossWolbaekFinalRestore>    RestoredFinals    { get; } = new List<BossWolbaekFinalRestore>();
        public List<int>                        RestoredWarps     { get; } = new List<int>();

        /****************************************
        *                Methods
        ****************************************/

        public BossWolbaekEventRecorder(Func<ushort> generationSource = null)
        {
            _generationSource = generationSource;
        }

        public int Count(string name)
        {
            int count = 0;
            for (int i = 0; i < _names.Count; ++i)
            {
                if (_names[i] == name)
                {
                    ++count;
                }
            }

            return count;
        }

        public int IndexOf(string name)
        {
            return _names.IndexOf(name);
        }

        private void Add(string name)
        {
            _names.Add(name);
        }

        /****************************************
        *               Base Events
        ****************************************/

        public void OnTelegraphStarted(in BossTelegraphStart start)
        {
            Add(nameof(OnTelegraphStarted));
        }

        public void OnAimChanged(int castId, Vector3 direction, float length, double serverTime)
        {
        }

        public void OnDirectionLocked(int castId, Vector3 direction, float length)
        {
        }

        public void OnStruck(int castId, double serverTime)
        {
        }

        public void OnDashStarted(int castId, float distance, float seconds, float speed, double startServerTime)
        {
        }

        public void OnDashStopped(int castId, Vector3 position, bool stoppedByWall, double stopServerTime)
        {
        }

        public void OnFinished(int castId)
        {
            Ledger.OnCastEnded(castId);
        }

        public void OnCancelled(int castId)
        {
            Ledger.OnCastEnded(castId);
        }

        public void OnProjectileLaunched(in BossProjectileStart start)
        {
        }

        public void OnProjectileEnded(int projectileId, EBossProjectileFate fate, Vector3 position)
        {
        }

        public void OnAreaActivated(in BossAreaStart start)
        {
        }

        public void OnAreaTicked(int areaId, int hitIndex, double roundServerTime)
        {
        }

        public void OnAreaEnded(int areaId, EBossAreaFate fate)
        {
        }

        public void OnSweepStarted(in BossSweepStart start)
        {
        }

        public void OnSweepZoneScheduled(in BossTelegraphStart zone)
        {
        }

        public void OnSweepZoneBlasted(int zoneId, double blastServerTime)
        {
        }

        public void OnSweepCancelled(int castId)
        {
        }

        public void OnBeamTelegraphStarted(in BossBeamStart start)
        {
        }

        public void OnBeamCancelled(int castId, int beamId)
        {
        }

        public void OnBeamActivated(int beamId, double activateServerTime)
        {
        }

        public void OnBeamTicked(int beamId, int hitIndex, double roundServerTime)
        {
        }

        public void OnBeamEnded(int beamId, EBossAreaFate fate)
        {
        }

        public void OnStarTelegraphStarted(in BossStarStart start)
        {
        }

        public void OnStarCancelled(int castId, int starId)
        {
        }

        public void OnStarActivated(int starId, double activateServerTime)
        {
        }

        public void OnStarTicked(int starId, int hitIndex, double roundServerTime)
        {
        }

        public void OnStarEnded(int starId, EBossAreaFate fate)
        {
        }

        public void OnBarrierStarted(in BossBarrierStart start)
        {
        }

        public void OnBarrierActivated(int barrierId, double activateServerTime)
        {
        }

        public void OnBarrierCompleted(int barrierId, double completeServerTime)
        {
        }

        public void OnBarrierCleared(int barrierId)
        {
        }

        /****************************************
        *            75% Talisman
        ****************************************/

        public void OnTalismanStarted(in WolbaekTalismanStart start)
        {
            Add(nameof(OnTalismanStarted));
            Ledger.OnTalismanStarted(in start);
        }

        public void OnTalismanHoldStarted(int castId, double serverTime)
        {
            Add(nameof(OnTalismanHoldStarted));
            Ledger.OnTalismanHoldStarted(castId, serverTime);
        }

        public void OnTalismanHoldReset(int castId, double serverTime, EWolbaekHoldReset reason, int combatId)
        {
            Add(nameof(OnTalismanHoldReset));
            Ledger.OnTalismanHoldReset(castId, serverTime, reason, combatId);
        }

        public void OnTalismanAttackTelegraphed(int castId, in WolbaekTalismanAttack attack)
        {
            Add(nameof(OnTalismanAttackTelegraphed));
            Ledger.OnTalismanAttackTelegraphed(castId, in attack);
        }

        public void OnTalismanAttackStruck(int castId, int attackId, double strikeServerTime)
        {
            Add(nameof(OnTalismanAttackStruck));
            Ledger.OnTalismanAttackStruck(castId, attackId, strikeServerTime);
        }

        public void OnTalismanAttackCancelled(int castId, int attackId)
        {
            Add(nameof(OnTalismanAttackCancelled));
            Ledger.OnTalismanAttackCancelled(castId, attackId);
        }

        public void OnTalismanSucceeded(int castId, double serverTime, double counterEndServerTime)
        {
            Add(nameof(OnTalismanSucceeded));
            Ledger.OnTalismanSucceeded(castId, serverTime, counterEndServerTime);
        }

        public void OnTalismanCounterEnded(int castId)
        {
            Add(nameof(OnTalismanCounterEnded));
            Ledger.OnTalismanCounterEnded(castId);
        }

        public void OnTalismanCleared(int castId)
        {
            Add(nameof(OnTalismanCleared));
            Ledger.OnTalismanCleared(castId);
        }

        /****************************************
        *            50% Color Ward
        ****************************************/

        public void OnWardStarted(in WolbaekWardStart start)
        {
            Add(nameof(OnWardStarted));
            Ledger.OnWardStarted(in start);
        }

        public void OnGatherHoldStarted(int castId, double serverTime)
        {
            Add(nameof(OnGatherHoldStarted));
            Ledger.OnGatherHoldStarted(castId, serverTime);
        }

        public void OnGatherHoldReset(int castId)
        {
            Add(nameof(OnGatherHoldReset));
            Ledger.OnGatherHoldReset(castId);
        }

        public void OnGatherCompleted(int castId)
        {
            Add(nameof(OnGatherCompleted));
            Ledger.OnGatherCompleted(castId);
        }

        public void OnStageStarted(int castId, in WolbaekWardStage stage)
        {
            Add(nameof(OnStageStarted));
            Ledger.OnStageStarted(castId, in stage);
        }

        public void OnStrikeTelegraphed(int castId, in WolbaekWardStrike strike)
        {
            Add(nameof(OnStrikeTelegraphed));
            Ledger.OnStrikeTelegraphed(castId, in strike);
        }

        public void OnStrikeResolved(int castId, int stageIndex, int strikeIndex, double strikeServerTime)
        {
            Add(nameof(OnStrikeResolved));
            Ledger.OnStrikeResolved(castId, stageIndex, strikeIndex, strikeServerTime);
        }

        public void OnLinkDamaged(int castId, double serverTime)
        {
            Add(nameof(OnLinkDamaged));
            Ledger.OnLinkDamaged(castId, serverTime);
        }

        public void OnGraceStarted(int castId, int combatId, double graceEndServerTime)
        {
            Add(nameof(OnGraceStarted));
            Ledger.OnGraceStarted(castId, combatId, graceEndServerTime);
        }

        public void OnGraceEnded(int castId, int combatId)
        {
            Add(nameof(OnGraceEnded));
            Ledger.OnGraceEnded(castId, combatId);
        }

        public void OnReviveWindowOpened(int castId)
        {
            Add(nameof(OnReviveWindowOpened));
            Ledger.OnReviveWindowOpened(castId);
        }

        public void OnWardSucceeded(int castId, ushort phaseGenerationAtSuccess)
        {
            Add(nameof(OnWardSucceeded));
            ++WardSuccessCount;
            WardSuccessGeneration           = phaseGenerationAtSuccess;
            GenerationAtWardSuccessCallback = (_generationSource != null) ? _generationSource() : (ushort)0;
            Ledger.OnWardSucceeded(castId, phaseGenerationAtSuccess);
        }

        public void OnWardCleared(int castId)
        {
            Add(nameof(OnWardCleared));
            Ledger.OnWardCleared(castId);
        }

        /****************************************
        *            25% Foxfire Seal
        ****************************************/

        public void OnSealStarted(in WolbaekSealStart start)
        {
            Add(nameof(OnSealStarted));
            Ledger.OnSealStarted(in start);
        }

        public void OnSealTurnStarted(int castId, in WolbaekSealTurn turn)
        {
            Add(nameof(OnSealTurnStarted));
            Ledger.OnSealTurnStarted(castId, in turn);
        }

        public void OnSealTrackingStarted(int castId, int turnIndex, double serverTime)
        {
            Add(nameof(OnSealTrackingStarted));
            Ledger.OnSealTrackingStarted(castId, turnIndex, serverTime);
        }

        public void OnSealFoxfireDetached(int castId, int turnIndex, int foxfireIndex, double serverTime, Vector3 position)
        {
            Add(nameof(OnSealFoxfireDetached));
            Ledger.OnSealFoxfireDetached(castId, turnIndex, foxfireIndex, serverTime, position);
        }

        public void OnSealSweepTelegraphed(int castId, in WolbaekSealSweep sweep)
        {
            Add(nameof(OnSealSweepTelegraphed));
            Ledger.OnSealSweepTelegraphed(castId, in sweep);
        }

        public void OnSealSweepResolved(int castId, int turnIndex, int sweepIndex, double serverTime)
        {
            Add(nameof(OnSealSweepResolved));
            Ledger.OnSealSweepResolved(castId, turnIndex, sweepIndex, serverTime);
        }

        public void OnSealFoxfiresLocked(int castId, int turnIndex, double serverTime, Vector3[] centers)
        {
            Add(nameof(OnSealFoxfiresLocked));
            Ledger.OnSealFoxfiresLocked(castId, turnIndex, serverTime, centers);
        }

        public void OnSealTurnResolved(int castId, int turnIndex, double serverTime, int successCount, byte destroyedCoreMask)
        {
            Add(nameof(OnSealTurnResolved));
            LastCoreMask = destroyedCoreMask;
            Ledger.OnSealTurnResolved(castId, turnIndex, serverTime, successCount, destroyedCoreMask);
        }

        public void OnSealSucceeded(int castId, double serverTime, double counterEndServerTime)
        {
            Add(nameof(OnSealSucceeded));
            Ledger.OnSealSucceeded(castId, serverTime, counterEndServerTime);
        }

        public void OnSealCounterEnded(int castId)
        {
            Add(nameof(OnSealCounterEnded));
            Ledger.OnSealCounterEnded(castId);
        }

        public void OnSealCleared(int castId)
        {
            Add(nameof(OnSealCleared));
            Ledger.OnSealCleared(castId);
        }

        /****************************************
        *          Hard 0% Final Area
        ****************************************/

        public void OnFinalStarted(in WolbaekFinalStart start)
        {
            Add(nameof(OnFinalStarted));
            Ledger.OnFinalStarted(in start);
        }

        public void OnFinalOuterFloorRemoved(int castId, double serverTime)
        {
            Add(nameof(OnFinalOuterFloorRemoved));
            Ledger.OnFinalOuterFloorRemoved(castId, serverTime);
        }

        public void OnFinalYellowsSpawned(int castId, WolbaekFinalYellow[] yellows)
        {
            Add(nameof(OnFinalYellowsSpawned));
            Ledger.OnFinalYellowsSpawned(castId, yellows);
        }

        public void OnFinalYellowDestroyed(int castId, int yellowIndex, double serverTime)
        {
            Add(nameof(OnFinalYellowDestroyed));
            Ledger.OnFinalYellowDestroyed(castId, yellowIndex, serverTime);
        }

        public void OnFinalBundleStarted(int castId, in WolbaekFinalBundle bundle)
        {
            Add(nameof(OnFinalBundleStarted));
            Ledger.OnFinalBundleStarted(castId, in bundle);
        }

        public void OnFinalRedDetached(int castId, int bundleIndex, int redIndex, double serverTime, Vector3 position)
        {
            Add(nameof(OnFinalRedDetached));
            Ledger.OnFinalRedDetached(castId, bundleIndex, redIndex, serverTime, position);
        }

        public void OnFinalRedsLocked(int castId, int bundleIndex, double serverTime, Vector3[] centers)
        {
            Add(nameof(OnFinalRedsLocked));
            Ledger.OnFinalRedsLocked(castId, bundleIndex, serverTime, centers);
        }

        public void OnFinalBundleResolved(int castId, int bundleIndex, double serverTime)
        {
            Add(nameof(OnFinalBundleResolved));
            Ledger.OnFinalBundleResolved(castId, bundleIndex, serverTime);
        }

        public void OnFinalSucceeded(int castId, double serverTime)
        {
            Add(nameof(OnFinalSucceeded));
            Ledger.OnFinalSucceeded(castId, serverTime);
        }

        public void OnFinalCleared(int castId)
        {
            Add(nameof(OnFinalCleared));
            Ledger.OnFinalCleared(castId);
        }

        /****************************************
        *               P07 Warp
        ****************************************/

        public void OnWarpDeparted(int castId, Vector3 departure, double serverTime)
        {
            Add(nameof(OnWarpDeparted));
            LastDeparture       = departure;
            LastDepartureCastId = castId;
            Ledger.OnWarpDeparted(castId, departure, serverTime);
        }

        /****************************************
        *                Restore
        ****************************************/

        public void OnTalismanRestored(in BossWolbaekTalismanRestore restore)
        {
            RestoredTalismans.Add(restore);
        }

        public void OnWardRestored(in BossWolbaekWardRestore restore)
        {
            RestoredWards.Add(restore);
        }

        public void OnSealRestored(in BossWolbaekSealRestore restore)
        {
            RestoredSeals.Add(restore);
        }

        public void OnFinalRestored(in BossWolbaekFinalRestore restore)
        {
            RestoredFinals.Add(restore);
        }

        public void OnWarpRestored(int castId, Vector3 departure, double serverTime)
        {
            RestoredWarps.Add(castId);
        }
    }
}
