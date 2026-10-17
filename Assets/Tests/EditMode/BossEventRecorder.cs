// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.AI;

namespace Shinshi.Tests
{
    /// <summary> 기록한 사건의 번호가 무엇의 번호인가, 종류가 다르면 같은 숫자라도 다른 대상이다 </summary>
    public enum EBossRecordKey
    {
        Cast,
        Area,
        Projectile,
        Beam,
        Star,
        Zone,
        Barrier
    }

    /// <summary> 기록한 사건 한 건, 이름은 계약 메서드 이름 그대로다 </summary>
    public readonly struct BossEventRecord
    {
        public string         Name      { get; }
        public EBossRecordKey Key       { get; }
        public int            Id        { get; }
        public int            Index     { get; }
        public bool           Flag      { get; }
        public double         Time      { get; }
        public int            Phase     { get; }
        public string         PatternId { get; }

        public BossEventRecord(string name, EBossRecordKey key, int id, int index, bool flag, double time, int phase,
            string patternId)
        {
            Name      = name;
            Key       = key;
            Id        = id;
            Index     = index;
            Flag      = flag;
            Time      = time;
            Phase     = phase;
            PatternId = patternId;
        }
    }

    // 서버 몸체가 보내는 세 계약(기본, 무철, 하드 페이즈)을 모두 구현해 온 순서대로 적는다. 판정이나 복원은 하지 않는다.
    // 복원 기록의 Replay를 받을 수 있게 두 상태 계약으로 구현하고, 복원은 사건 기록과 섞지 않고 종류별 목록에만 담는다.
    // 하드 페이즈 사건과 전장 공격은 값에 패턴 ID가 없어, 사건이 온 순간 몸체가 시전 중이던 패턴을 patternSource로 함께 적는다.
    // 페이즈는 시험이 틱 전에 넣어 준 값이다. 전환한 틱에 온 정리 사건은 이전 페이즈로 적힌다
    /// <summary> 모의 전투에서 보스 사건을 순서대로 기록하는 시험용 수신처 </summary>
    public sealed class BossEventRecorder : IBossEventListener, IBossMucheolStateListener, IBossHardPhaseStateListener
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int NO_INDEX = -1;

        private readonly List<BossEventRecord> _records        = new List<BossEventRecord>();
        private readonly List<int>             _dedicatedCasts = new List<int>();
        private readonly Func<string>          _patternSource;

        /****************************************
        *              Properties
        ****************************************/

        public IReadOnlyList<BossEventRecord> Records => _records;

        /// <summary> 전용 사건으로 예고한 시전 번호, 시작한 순서이고 겹치지 않는다 </summary>
        public IReadOnlyList<int> DedicatedCasts => _dedicatedCasts;

        /// <summary> 다음 사건에 적을 페이즈 </summary>
        public int Phase { get; set; }

        // 복제 상태(BossState)가 서버 쪽 복원 기록을 이어 가는 것과 같은 연결이다: 전용 사건은 그대로 넘기고,
        // 기본 사건의 정상 종료와 중단은 시전 종료로, 장판 활성은 같은 번호의 이동 예고 종료로 넘긴다
        /// <summary> 받은 사건으로 이어 간 전용 공격 복원 기록 </summary>
        public BossPhaseEffectLedger PhaseLedger { get; } = new BossPhaseEffectLedger();

        public List<BossTelegraphStart>    Telegraphs    { get; } = new List<BossTelegraphStart>();
        public List<BossProjectileStart>   Projectiles   { get; } = new List<BossProjectileStart>();
        public List<BossAreaStart>         Areas         { get; } = new List<BossAreaStart>();
        public List<BossBarrierStart>      Barriers      { get; } = new List<BossBarrierStart>();
        public List<BossStrikeChainStart>  StrikeChains  { get; } = new List<BossStrikeChainStart>();
        public List<BossMultiDashStart>    MultiDashes   { get; } = new List<BossMultiDashStart>();
        public List<BossMultiDashReaim>    Reaims        { get; } = new List<BossMultiDashReaim>();
        public List<BossBattlefieldStart>  Battlefields  { get; } = new List<BossBattlefieldStart>();
        public List<BossChargeOutcome>     Charges       { get; } = new List<BossChargeOutcome>();
        public List<BossChargeGroggy>      Groggies      { get; } = new List<BossChargeGroggy>();
        public List<BossStarStageStart>    StarStages    { get; } = new List<BossStarStageStart>();
        public List<BossGridBeamStart>     GridBeams     { get; } = new List<BossGridBeamStart>();
        public List<BossChainFanStart>     ChainFans     { get; } = new List<BossChainFanStart>();
        public List<BossTrackingAreaStart> TrackingAreas { get; } = new List<BossTrackingAreaStart>();
        public List<BossBarrageStart>      Barrages      { get; } = new List<BossBarrageStart>();
        public List<BossBarrageShot>       BarrageShots  { get; } = new List<BossBarrageShot>();
        public List<BossCircleBurstStart>  CircleBursts  { get; } = new List<BossCircleBurstStart>();

        public List<BossStrikeChainSnapshot>  RestoredStrikeChains  { get; } = new List<BossStrikeChainSnapshot>();
        public List<BossMultiDashSnapshot>    RestoredMultiDashes   { get; } = new List<BossMultiDashSnapshot>();
        public List<BossBattlefieldSnapshot>  RestoredBattlefields  { get; } = new List<BossBattlefieldSnapshot>();
        public List<BossChargeOutcome>        RestoredCharges       { get; } = new List<BossChargeOutcome>();
        public List<BossChargeGroggy>         RestoredGroggies      { get; } = new List<BossChargeGroggy>();
        public List<BossStarStageSnapshot>    RestoredStarStages    { get; } = new List<BossStarStageSnapshot>();
        public List<BossGridBeamSnapshot>     RestoredGridBeams     { get; } = new List<BossGridBeamSnapshot>();
        public List<BossChainFanSnapshot>     RestoredChainFans     { get; } = new List<BossChainFanSnapshot>();
        public List<BossTrackingAreaSnapshot> RestoredTrackingAreas { get; } = new List<BossTrackingAreaSnapshot>();
        public List<BossBarrageSnapshot>      RestoredBarrages      { get; } = new List<BossBarrageSnapshot>();
        public List<BossCircleBurstSnapshot>  RestoredCircleBursts  { get; } = new List<BossCircleBurstSnapshot>();

        /// <summary> 받은 복원의 총 건수 </summary>
        public int RestoreCount => RestoredStrikeChains.Count + RestoredMultiDashes.Count + RestoredBattlefields.Count
            + RestoredCharges.Count + RestoredGroggies.Count + RestoredStarStages.Count + RestoredGridBeams.Count + RestoredChainFans.Count
            + RestoredTrackingAreas.Count + RestoredBarrages.Count + RestoredCircleBursts.Count;

        /****************************************
        *                Methods
        ****************************************/

        public BossEventRecorder(Func<string> patternSource = null)
        {
            _patternSource = patternSource;
        }

        public int Count(string name)
        {
            int count = 0;
            for (int i = 0; i < _records.Count; ++i)
            {
                if (_records[i].Name == name)
                {
                    ++count;
                }
            }

            return count;
        }

        public int Count(string name, EBossRecordKey key, int id)
        {
            int count = 0;
            for (int i = 0; i < _records.Count; ++i)
            {
                BossEventRecord record = _records[i];
                if ((record.Name == name) && (record.Key == key) && (record.Id == id))
                {
                    ++count;
                }
            }

            return count;
        }

        /// <summary> 그 번호에 온 사건 이름을 온 순서대로 모은다 </summary>
        public List<string> NamesOf(EBossRecordKey key, int id)
        {
            var names = new List<string>();
            for (int i = 0; i < _records.Count; ++i)
            {
                if ((_records[i].Key == key) && (_records[i].Id == id))
                {
                    names.Add(_records[i].Name);
                }
            }

            return names;
        }

        /// <summary> 그 번호에 온 그 이름의 사건을 온 순서대로 모은다 </summary>
        public List<BossEventRecord> Find(string name, EBossRecordKey key, int id)
        {
            var found = new List<BossEventRecord>();
            for (int i = 0; i < _records.Count; ++i)
            {
                BossEventRecord record = _records[i];
                if ((record.Name == name) && (record.Key == key) && (record.Id == id))
                {
                    found.Add(record);
                }
            }

            return found;
        }

        /// <summary> 그 페이즈에 온 그 이름의 사건이 있는가 </summary>
        public bool HasInPhase(string name, int phase)
        {
            for (int i = 0; i < _records.Count; ++i)
            {
                if ((_records[i].Name == name) && (_records[i].Phase == phase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary> 그 페이즈에 사건이 온 패턴 ID를 정렬해 모은다 </summary>
        public SortedSet<string> PatternsInPhase(int phase)
        {
            var patterns = new SortedSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < _records.Count; ++i)
            {
                if ((_records[i].Phase == phase) && (!string.IsNullOrEmpty(_records[i].PatternId)))
                {
                    patterns.Add(_records[i].PatternId);
                }
            }

            return patterns;
        }

        /// <summary> 그 이름의 사건이 온 패턴 ID를 정렬해 모은다 </summary>
        public SortedSet<string> PatternsOf(string name)
        {
            var patterns = new SortedSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < _records.Count; ++i)
            {
                if ((_records[i].Name == name) && (!string.IsNullOrEmpty(_records[i].PatternId)))
                {
                    patterns.Add(_records[i].PatternId);
                }
            }

            return patterns;
        }

        private void Add(string name, EBossRecordKey key, int id, int index = NO_INDEX, bool flag = false, double time = 0.0,
            string patternId = null)
        {
            if (patternId == null)
            {
                patternId = (_patternSource != null) ? _patternSource() : string.Empty;
            }

            _records.Add(new BossEventRecord(name, key, id, index, flag, time, Phase, patternId));
        }

        // 추적 장판은 한 시전이 장판마다 시작을 알려 같은 번호가 여러 번 온다
        private void AddDedicatedCast(int castId)
        {
            if (!_dedicatedCasts.Contains(castId))
            {
                _dedicatedCasts.Add(castId);
            }
        }

        /****************************************
        *                Basic
        ****************************************/

        public void OnTelegraphStarted(in BossTelegraphStart start)
        {
            PhaseLedger.OnCastStarted(start.CastId);
            Telegraphs.Add(start);
            Add(nameof(OnTelegraphStarted), EBossRecordKey.Cast, start.CastId, start.GroupId, false, start.StartServerTime,
                start.PatternId);
        }

        public void OnAimChanged(int castId, Vector3 direction, float length, double serverTime)
        {
            Add(nameof(OnAimChanged), EBossRecordKey.Cast, castId, NO_INDEX, false, serverTime);
        }

        public void OnDirectionLocked(int castId, Vector3 direction, float length)
        {
            Add(nameof(OnDirectionLocked), EBossRecordKey.Cast, castId);
        }

        public void OnStruck(int castId, double serverTime)
        {
            Add(nameof(OnStruck), EBossRecordKey.Cast, castId, NO_INDEX, false, serverTime);
        }

        public void OnDashStarted(int castId, float distance, float seconds, float speed, double startServerTime)
        {
            Add(nameof(OnDashStarted), EBossRecordKey.Cast, castId, NO_INDEX, false, startServerTime);
        }

        public void OnDashStopped(int castId, Vector3 position, bool stoppedByWall, double stopServerTime)
        {
            Add(nameof(OnDashStopped), EBossRecordKey.Cast, castId, NO_INDEX, stoppedByWall, stopServerTime);
        }

        public void OnFinished(int castId)
        {
            PhaseLedger.OnCastEnded(castId);
            Add(nameof(OnFinished), EBossRecordKey.Cast, castId);
        }

        public void OnCancelled(int castId)
        {
            PhaseLedger.OnCastEnded(castId);
            Add(nameof(OnCancelled), EBossRecordKey.Cast, castId);
        }

        public void OnProjectileLaunched(in BossProjectileStart start)
        {
            Projectiles.Add(start);
            Add(nameof(OnProjectileLaunched), EBossRecordKey.Projectile, start.ProjectileId, NO_INDEX, false,
                start.LaunchServerTime);
        }

        public void OnProjectileEnded(int projectileId, EBossProjectileFate fate, Vector3 position)
        {
            Add(nameof(OnProjectileEnded), EBossRecordKey.Projectile, projectileId, (int)fate);
        }

        public void OnAreaActivated(in BossAreaStart start)
        {
            PhaseLedger.OnAreaActivated(start.AreaId);
            Areas.Add(start);
            Add(nameof(OnAreaActivated), EBossRecordKey.Area, start.AreaId, NO_INDEX, false, start.ActivateServerTime);
        }

        public void OnAreaTicked(int areaId, int hitIndex, double roundServerTime)
        {
            Add(nameof(OnAreaTicked), EBossRecordKey.Area, areaId, hitIndex, false, roundServerTime);
        }

        public void OnAreaEnded(int areaId, EBossAreaFate fate)
        {
            Add(nameof(OnAreaEnded), EBossRecordKey.Area, areaId, (int)fate);
        }

        public void OnSweepStarted(in BossSweepStart start)
        {
            Add(nameof(OnSweepStarted), EBossRecordKey.Cast, start.CastId, NO_INDEX, false, start.StartServerTime);
        }

        public void OnSweepZoneScheduled(in BossTelegraphStart zone)
        {
            Add(nameof(OnSweepZoneScheduled), EBossRecordKey.Zone, zone.CastId, zone.GroupId, false, zone.StartServerTime,
                zone.PatternId);
        }

        public void OnSweepZoneBlasted(int zoneId, double blastServerTime)
        {
            Add(nameof(OnSweepZoneBlasted), EBossRecordKey.Zone, zoneId, NO_INDEX, false, blastServerTime);
        }

        public void OnSweepCancelled(int castId)
        {
            Add(nameof(OnSweepCancelled), EBossRecordKey.Cast, castId);
        }

        public void OnBeamTelegraphStarted(in BossBeamStart start)
        {
            Add(nameof(OnBeamTelegraphStarted), EBossRecordKey.Beam, start.BeamId, start.CastId, false, start.StartServerTime);
        }

        // 시전 번호는 Index에 적는다
        public void OnBeamCancelled(int castId, int beamId)
        {
            Add(nameof(OnBeamCancelled), EBossRecordKey.Beam, beamId, castId);
        }

        public void OnBeamActivated(int beamId, double activateServerTime)
        {
            Add(nameof(OnBeamActivated), EBossRecordKey.Beam, beamId, NO_INDEX, false, activateServerTime);
        }

        public void OnBeamTicked(int beamId, int hitIndex, double roundServerTime)
        {
            Add(nameof(OnBeamTicked), EBossRecordKey.Beam, beamId, hitIndex, false, roundServerTime);
        }

        public void OnBeamEnded(int beamId, EBossAreaFate fate)
        {
            Add(nameof(OnBeamEnded), EBossRecordKey.Beam, beamId, (int)fate);
        }

        public void OnStarTelegraphStarted(in BossStarStart start)
        {
            Add(nameof(OnStarTelegraphStarted), EBossRecordKey.Star, start.StarId, start.CastId, false, start.StartServerTime);
        }

        // 시전 번호는 Index에 적는다
        public void OnStarCancelled(int castId, int starId)
        {
            Add(nameof(OnStarCancelled), EBossRecordKey.Star, starId, castId);
        }

        public void OnStarActivated(int starId, double activateServerTime)
        {
            Add(nameof(OnStarActivated), EBossRecordKey.Star, starId, NO_INDEX, false, activateServerTime);
        }

        public void OnStarTicked(int starId, int hitIndex, double roundServerTime)
        {
            Add(nameof(OnStarTicked), EBossRecordKey.Star, starId, hitIndex, false, roundServerTime);
        }

        public void OnStarEnded(int starId, EBossAreaFate fate)
        {
            Add(nameof(OnStarEnded), EBossRecordKey.Star, starId, (int)fate);
        }

        public void OnBarrierStarted(in BossBarrierStart start)
        {
            Barriers.Add(start);
            Add(nameof(OnBarrierStarted), EBossRecordKey.Barrier, start.BarrierId, NO_INDEX, false, start.TelegraphServerTime);
        }

        public void OnBarrierActivated(int barrierId, double activateServerTime)
        {
            Add(nameof(OnBarrierActivated), EBossRecordKey.Barrier, barrierId, NO_INDEX, false, activateServerTime);
        }

        public void OnBarrierCompleted(int barrierId, double completeServerTime)
        {
            Add(nameof(OnBarrierCompleted), EBossRecordKey.Barrier, barrierId, NO_INDEX, false, completeServerTime);
        }

        public void OnBarrierCleared(int barrierId)
        {
            Add(nameof(OnBarrierCleared), EBossRecordKey.Barrier, barrierId);
        }

        /****************************************
        *               Mucheol
        ****************************************/

        // Flag는 순차 타격이면 참, 콤보면 거짓이다
        public void OnStrikeChainStarted(in BossStrikeChainStart start)
        {
            PhaseLedger.OnStrikeChainStarted(in start);
            StrikeChains.Add(start);
            AddDedicatedCast(start.CastId);
            Add(nameof(OnStrikeChainStarted), EBossRecordKey.Cast, start.CastId, start.StepCount,
                start.Kind == EBossStrikeChainKind.Sequence, start.StartServerTime, start.PatternId);
        }

        public void OnStrikeChainAimChanged(EBossStrikeChainKind kind, int castId, Vector3 direction, double serverTime)
        {
            PhaseLedger.OnStrikeChainAimChanged(kind, castId, direction, serverTime);
            Add(nameof(OnStrikeChainAimChanged), EBossRecordKey.Cast, castId, NO_INDEX, kind == EBossStrikeChainKind.Sequence,
                serverTime);
        }

        public void OnStrikeChainDirectionLocked(EBossStrikeChainKind kind, int castId, Vector3 direction)
        {
            PhaseLedger.OnStrikeChainDirectionLocked(kind, castId, direction);
            Add(nameof(OnStrikeChainDirectionLocked), EBossRecordKey.Cast, castId, NO_INDEX,
                kind == EBossStrikeChainKind.Sequence);
        }

        public void OnStrikeChainHit(EBossStrikeChainKind kind, int castId, int stepIndex, double serverTime)
        {
            PhaseLedger.OnStrikeChainHit(kind, castId, stepIndex, serverTime);
            Add(nameof(OnStrikeChainHit), EBossRecordKey.Cast, castId, stepIndex, kind == EBossStrikeChainKind.Sequence,
                serverTime);
        }

        public void OnStrikeChainCancelled(EBossStrikeChainKind kind, int castId)
        {
            PhaseLedger.OnStrikeChainCancelled(kind, castId);
            Add(nameof(OnStrikeChainCancelled), EBossRecordKey.Cast, castId, NO_INDEX, kind == EBossStrikeChainKind.Sequence);
        }

        public void OnMultiDashStarted(in BossMultiDashStart start)
        {
            PhaseLedger.OnMultiDashStarted(in start);
            MultiDashes.Add(start);
            AddDedicatedCast(start.CastId);
            Add(nameof(OnMultiDashStarted), EBossRecordKey.Cast, start.CastId, start.DashCount, false, start.StartServerTime,
                start.PatternId);
        }

        public void OnMultiDashAimChanged(int castId, int dashIndex, Vector3 direction, float length, double serverTime)
        {
            PhaseLedger.OnMultiDashAimChanged(castId, dashIndex, direction, length, serverTime);
            Add(nameof(OnMultiDashAimChanged), EBossRecordKey.Cast, castId, dashIndex, false, serverTime);
        }

        public void OnMultiDashDirectionLocked(int castId, int dashIndex, Vector3 direction, float length)
        {
            PhaseLedger.OnMultiDashDirectionLocked(castId, dashIndex, direction, length);
            Add(nameof(OnMultiDashDirectionLocked), EBossRecordKey.Cast, castId, dashIndex);
        }

        public void OnMultiDashReaimStarted(in BossMultiDashReaim reaim)
        {
            PhaseLedger.OnMultiDashReaimStarted(in reaim);
            Reaims.Add(reaim);
            Add(nameof(OnMultiDashReaimStarted), EBossRecordKey.Cast, reaim.CastId, reaim.DashIndex, false,
                reaim.StartServerTime);
        }

        public void OnMultiDashDashStarted(int castId, int dashIndex, float distance, float seconds, float speed,
            double startServerTime)
        {
            PhaseLedger.OnMultiDashDashStarted(castId, dashIndex, distance, seconds, speed, startServerTime);
            Add(nameof(OnMultiDashDashStarted), EBossRecordKey.Cast, castId, dashIndex, false, startServerTime);
        }

        public void OnMultiDashDashStopped(int castId, int dashIndex, Vector3 position, bool stoppedByWall, double stopServerTime)
        {
            PhaseLedger.OnMultiDashDashStopped(castId, dashIndex, position, stoppedByWall, stopServerTime);
            Add(nameof(OnMultiDashDashStopped), EBossRecordKey.Cast, castId, dashIndex, stoppedByWall, stopServerTime);
        }

        public void OnMultiDashTruncated(int castId, int plannedDashCount, int completedDashCount)
        {
            PhaseLedger.OnMultiDashTruncated(castId, plannedDashCount, completedDashCount);
            Add(nameof(OnMultiDashTruncated), EBossRecordKey.Cast, castId, completedDashCount);
        }

        public void OnMultiDashCancelled(int castId)
        {
            PhaseLedger.OnMultiDashCancelled(castId);
            Add(nameof(OnMultiDashCancelled), EBossRecordKey.Cast, castId);
        }

        public void OnBattlefieldStarted(in BossBattlefieldStart start)
        {
            PhaseLedger.OnBattlefieldStarted(in start);
            Battlefields.Add(start);
            AddDedicatedCast(start.CastId);
            Add(nameof(OnBattlefieldStarted), EBossRecordKey.Cast, start.CastId, NO_INDEX, false, start.StartServerTime);
        }

        public void OnBattlefieldHit(int castId, double serverTime)
        {
            PhaseLedger.OnBattlefieldHit(castId, serverTime);
            Add(nameof(OnBattlefieldHit), EBossRecordKey.Cast, castId, NO_INDEX, false, serverTime);
        }

        public void OnBattlefieldCancelled(int castId)
        {
            PhaseLedger.OnBattlefieldCancelled(castId);
            Add(nameof(OnBattlefieldCancelled), EBossRecordKey.Cast, castId);
        }

        // 이 시전의 예고는 기본 돌진 사건으로 오므로 전용 시전으로 세지 않는다
        public void OnChargeEnded(in BossChargeOutcome outcome)
        {
            PhaseLedger.OnChargeEnded(in outcome);
            Charges.Add(outcome);
            Add(nameof(OnChargeEnded), EBossRecordKey.Cast, outcome.CastId, outcome.StructureId, outcome.IsCollision,
                outcome.ServerTime);
        }

        // 그로기 사건의 시각은 대기면 충돌 시각, 시작이면 그로기 시작 시각을 적는다
        public void OnChargeGroggyPending(in BossChargeGroggy groggy)
        {
            PhaseLedger.OnChargeGroggyPending(in groggy);
            Groggies.Add(groggy);
            Add(nameof(OnChargeGroggyPending), EBossRecordKey.Cast, groggy.CastId, groggy.StructureId, false,
                groggy.CollisionServerTime);
        }

        public void OnChargeGroggyStarted(in BossChargeGroggy groggy)
        {
            PhaseLedger.OnChargeGroggyStarted(in groggy);
            Groggies.Add(groggy);
            Add(nameof(OnChargeGroggyStarted), EBossRecordKey.Cast, groggy.CastId, groggy.StructureId, true,
                groggy.GroggyStartServerTime);
        }

        public void OnChargeGroggyCancelled(int castId)
        {
            PhaseLedger.OnChargeGroggyCancelled(castId);
            Add(nameof(OnChargeGroggyCancelled), EBossRecordKey.Cast, castId);
        }

        /****************************************
        *          Wolbaek Hard Phase 2
        ****************************************/

        public void OnStarStageStarted(in BossStarStageStart start)
        {
            PhaseLedger.OnStarStageStarted(in start);
            StarStages.Add(start);
            AddDedicatedCast(start.CastId);
            Add(nameof(OnStarStageStarted), EBossRecordKey.Cast, start.CastId, start.StageCount, false,
                start.CastStartServerTime);
        }

        public void OnStarStageStruck(int castId, int stageIndex, float rotationDegrees, double scheduledServerTime)
        {
            PhaseLedger.OnStarStageStruck(castId, stageIndex, rotationDegrees, scheduledServerTime);
            Add(nameof(OnStarStageStruck), EBossRecordKey.Cast, castId, stageIndex, false, scheduledServerTime);
        }

        public void OnStarStageCancelled(int castId)
        {
            PhaseLedger.OnStarStageCancelled(castId);
            Add(nameof(OnStarStageCancelled), EBossRecordKey.Cast, castId);
        }

        public void OnGridBeamStarted(in BossGridBeamStart start)
        {
            PhaseLedger.OnGridBeamStarted(in start);
            GridBeams.Add(start);
            AddDedicatedCast(start.CastId);
            Add(nameof(OnGridBeamStarted), EBossRecordKey.Cast, start.CastId, start.LineCount, false,
                start.CastStartServerTime);
        }

        public void OnGridBeamLineTelegraphed(int castId, int lineId, bool isHorizontal, int lineIndex,
            double telegraphServerTime, double strikeServerTime)
        {
            PhaseLedger.OnGridBeamLineTelegraphed(castId, lineId, isHorizontal, lineIndex, telegraphServerTime, strikeServerTime);
            Add(nameof(OnGridBeamLineTelegraphed), EBossRecordKey.Cast, castId, lineIndex, isHorizontal, telegraphServerTime);
        }

        public void OnGridBeamLineStruck(int castId, int lineId, bool isHorizontal, int lineIndex, double scheduledServerTime)
        {
            PhaseLedger.OnGridBeamLineStruck(castId, lineId, isHorizontal, lineIndex, scheduledServerTime);
            Add(nameof(OnGridBeamLineStruck), EBossRecordKey.Cast, castId, lineIndex, isHorizontal, scheduledServerTime);
        }

        public void OnGridBeamCancelled(int castId)
        {
            PhaseLedger.OnGridBeamCancelled(castId);
            Add(nameof(OnGridBeamCancelled), EBossRecordKey.Cast, castId);
        }

        public void OnChainFanStarted(in BossChainFanStart start)
        {
            PhaseLedger.OnChainFanStarted(in start);
            ChainFans.Add(start);
            AddDedicatedCast(start.CastId);
            Add(nameof(OnChainFanStarted), EBossRecordKey.Cast, start.CastId, start.ZoneCount, false,
                start.CastStartServerTime);
        }

        public void OnChainFanBlasted(int castId, int zoneIndex, double scheduledServerTime)
        {
            PhaseLedger.OnChainFanBlasted(castId, zoneIndex, scheduledServerTime);
            Add(nameof(OnChainFanBlasted), EBossRecordKey.Cast, castId, zoneIndex, false, scheduledServerTime);
        }

        public void OnChainFanDangerEnded(int castId, double scheduledServerTime)
        {
            PhaseLedger.OnChainFanDangerEnded(castId, scheduledServerTime);
            Add(nameof(OnChainFanDangerEnded), EBossRecordKey.Cast, castId, NO_INDEX, false, scheduledServerTime);
        }

        public void OnChainFanCancelled(int castId)
        {
            PhaseLedger.OnChainFanCancelled(castId);
            Add(nameof(OnChainFanCancelled), EBossRecordKey.Cast, castId);
        }

        // 장판 번호가 키이고 Index에 시전 번호를 적는다
        public void OnTrackingAreaStarted(in BossTrackingAreaStart start)
        {
            PhaseLedger.OnTrackingAreaStarted(in start);
            TrackingAreas.Add(start);
            AddDedicatedCast(start.CastId);
            Add(nameof(OnTrackingAreaStarted), EBossRecordKey.Area, start.AreaId, start.CastId, false, start.StartServerTime);
        }

        public void OnTrackingAreaMoved(int areaId, Vector3 center, double serverTime)
        {
            PhaseLedger.OnTrackingAreaMoved(areaId, center, serverTime);
            Add(nameof(OnTrackingAreaMoved), EBossRecordKey.Area, areaId, NO_INDEX, false, serverTime);
        }

        public void OnTrackingAreaFixed(int areaId, Vector3 center, double fixedServerTime)
        {
            PhaseLedger.OnTrackingAreaFixed(areaId, center, fixedServerTime);
            Add(nameof(OnTrackingAreaFixed), EBossRecordKey.Area, areaId, NO_INDEX, false, fixedServerTime);
        }

        // 시전 번호는 Index에 적는다
        public void OnTrackingAreaCancelled(int castId, int areaId)
        {
            PhaseLedger.OnTrackingAreaCancelled(castId, areaId);
            Add(nameof(OnTrackingAreaCancelled), EBossRecordKey.Area, areaId, castId);
        }

        public void OnBarrageStarted(in BossBarrageStart start)
        {
            PhaseLedger.OnBarrageStarted(in start);
            Barrages.Add(start);
            AddDedicatedCast(start.CastId);
            Add(nameof(OnBarrageStarted), EBossRecordKey.Cast, start.CastId, start.ShotCount, false,
                start.CastStartServerTime);
        }

        public void OnBarrageShotTelegraphed(in BossBarrageShot shot)
        {
            PhaseLedger.OnBarrageShotTelegraphed(in shot);
            BarrageShots.Add(shot);
            Add(nameof(OnBarrageShotTelegraphed), EBossRecordKey.Cast, shot.CastId, shot.ShotIndex, false,
                shot.ScheduledServerTime);
        }

        // 발사된 발은 투사체 번호로도 한 번 더 적어 기본 투사체 사건과 맞춰 본다
        public void OnBarrageShotFired(int castId, int shotIndex, int projectileId, bool isLaunched, double scheduledServerTime)
        {
            PhaseLedger.OnBarrageShotFired(castId, shotIndex, projectileId, isLaunched, scheduledServerTime);
            Add(nameof(OnBarrageShotFired), EBossRecordKey.Cast, castId, shotIndex, isLaunched, scheduledServerTime);
            if (isLaunched)
            {
                Add(nameof(OnBarrageShotFired), EBossRecordKey.Projectile, projectileId, shotIndex, true, scheduledServerTime);
            }
        }

        public void OnBarrageCancelled(int castId)
        {
            PhaseLedger.OnBarrageCancelled(castId);
            Add(nameof(OnBarrageCancelled), EBossRecordKey.Cast, castId);
        }

        public void OnCircleBurstStarted(in BossCircleBurstStart start)
        {
            PhaseLedger.OnCircleBurstStarted(in start);
            CircleBursts.Add(start);
            AddDedicatedCast(start.CastId);
            Add(nameof(OnCircleBurstStarted), EBossRecordKey.Cast, start.CastId, start.CircleCount, false,
                start.CastStartServerTime);
        }

        public void OnCircleBurstStruck(int castId, double scheduledServerTime)
        {
            PhaseLedger.OnCircleBurstStruck(castId, scheduledServerTime);
            Add(nameof(OnCircleBurstStruck), EBossRecordKey.Cast, castId, NO_INDEX, false, scheduledServerTime);
        }

        public void OnCircleBurstCancelled(int castId)
        {
            PhaseLedger.OnCircleBurstCancelled(castId);
            Add(nameof(OnCircleBurstCancelled), EBossRecordKey.Cast, castId);
        }

        /****************************************
        *               Restores
        ****************************************/

        public void OnStrikeChainRestored(in BossStrikeChainSnapshot snapshot)
        {
            RestoredStrikeChains.Add(snapshot);
        }

        public void OnMultiDashRestored(in BossMultiDashSnapshot snapshot)
        {
            RestoredMultiDashes.Add(snapshot);
        }

        public void OnBattlefieldRestored(in BossBattlefieldSnapshot snapshot)
        {
            RestoredBattlefields.Add(snapshot);
        }

        public void OnChargeRestored(in BossChargeOutcome outcome)
        {
            RestoredCharges.Add(outcome);
        }

        public void OnChargeGroggyRestored(in BossChargeGroggy groggy)
        {
            RestoredGroggies.Add(groggy);
        }

        public void OnStarStageRestored(in BossStarStageSnapshot snapshot)
        {
            RestoredStarStages.Add(snapshot);
        }

        public void OnGridBeamRestored(in BossGridBeamSnapshot snapshot)
        {
            RestoredGridBeams.Add(snapshot);
        }

        public void OnChainFanRestored(in BossChainFanSnapshot snapshot)
        {
            RestoredChainFans.Add(snapshot);
        }

        public void OnTrackingAreaRestored(in BossTrackingAreaSnapshot snapshot)
        {
            RestoredTrackingAreas.Add(snapshot);
        }

        public void OnBarrageRestored(in BossBarrageSnapshot snapshot)
        {
            RestoredBarrages.Add(snapshot);
        }

        public void OnCircleBurstRestored(in BossCircleBurstSnapshot snapshot)
        {
            RestoredCircleBursts.Add(snapshot);
        }
    }
}
