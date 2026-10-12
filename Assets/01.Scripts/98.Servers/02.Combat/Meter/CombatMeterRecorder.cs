// System
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;
using Shinshi.Network;

namespace Shinshi.Combat
{
    /// <summary> 보스 출정 하나의 서버 전투 기록, 모든 변경과 전송은 Unity 메인 스레드에서만 실행한다 </summary>
    public sealed class CombatMeterRecorder : ICombatMeterSink
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    NONE_INDEX                   = -1;
        private const byte   FIRST_PHASE                  = 1;
        private const int    MAX_DAMAGE_TAKEN_SOURCES      = 128;
        private const int    INITIAL_COMBAT_ID_CAPACITY    = Constants.Combat.MAX_COMBAT_TARGETS;
        private const int    MAX_SUMMON_CACHE_ENTRIES       = Constants.Combat.MAX_COMBAT_TARGETS;
        private const int    BUFFER_GROWTH_FACTOR          = 2;
        private const int    LOG_INITIAL_CAPACITY          = 1024;
        private const string SUMMARY_EVENT                = "combat.meter.closed";
        private const string DEATH_EVENT                  = "combat.meter.death";
        private const string UNKNOWN_SOURCE_WARNING       = "[CombatMeter] 등록되지 않은 피해 출처를 기타 슬롯에 기록했다";
        private const string SOURCE_CAPACITY_WARNING      = "[CombatMeter] 받은 피해 출처 상한을 넘어 상세 출처 기록을 거절했다";
        private const string SUMMON_CACHE_WARNING          = "[CombatMeter] 소환물 귀속 캐시 상한에 닿았다, 활성 소환물은 주인 번호로 계속 기록한다";
        private const string PARTICIPANT_CAPACITY_WARNING  = "[CombatMeter] 참가자 슬롯 상한이나 중복 번호로 등록을 거절했다";
        private const string PHASE_CAPACITY_WARNING        = "[CombatMeter] 단계 상한이나 역방향 전환으로 구간 등록을 거절했다";

        private struct SlotRecord
        {
            public float  Damage;
            public ushort Hits;
            public float  MaxHit;
        }

        private struct SourceRecord
        {
            public string SourceId;
            public float  Amount;
        }

        private struct ReceivedHit
        {
            public string SourceId;
            public float  Amount;
            public double ServerTime;
        }

        private struct ParticipantRecord
        {
            public bool                          Registered;
            public bool                          Closed;
            public int                           ParticipantId;
            public string                        ApiParticipantId;
            public int                           CombatId;
            public string                        DisplayName;
            public EPlayerClass                  PlayerClass;
            public CharacterStats                Stats;
            public EParticipantLifeState          LifeState;
            public bool                          ActiveSuspended;
            public double                        AliveSince;
            public double                        ActiveSeconds;
            public float                         Damage;
            public float                         BossDamage;
            public float                         EnemyDamage;
            public float                         DamageTaken;
            public ushort                        GuardedHits;
            public byte                          Deaths;
            public byte                          Revives;
            public byte                          PotionsUsed;
            public float                         LifeStealHealed;
            public SlotRecord[]                  Slots;
            public float[]                       PhaseDamage;
            public SourceRecord[]                Sources;
            public int                           SourceCount;
            public ReceivedHit[]                 RecentHits;
            public int                           RecentCount;
            public int                           RecentNext;
            public CombatMeterDeathEntry[]        LastDeathRecap;
            public int                           LastDeathCount;
            public CombatMeterSlotStat[]          SendSlots;
            public CombatMeterFinalSelfMessage    FinalSelf;
        }

        private struct AttributionRecord
        {
            public int  CombatId;
            public int  ParticipantIndex;
            public bool IsSummon;
        }

        private struct PhaseRecord
        {
            public byte   Phase;
            public double StartTime;
            public double EndTime;
            public float  Damage;
            public double WaitingSeconds;
            public double WaitingSince;
            public bool   IsWaiting;
        }

        private readonly ICombatTargetQuery                         _targets;
        private readonly Func<int, NetworkConnectionToClient>       _connectionLookup;
        private readonly Dictionary<string, int>                     _slotBySource;
        private readonly Dictionary<string, EPlayerClass>            _classBySource;
        private readonly ParticipantRecord[]                        _participants;
        private readonly Dictionary<int, int>                        _bindingByCombatId;
        private readonly AttributionRecord[]                        _summonAttributions;
        private readonly PhaseRecord[]                              _phases;
        private readonly CombatMeterPartyEntry[][]                   _partyBuffers;
        private readonly int                                        _sessionEpoch;
        private readonly string                                     _sessionId;
        private readonly string                                     _eventId;

        private AttributionRecord[]      _attributions;
        private int                      _participantCount       = 0;
        private int                      _attributionCount       = 0;
        private int                      _summonAttributionCount = 0;
        private int                      _phaseCount             = 0;
        private double                   _startTime              = 0d;
        private double                   _lastTime               = 0d;
        private double                   _endTime                = 0d;
        private double                   _nextSendTime           = 0d;
        private bool                     _started                = false;
        private bool                     _closed                 = false;
        private bool                     _faulted                = false;
        private bool                     _finalReady             = false;
        private bool                     _unknownSource          = false;
        private bool                     _sourceOverflow         = false;
        private bool                     _summonOverflow         = false;
        private bool                     _participantError       = false;
        private bool                     _phaseError             = false;
        private bool                     _unknownLogged           = false;
        private bool                     _sourceLogged            = false;
        private bool                     _summonLogged            = false;
        private bool                     _participantLogged      = false;
        private bool                     _phaseLogged             = false;
        private CombatMeterResultMessage _result;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsStarted => _started;
        public bool IsClosed  => _closed;

        /****************************************
        *                Methods
        ****************************************/

        public CombatMeterRecorder(ICombatTargetQuery targets, SkillCatalogSO catalog,
            Func<int, NetworkConnectionToClient> connectionLookup, int sessionEpoch, string sessionId)
        {
            _targets            = targets ?? throw new ArgumentNullException(nameof(targets));
            _connectionLookup   = connectionLookup;
            _sessionEpoch       = sessionEpoch;
            _sessionId          = sessionId;
            _eventId            = sessionEpoch.ToString(CultureInfo.InvariantCulture);
            _participants       = new ParticipantRecord[Constants.CombatMeter.MAX_PARTICIPANTS];
            _attributions       = new AttributionRecord[INITIAL_COMBAT_ID_CAPACITY];
            _bindingByCombatId   = new Dictionary<int, int>(INITIAL_COMBAT_ID_CAPACITY);
            _summonAttributions = new AttributionRecord[MAX_SUMMON_CACHE_ENTRIES];
            _phases             = new PhaseRecord[Constants.CombatMeter.MAX_PHASES];
            _partyBuffers       = new CombatMeterPartyEntry[Constants.CombatMeter.MAX_PARTICIPANTS + 1][];
            _slotBySource       = BuildSourceSlots(catalog);
            _classBySource      = BuildSourceClasses(catalog);

            for (int i = 0; i < _partyBuffers.Length; ++i)
            {
                _partyBuffers[i] = new CombatMeterPartyEntry[i];
            }
            for (int i = 0; i < _participants.Length; ++i)
            {
                ref ParticipantRecord participant = ref _participants[i];
                participant.Slots          = new SlotRecord[Constants.CombatMeter.SLOT_COUNT];
                participant.PhaseDamage    = new float[Constants.CombatMeter.MAX_PHASES];
                participant.Sources        = new SourceRecord[MAX_DAMAGE_TAKEN_SOURCES];
                participant.RecentHits     = new ReceivedHit[Constants.CombatMeter.DEATH_RECAP_MAX_ENTRIES];
                participant.LastDeathRecap = new CombatMeterDeathEntry[Constants.CombatMeter.DEATH_RECAP_MAX_ENTRIES];
                participant.SendSlots      = new CombatMeterSlotStat[Constants.CombatMeter.SLOT_COUNT];
            }
        }

        public bool RegisterParticipant(int participantId, int combatId, string displayName, EPlayerClass playerClass,
            in CharacterStats stats, int partySlot = NONE_INDEX, string participantKey = null)
        {
            if ((_faulted) || (_closed) || (combatId == CombatIds.NONE))
            {
                return false;
            }

            int existing = FindParticipant(participantId);
            if (existing != NONE_INDEX)
            {
                return RegisterCombatId(participantId, combatId);
            }

            int index = (partySlot == NONE_INDEX) ? FindEmptySlot() : partySlot;
            if ((index < 0) || (index >= _participants.Length) || (_participants[index].Registered)
                || (_participantCount >= Constants.CombatMeter.MAX_PARTICIPANTS)
                || (FindAttribution(combatId) != NONE_INDEX))
            {
                _participantError = true;
                return false;
            }
            if (!AddAttribution(combatId, index, false))
            {
                return false;
            }

            ref ParticipantRecord participant = ref _participants[index];
            participant.Registered    = true;
            participant.ParticipantId = participantId;
            participant.ApiParticipantId = SessionContractIds.TryGetApiParticipantId(participantKey, out string apiId)
                ? apiId
                : null;
            participant.CombatId      = combatId;
            participant.DisplayName   = displayName ?? string.Empty;
            participant.PlayerClass   = playerClass;
            participant.Stats         = stats;
            participant.LifeState     = EParticipantLifeState.Alive;
            participant.AliveSince    = _lastTime;
            ++_participantCount;
            return true;
        }

        /// <summary> 본체 재생성은 새 번호를 더하고 이전 번호는 이탈 정산까지 보존한다 </summary>
        public bool RegisterCombatId(int participantId, int combatId)
        {
            int index = FindParticipant(participantId);
            if ((_faulted) || (_closed) || (index == NONE_INDEX) || (_participants[index].Closed) || (combatId == CombatIds.NONE))
            {
                return false;
            }

            int binding = FindAttribution(combatId);
            if (binding != NONE_INDEX)
            {
                return (_attributions[binding].ParticipantIndex == index) && (!_attributions[binding].IsSummon);
            }
            if (!AddAttribution(combatId, index, false))
            {
                return false;
            }

            _participants[index].CombatId = combatId;
            return true;
        }

        public void OnSessionStarted(double serverTime) => Begin(serverTime);

        public void Begin(double serverTime)
        {
            if ((_faulted) || (_started) || (_closed) || (!IsFinite(serverTime)))
            {
                return;
            }

            _started      = true;
            _startTime    = serverTime;
            _lastTime     = serverTime;
            _nextSendTime = serverTime + Constants.CombatMeter.UPDATE_INTERVAL_SECONDS;
            _phaseCount   = 1;
            _phases[0]    = new PhaseRecord { Phase = FIRST_PHASE, StartTime = serverTime };
            for (int i = 0; i < _participants.Length; ++i)
            {
                _participants[i].AliveSince = serverTime;
            }
        }

        public void ObserveLifeState(int participantId, EParticipantLifeState lifeState, double serverTime)
        {
            int index = FindParticipant(participantId);
            if ((_faulted) || (!_started) || (_closed) || (index == NONE_INDEX) || (_participants[index].Closed)
                || (!IsFinite(serverTime)))
            {
                return;
            }

            ref ParticipantRecord participant = ref _participants[index];
            if ((participant.LifeState == lifeState) && (!participant.ActiveSuspended))
            {
                return;
            }

            double now = AdvanceTime(serverTime);
            if ((participant.LifeState == EParticipantLifeState.Alive) && (participant.LifeState != lifeState))
            {
                if (!participant.ActiveSuspended)
                {
                    participant.ActiveSeconds += CombatMeterRules.Elapsed(participant.AliveSince, now);
                }
                // 생사 통지가 Dead를 건너뛰고 부활 대기로 가도 활동 구간 종료는 사망 한 번이다.
                participant.Deaths = CombatMeterRules.Increment(participant.Deaths);
                SaveDeathRecap(ref participant, now);
                LogDeath(in participant);
            }
            if (lifeState == EParticipantLifeState.Alive)
            {
                participant.AliveSince = now;
            }
            participant.LifeState = lifeState;
            participant.ActiveSuspended = false;
        }

        /// <summary> 전투 개체가 없는 동안 활동 시간만 중단하고 생사와 사망 횟수는 보존한다 </summary>
        public void SuspendActive(int participantId, double serverTime)
        {
            int index = FindParticipant(participantId);
            if ((_faulted) || (!_started) || (_closed) || (index == NONE_INDEX) || (_participants[index].Closed)
                || (_participants[index].ActiveSuspended) || (!IsFinite(serverTime)))
            {
                return;
            }

            ref ParticipantRecord participant = ref _participants[index];
            double now = AdvanceTime(serverTime);
            if (participant.LifeState == EParticipantLifeState.Alive)
            {
                participant.ActiveSeconds += CombatMeterRules.Elapsed(participant.AliveSince, now);
            }
            participant.ActiveSuspended = true;
        }

        public void CloseParticipant(int participantId, double serverTime)
        {
            int index = FindParticipant(participantId);
            if ((_faulted) || (index == NONE_INDEX) || (_participants[index].Closed) || (!IsFinite(serverTime)))
            {
                return;
            }

            ref ParticipantRecord participant = ref _participants[index];
            if ((_started) && (!_closed) && (!participant.ActiveSuspended)
                && (participant.LifeState == EParticipantLifeState.Alive))
            {
                participant.ActiveSeconds += CombatMeterRules.Elapsed(participant.AliveSince, AdvanceTime(serverTime));
            }
            participant.Closed = true;
            for (int i = _attributionCount - 1; i >= 0; --i)
            {
                if (_attributions[i].ParticipantIndex == index)
                {
                    _bindingByCombatId.Remove(_attributions[i].CombatId);
                    --_attributionCount;
                    _attributions[i] = _attributions[_attributionCount];
                    if (i != _attributionCount)
                    {
                        _bindingByCombatId[_attributions[i].CombatId] = i;
                    }
                    _attributions[_attributionCount] = default;
                }
            }
            for (int i = _summonAttributionCount - 1; i >= 0; --i)
            {
                if (_summonAttributions[i].ParticipantIndex == index)
                {
                    --_summonAttributionCount;
                    _summonAttributions[i] = _summonAttributions[_summonAttributionCount];
                    _summonAttributions[_summonAttributionCount] = default;
                }
            }
        }

        /// <summary> 보스 진행 직후의 단계 번호를 받으며 인계와 준비 대기도 구간 시간에 포함한다 </summary>
        public void ObservePhase(byte phase, bool isWaiting, double serverTime)
        {
            if ((_faulted) || (!_started) || (_closed) || (!IsFinite(serverTime)))
            {
                return;
            }

            ref PhaseRecord current = ref _phases[_phaseCount - 1];
            if ((phase < current.Phase) || (phase > Constants.CombatMeter.MAX_PHASES))
            {
                _phaseError = true;
                return;
            }

            double now = AdvanceTime(serverTime);
            if (phase != current.Phase)
            {
                if (_phaseCount >= _phases.Length)
                {
                    _phaseError = true;
                    return;
                }
                ClosePhase(ref current, now);
                _phases[_phaseCount] = new PhaseRecord { Phase = phase, StartTime = now };
                ++_phaseCount;
            }

            ref PhaseRecord observed = ref _phases[_phaseCount - 1];
            if (observed.IsWaiting == isWaiting)
            {
                return;
            }
            if (isWaiting)
            {
                observed.WaitingSince = now;
            }
            else
            {
                observed.WaitingSeconds += CombatMeterRules.Elapsed(observed.WaitingSince, now);
            }
            observed.IsWaiting = isWaiting;
        }

        public void RecordRevive(int participantId, double serverTime)
        {
            int index = FindParticipant(participantId);
            if ((_faulted) || (!_started) || (_closed) || (index == NONE_INDEX) || (_participants[index].Closed)
                || (!IsFinite(serverTime)))
            {
                return;
            }

            _participants[index].Revives = CombatMeterRules.Increment(_participants[index].Revives);
            ObserveLifeState(participantId, EParticipantLifeState.Alive, serverTime);
        }

        public void RecordPotion(int participantId)
        {
            int index = FindParticipant(participantId);
            if ((_faulted) || (!_started) || (_closed) || (index == NONE_INDEX) || (_participants[index].Closed))
            {
                return;
            }
            _participants[index].PotionsUsed = CombatMeterRules.Increment(_participants[index].PotionsUsed);
        }

        /// <summary> 생명력 흡수로 실제 오른 체력을 더한다, 상한이나 가득 찬 체력으로 버려진 몫은 넘어오지 않는다 </summary>
        public void RecordLifeSteal(int participantId, float healed)
        {
            int index = FindParticipant(participantId);
            if ((_faulted) || (!_started) || (_closed) || (index == NONE_INDEX) || (_participants[index].Closed)
                || (!(healed > 0f)) || (float.IsInfinity(healed)))
            {
                return;
            }
            _participants[index].LifeStealHealed += healed;
        }

        public void Record(in DamageRequest request, in DamageResult result, double serverTime)
        {
            CombatMeterHitContext context = Capture(in request);
            Record(in request, in result, in context, serverTime);
        }

        internal CombatMeterHitContext Capture(in DamageRequest request)
        {
            if ((_faulted) || (!_started) || (_closed))
            {
                return new CombatMeterHitContext(NONE_INDEX, NONE_INDEX, false, false, false);
            }

            try
            {
                return CaptureHit(in request);
            }
            catch (Exception exception)
            {
                ReportFault(exception);
                return new CombatMeterHitContext(NONE_INDEX, NONE_INDEX, false, false, false);
            }
        }

        private CombatMeterHitContext CaptureHit(in DamageRequest request)
        {
            int  attackerIndex = ResolveAttacker(request.AttackerId, out bool isSummon);
            int  victimIndex   = NONE_INDEX;
            bool isEnemyTarget = false;
            bool isBossTarget  = false;
            if (_targets.TryGet(request.HitTargetId, out ICombatTarget target))
            {
                isEnemyTarget = target.Faction == ECombatFaction.Enemy;
                isBossTarget  = target is BossServer;
                if ((!target.IsSummon) && (target.Faction == ECombatFaction.Player))
                {
                    int binding = FindAttribution(request.HitTargetId);
                    victimIndex = (binding == NONE_INDEX) ? NONE_INDEX : _attributions[binding].ParticipantIndex;
                }
            }
            return new CombatMeterHitContext(attackerIndex, victimIndex, isSummon, isEnemyTarget, isBossTarget);
        }

        internal void Record(in DamageRequest request, in DamageResult result, in CombatMeterHitContext context,
            double serverTime)
        {
            if ((_faulted) || (!_started) || (_closed) || (!IsFinite(serverTime)) || (serverTime < _startTime))
            {
                return;
            }

            try
            {
                RecordHit(in request, in result, in context, serverTime);
            }
            catch (Exception exception)
            {
                ReportFault(exception);
            }
        }

        private void RecordHit(in DamageRequest request, in DamageResult result, in CombatMeterHitContext context,
            double serverTime)
        {
            int victim = context.VictimIndex;
            if (result.Outcome == EDamageOutcome.Guarded)
            {
                if ((victim != NONE_INDEX) && (!_participants[victim].Closed))
                {
                    _participants[victim].GuardedHits = CombatMeterRules.Increment(_participants[victim].GuardedHits);
                }
                return;
            }
            if ((!result.IsApplied) || (float.IsNaN(result.AppliedAmount)) || (float.IsInfinity(result.AppliedAmount))
                || (result.AppliedAmount < 0f))
            {
                return;
            }

            double now = AdvanceTime(serverTime);
            float amount = result.AppliedAmount;
            int attacker = context.AttackerIndex;
            if ((attacker != NONE_INDEX) && (context.IsEnemyTarget) && (!_participants[attacker].Closed))
            {
                ref ParticipantRecord participant = ref _participants[attacker];
                ECombatMeterSlot slot = CombatMeterRules.ResolveSlot(request.SourceDefinitionId, context.IsSummon,
                    _slotBySource, out bool known);
                if ((!context.IsSummon) && (!string.IsNullOrEmpty(request.SourceDefinitionId))
                    && (_classBySource.TryGetValue(request.SourceDefinitionId, out EPlayerClass sourceClass))
                    && (sourceClass != participant.PlayerClass))
                {
                    slot = ECombatMeterSlot.Other;
                    known = false;
                }
                _unknownSource |= !known;
                ref SlotRecord record = ref participant.Slots[(int)slot];
                record.Damage += amount;
                if (result.CountsAsHit)
                {
                    record.Hits = CombatMeterRules.Increment(record.Hits);
                }
                record.MaxHit = Math.Max(record.MaxHit, amount);
                participant.Damage += amount;
                participant.PhaseDamage[_phaseCount - 1] += amount;
                if (context.IsBossTarget)
                {
                    participant.BossDamage += amount;
                }
                else
                {
                    participant.EnemyDamage += amount;
                }
                _phases[_phaseCount - 1].Damage += amount;
            }
            if ((victim != NONE_INDEX) && (!_participants[victim].Closed))
            {
                ref ParticipantRecord participant = ref _participants[victim];
                participant.DamageTaken += amount;
                if (amount > 0f)
                {
                    AddDamageSource(ref participant, request.SourceDefinitionId, amount);
                    participant.RecentHits[participant.RecentNext] = new ReceivedHit
                    {
                        SourceId = request.SourceDefinitionId ?? string.Empty,
                        Amount = amount,
                        ServerTime = now
                    };
                    participant.RecentNext = (participant.RecentNext + 1) % participant.RecentHits.Length;
                    participant.RecentCount = Math.Min(participant.RecentCount + 1, participant.RecentHits.Length);
                }
                if (result.IsLethal)
                {
                    ObserveLifeState(participant.ParticipantId, EParticipantLifeState.Dead, now);
                }
            }
        }

        public void Tick(double serverTime)
        {
            if (_faulted)
            {
                return;
            }

            try
            {
                FlushWarnings();
                SendUpdates(serverTime);
            }
            catch (Exception exception)
            {
                ReportFault(exception);
            }
        }

        private void SendUpdates(double serverTime)
        {
            if ((!_started) || (_closed) || (!IsFinite(serverTime)))
            {
                return;
            }

            double now = AdvanceTime(serverTime);
            if (now < _nextSendTime)
            {
                return;
            }

            _nextSendTime = now + Constants.CombatMeter.UPDATE_INTERVAL_SECONDS;
            CombatMeterPartyMessage party = BuildParty(now);
            for (int i = 0; i < _participants.Length; ++i)
            {
                ref ParticipantRecord participant = ref _participants[i];
                if ((!participant.Registered) || (participant.Closed))
                {
                    continue;
                }

                NetworkConnectionToClient connection = _connectionLookup?.Invoke(participant.ParticipantId);
                if ((connection == null) || (!connection.isAuthenticated))
                {
                    continue;
                }
                connection.Send(BuildSelf(i, now));
                connection.Send(party);
            }
        }

        public void Resend(int participantId, double serverTime)
        {
            int index = FindParticipant(participantId);
            if ((_faulted) || (!_started) || (_closed) || (index == NONE_INDEX) || (_participants[index].Closed)
                || (!IsFinite(serverTime)))
            {
                return;
            }

            try
            {
                NetworkConnectionToClient connection = _connectionLookup?.Invoke(participantId);
                if ((connection == null) || (!connection.isAuthenticated))
                {
                    return;
                }
                double now = Math.Max(_lastTime, serverTime);
                connection.Send(BuildSelf(index, now));
                connection.Send(BuildParty(now));
            }
            catch (Exception exception)
            {
                ReportFault(exception);
            }
        }

        public void ResendFinal(int participantId)
        {
            int index = FindParticipant(participantId);
            if ((!_finalReady) || (index == NONE_INDEX))
            {
                return;
            }

            try
            {
                NetworkConnectionToClient connection = _connectionLookup?.Invoke(participantId);
                if ((connection == null) || (!connection.isAuthenticated))
                {
                    return;
                }
                connection.Send(_participants[index].FinalSelf);
                connection.Send(_result);
            }
            catch (Exception exception)
            {
                // 표시 전송 실패가 참가자 종료와 다른 연결의 전송을 막지 않도록 연결마다 격리한다.
                Debug.LogException(exception);
            }
        }

        public void OnSessionClosed(ESessionEndReason reason, double serverTime)
        {
            if ((_closed) || (!IsFinite(serverTime)))
            {
                return;
            }

            // 종료 콜백 이후 같은 틱에 남은 판정은 실행돼도 기록은 먼저 동결한다.
            _closed  = true;
            if (_faulted)
            {
                return;
            }

            try
            {
                FreezeResult(reason, serverTime);
                for (int i = 0; i < _participants.Length; ++i)
                {
                    if (_participants[i].Registered)
                    {
                        ResendFinal(_participants[i].ParticipantId);
                    }
                }
                FlushWarnings();
                LogSummaries(reason);
            }
            catch (Exception exception)
            {
                ReportFault(exception);
            }
        }

        private void FreezeResult(ESessionEndReason reason, double serverTime)
        {
            _endTime = _started ? AdvanceTime(serverTime) : serverTime;
            if (_started)
            {
                ClosePhase(ref _phases[_phaseCount - 1], _endTime);
            }
            for (int i = 0; i < _participants.Length; ++i)
            {
                ref ParticipantRecord participant = ref _participants[i];
                if (!participant.Registered)
                {
                    continue;
                }
                if ((_started) && (!participant.Closed) && (!participant.ActiveSuspended)
                    && (participant.LifeState == EParticipantLifeState.Alive))
                {
                    participant.ActiveSeconds += CombatMeterRules.Elapsed(participant.AliveSince, _endTime);
                }
                participant.FinalSelf = BuildFinalSelf(i);
            }
            _result = BuildResult(reason);
            _finalReady = true;
        }

        /// <summary> 실시간 배열은 다음 Tick에서 재사용한다. 장기 보관할 호출자는 복사한다 </summary>
        public bool TryGetSelf(int participantId, double serverTime, out CombatMeterSelfMessage message)
        {
            int index = FindParticipant(participantId);
            if ((!_started) || (index == NONE_INDEX) || (!IsFinite(serverTime)))
            {
                message = default;
                return false;
            }
            message = BuildSelf(index, _closed ? _endTime : Math.Max(_lastTime, serverTime));
            return true;
        }

        public bool TryGetFinalSelf(int participantId, out CombatMeterFinalSelfMessage message)
        {
            int index = FindParticipant(participantId);
            if ((!_finalReady) || (index == NONE_INDEX))
            {
                message = default;
                return false;
            }
            message = _participants[index].FinalSelf;
            return true;
        }

        public bool TryGetResult(out CombatMeterResultMessage message)
        {
            message = _result;
            return _finalReady;
        }

        private void ReportFault(Exception exception)
        {
            if (_faulted)
            {
                return;
            }

            // 기록 장애는 한 번만 알리고 이후 전투 판정과 세션 정산 경로에서 기록을 끈다.
            _faulted = true;
            try
            {
                Debug.LogException(exception);
            }
            catch (Exception)
            {
                // Unity 로그 처리기 자체가 고장나도 원래 기록 장애를 표준 오류에 남긴다.
                Console.Error.WriteLine(exception);
            }
        }

        private static Dictionary<string, int> BuildSourceSlots(SkillCatalogSO catalog)
        {
            int skillCount = (catalog == null) ? 0 : catalog.Skills.Count;
            int extraCount = (catalog == null) ? 0 : catalog.Ascensions.Count;
            var slots = new Dictionary<string, int>(skillCount + extraCount + 1, StringComparer.Ordinal);
            slots.Add(Constants.Combat.BASIC_ATTACK_SOURCE_ID, (int)ECombatMeterSlot.BasicAttack);
            for (int i = 0; i < skillCount; ++i)
            {
                SkillCatalogSO.SkillDefinition skill = catalog.Skills[i];
                if ((skill != null) && (!string.IsNullOrEmpty(skill.DefinitionId)))
                {
                    slots[skill.DefinitionId] = (skill.Slot >= 0) && (skill.Slot < Constants.Player.SKILL_SLOT_COUNT)
                        ? (int)ECombatMeterSlot.Skill1 + skill.Slot
                        : (int)ECombatMeterSlot.Other;
                }
            }
            for (int i = 0; i < extraCount; ++i)
            {
                SkillCatalogSO.AscensionDefinition skill = catalog.Ascensions[i];
                if ((skill != null) && (!string.IsNullOrEmpty(skill.DefinitionId)))
                {
                    slots[skill.DefinitionId] = (int)ECombatMeterSlot.Extra;
                }
            }
            return slots;
        }

        private static Dictionary<string, EPlayerClass> BuildSourceClasses(SkillCatalogSO catalog)
        {
            int skillCount = (catalog == null) ? 0 : catalog.Skills.Count;
            int extraCount = (catalog == null) ? 0 : catalog.Ascensions.Count;
            var classes = new Dictionary<string, EPlayerClass>(skillCount + extraCount, StringComparer.Ordinal);
            for (int i = 0; i < skillCount; ++i)
            {
                SkillCatalogSO.SkillDefinition skill = catalog.Skills[i];
                if ((skill != null) && (!string.IsNullOrEmpty(skill.DefinitionId)))
                {
                    classes[skill.DefinitionId] = skill.PlayerClass;
                }
            }
            for (int i = 0; i < extraCount; ++i)
            {
                SkillCatalogSO.AscensionDefinition skill = catalog.Ascensions[i];
                if ((skill != null) && (!string.IsNullOrEmpty(skill.DefinitionId)))
                {
                    // 현재 Ascensions 계약은 신령사 강림이며 일반 기술처럼 직업 필드를 갖지 않는다.
                    classes[skill.DefinitionId] = EPlayerClass.Summoner;
                }
            }
            return classes;
        }

        private int ResolveAttacker(int combatId, out bool isSummon)
        {
            int binding = FindAttribution(combatId);
            if (binding != NONE_INDEX)
            {
                isSummon = _attributions[binding].IsSummon;
                return _attributions[binding].ParticipantIndex;
            }
            isSummon = false;
            if ((!_targets.TryGet(combatId, out ICombatTarget attacker)) || (!attacker.IsSummon)
                || (attacker.Faction != ECombatFaction.Player))
            {
                ForgetSummon(combatId);
                return NONE_INDEX;
            }

            int owner = FindAttribution(attacker.OwnerCombatId);
            if (owner == NONE_INDEX)
            {
                ForgetSummon(combatId);
                return NONE_INDEX;
            }
            int index = _attributions[owner].ParticipantIndex;
            if (_participants[index].Closed)
            {
                ForgetSummon(combatId);
                return NONE_INDEX;
            }

            isSummon = true;
            for (int i = 0; i < _summonAttributionCount; ++i)
            {
                if (_summonAttributions[i].CombatId == combatId)
                {
                    // 번호가 재사용돼도 현재 개체의 주인을 기준으로 귀속한다.
                    _summonAttributions[i].ParticipantIndex = index;
                    return index;
                }
            }
            if (_summonAttributionCount == _summonAttributions.Length)
            {
                PruneSummons();
            }
            // 반복 소환이 본체와 재접속 번호의 보존 용량을 소모하지 않도록 별도 캐시에 둔다.
            if (_summonAttributionCount < _summonAttributions.Length)
            {
                _summonAttributions[_summonAttributionCount] = new AttributionRecord
                {
                    CombatId = combatId,
                    ParticipantIndex = index,
                    IsSummon = true
                };
                ++_summonAttributionCount;
            }
            else
            {
                _summonOverflow = true;
            }
            return index;
        }

        private void ForgetSummon(int combatId)
        {
            for (int i = 0; i < _summonAttributionCount; ++i)
            {
                if (_summonAttributions[i].CombatId == combatId)
                {
                    RemoveSummonAt(i);
                    return;
                }
            }
        }

        private void PruneSummons()
        {
            // 해제된 번호가 다시 공격하지 않아도 다음 소환을 캐시할 때 자리를 회수한다.
            for (int i = _summonAttributionCount - 1; i >= 0; --i)
            {
                if ((!_targets.TryGet(_summonAttributions[i].CombatId, out ICombatTarget summon))
                    || (!summon.IsSummon) || (summon.Faction != ECombatFaction.Player)
                    || (FindAttribution(summon.OwnerCombatId) == NONE_INDEX))
                {
                    RemoveSummonAt(i);
                }
            }
        }

        private void RemoveSummonAt(int index)
        {
            --_summonAttributionCount;
            _summonAttributions[index] = _summonAttributions[_summonAttributionCount];
            _summonAttributions[_summonAttributionCount] = default;
        }

        private bool AddAttribution(int combatId, int participantIndex, bool isSummon)
        {
            if (_attributionCount >= _attributions.Length)
            {
                // 이 경로는 개체 생성과 재접속 등록에서만 실행한다. 과거 본체 번호는 출정 종료까지 잃지 않는다.
                Array.Resize(ref _attributions, checked(_attributions.Length * BUFFER_GROWTH_FACTOR));
            }
            _attributions[_attributionCount] = new AttributionRecord
            {
                CombatId = combatId,
                ParticipantIndex = participantIndex,
                IsSummon = isSummon
            };
            _bindingByCombatId.Add(combatId, _attributionCount);
            ++_attributionCount;
            return true;
        }

        private int FindAttribution(int combatId)
        {
            return _bindingByCombatId.TryGetValue(combatId, out int index) ? index : NONE_INDEX;
        }

        private int FindParticipant(int participantId)
        {
            for (int i = 0; i < _participants.Length; ++i)
            {
                if ((_participants[i].Registered) && (_participants[i].ParticipantId == participantId))
                {
                    return i;
                }
            }
            return NONE_INDEX;
        }

        private int FindEmptySlot()
        {
            for (int i = 0; i < _participants.Length; ++i)
            {
                if (!_participants[i].Registered)
                {
                    return i;
                }
            }
            return NONE_INDEX;
        }

        private void AddDamageSource(ref ParticipantRecord participant, string sourceId, float amount)
        {
            string id = sourceId ?? string.Empty;
            for (int i = 0; i < participant.SourceCount; ++i)
            {
                if (string.Equals(participant.Sources[i].SourceId, id, StringComparison.Ordinal))
                {
                    participant.Sources[i].Amount += amount;
                    return;
                }
            }
            if (participant.SourceCount >= participant.Sources.Length)
            {
                _sourceOverflow = true;
                return;
            }
            participant.Sources[participant.SourceCount] = new SourceRecord { SourceId = id, Amount = amount };
            ++participant.SourceCount;
        }

        private static void SaveDeathRecap(ref ParticipantRecord participant, double serverTime)
        {
            participant.LastDeathCount = 0;
            for (int i = 0; i < participant.LastDeathRecap.Length; ++i)
            {
                participant.LastDeathRecap[i] = default;
            }
            for (int i = participant.RecentCount; i > 0; --i)
            {
                int index = (participant.RecentNext - i + participant.RecentHits.Length) % participant.RecentHits.Length;
                ref ReceivedHit hit = ref participant.RecentHits[index];
                double beforeDeath = CombatMeterRules.Elapsed(hit.ServerTime, serverTime);
                if (beforeDeath <= Constants.CombatMeter.DEATH_RECAP_WINDOW_SECONDS)
                {
                    participant.LastDeathRecap[participant.LastDeathCount] =
                        new CombatMeterDeathEntry(hit.SourceId, hit.Amount, (float)beforeDeath);
                    ++participant.LastDeathCount;
                }
            }
        }

        // 종료 요약에는 마지막 사망만 남는다. 사망 원인 분포를 모으려면 사망마다 직전 피해 출처를 따로 남겨야 한다
        private void LogDeath(in ParticipantRecord participant)
        {
            try
            {
                var data = new StringBuilder(LOG_INITIAL_CAPACITY);
                data.Append("{\"participantId\":").Append(participant.ParticipantId);
                data.Append(",\"eventId\":");
                ServerLogLine.AppendJsonString(data, _eventId);
                data.Append(",\"deaths\":").Append(participant.Deaths);
                data.Append(",\"potionsUsed\":").Append(participant.PotionsUsed);
                AppendFloat(data, "lifeStealHealed", participant.LifeStealHealed);
                data.Append(",\"hits\":[");
                for (int i = 0; i < participant.LastDeathCount; ++i)
                {
                    if (i > 0)
                    {
                        data.Append(',');
                    }
                    CombatMeterDeathEntry entry = participant.LastDeathRecap[i];
                    data.Append("{\"sourceId\":");
                    ServerLogLine.AppendJsonString(data, entry.SourceId);
                    AppendFloat(data, "amount", entry.Amount);
                    AppendFloat(data, "secondsBeforeDeath", entry.SecondsBeforeDeath);
                    data.Append('}');
                }
                data.Append("]}");

                if (ServerJsonLogHandler.IsInstalled)
                {
                    ServerJsonLogHandler.LogParticipantEvent(ServerLogLine.LEVEL_INFO, DEATH_EVENT,
                        participant.ApiParticipantId, data.ToString());
                }
                else
                {
                    Debug.Log($"[CombatMeter] death, sessionId={_sessionId}, {data}");
                }
            }
            catch (Exception exception)
            {
                // 운영 로그 출력 장애가 생사 판정을 멈추지 않는다
                Debug.LogWarning($"[CombatMeter] 사망 로그 실패: {exception.Message}");
            }
        }

        private double AdvanceTime(double serverTime)
        {
            _lastTime = Math.Max(_lastTime, serverTime);
            return _lastTime;
        }

        private static bool IsFinite(double value)
        {
            return (!double.IsNaN(value)) && (!double.IsInfinity(value));
        }

        private static void ClosePhase(ref PhaseRecord phase, double serverTime)
        {
            phase.EndTime = serverTime;
            if (phase.IsWaiting)
            {
                phase.WaitingSeconds += CombatMeterRules.Elapsed(phase.WaitingSince, serverTime);
                phase.IsWaiting = false;
            }
        }

        private float GetActiveSeconds(in ParticipantRecord participant, double serverTime)
        {
            double seconds = participant.ActiveSeconds;
            if ((!_closed) && (!participant.Closed) && (!participant.ActiveSuspended)
                && (participant.LifeState == EParticipantLifeState.Alive))
            {
                seconds += CombatMeterRules.Elapsed(participant.AliveSince, serverTime);
            }
            return (float)seconds;
        }

        private static void FillSlots(ref ParticipantRecord participant)
        {
            for (int i = 0; i < participant.Slots.Length; ++i)
            {
                ref SlotRecord slot = ref participant.Slots[i];
                participant.SendSlots[i] = new CombatMeterSlotStat(slot.Damage, slot.Hits, slot.MaxHit);
            }
        }

        private CombatMeterSelfMessage BuildSelf(int index, double serverTime)
        {
            ref ParticipantRecord participant = ref _participants[index];
            ref PhaseRecord phase = ref _phases[_phaseCount - 1];
            FillSlots(ref participant);
            return new CombatMeterSelfMessage(phase.Phase, (float)CombatMeterRules.Elapsed(phase.StartTime, serverTime),
                (float)CombatMeterRules.Elapsed(_startTime, serverTime), GetActiveSeconds(in participant, serverTime),
                participant.Damage, participant.PhaseDamage[_phaseCount - 1], participant.DamageTaken,
                participant.GuardedHits, participant.SendSlots);
        }

        private CombatMeterPartyMessage BuildParty(double serverTime)
        {
            CombatMeterPartyEntry[] entries = _partyBuffers[_participantCount];
            int count = 0;
            for (int i = 0; i < _participants.Length; ++i)
            {
                ref ParticipantRecord participant = ref _participants[i];
                if (participant.Registered)
                {
                    entries[count] = new CombatMeterPartyEntry(participant.CombatId, participant.Damage,
                        participant.PhaseDamage[_phaseCount - 1]);
                    ++count;
                }
            }
            ref PhaseRecord phase = ref _phases[_phaseCount - 1];
            return new CombatMeterPartyMessage(phase.Phase, (float)CombatMeterRules.Elapsed(phase.StartTime, serverTime),
                (float)CombatMeterRules.Elapsed(_startTime, serverTime), entries);
        }

        private CombatMeterFinalSelfMessage BuildFinalSelf(int index)
        {
            ref ParticipantRecord participant = ref _participants[index];
            var slots = new CombatMeterSlotStat[Constants.CombatMeter.SLOT_COUNT];
            FillSlots(ref participant);
            Array.Copy(participant.SendSlots, slots, slots.Length);
            var phases = new CombatMeterPhaseStat[_phaseCount];
            for (int i = 0; i < _phaseCount; ++i)
            {
                ref PhaseRecord phase = ref _phases[i];
                phases[i] = new CombatMeterPhaseStat(phase.Phase,
                    (float)CombatMeterRules.Elapsed(phase.StartTime, phase.EndTime), participant.PhaseDamage[i]);
            }
            var death = new CombatMeterDeathEntry[participant.LastDeathCount];
            Array.Copy(participant.LastDeathRecap, death, death.Length);
            return new CombatMeterFinalSelfMessage(_sessionEpoch, EncounterSeconds(), (float)participant.ActiveSeconds,
                participant.Damage, participant.DamageTaken, participant.GuardedHits, participant.Deaths,
                participant.Revives, participant.PotionsUsed, slots, phases, BuildTopSources(in participant), death);
        }

        private static CombatMeterSourceAmount[] BuildTopSources(in ParticipantRecord participant)
        {
            int count = Math.Min(participant.SourceCount, Constants.CombatMeter.TOP_DAMAGE_TAKEN_SOURCES);
            var result = new CombatMeterSourceAmount[count];
            // 전체 출처 합계를 훑고 고정 길이 상위 목록에 삽입해 잦은 작은 타격도 정확히 합산한다.
            for (int i = 0; i < participant.SourceCount; ++i)
            {
                ref SourceRecord source = ref participant.Sources[i];
                for (int rank = 0; rank < result.Length; ++rank)
                {
                    if (source.Amount > result[rank].Amount)
                    {
                        for (int shift = result.Length - 1; shift > rank; --shift)
                        {
                            result[shift] = result[shift - 1];
                        }
                        result[rank] = new CombatMeterSourceAmount(source.SourceId, source.Amount);
                        break;
                    }
                }
            }
            return result;
        }

        private CombatMeterResultMessage BuildResult(ESessionEndReason reason)
        {
            var entries = new CombatMeterResultEntry[_participantCount];
            int count = 0;
            for (int i = 0; i < _participants.Length; ++i)
            {
                ref ParticipantRecord participant = ref _participants[i];
                if (participant.Registered)
                {
                    entries[count] = new CombatMeterResultEntry(participant.CombatId, participant.DisplayName,
                        participant.Damage, participant.DamageTaken, participant.Deaths, participant.Revives);
                    ++count;
                }
            }
            var phases = new CombatMeterPhaseStat[_phaseCount];
            for (int i = 0; i < _phaseCount; ++i)
            {
                ref PhaseRecord phase = ref _phases[i];
                phases[i] = new CombatMeterPhaseStat(phase.Phase,
                    (float)CombatMeterRules.Elapsed(phase.StartTime, phase.EndTime), phase.Damage);
            }
            return new CombatMeterResultMessage(_sessionEpoch, reason, EncounterSeconds(), entries, phases);
        }

        private float EncounterSeconds()
        {
            return _started ? (float)CombatMeterRules.Elapsed(_startTime, _endTime) : 0f;
        }

        private void FlushWarnings()
        {
            LogWarningOnce(_unknownSource, ref _unknownLogged, UNKNOWN_SOURCE_WARNING);
            LogWarningOnce(_sourceOverflow, ref _sourceLogged, SOURCE_CAPACITY_WARNING);
            LogWarningOnce(_summonOverflow, ref _summonLogged, SUMMON_CACHE_WARNING);
            LogWarningOnce(_participantError, ref _participantLogged, PARTICIPANT_CAPACITY_WARNING);
            LogWarningOnce(_phaseError, ref _phaseLogged, PHASE_CAPACITY_WARNING);
        }

        private static void LogWarningOnce(bool pending, ref bool logged, string text)
        {
            if ((!pending) || (logged))
            {
                return;
            }
            logged = true;
            Debug.LogWarning(text);
        }

        private void LogSummaries(ESessionEndReason reason)
        {
            var data = new StringBuilder(LOG_INITIAL_CAPACITY);
            for (int i = 0; i < _participants.Length; ++i)
            {
                ref ParticipantRecord participant = ref _participants[i];
                if (!participant.Registered)
                {
                    continue;
                }

                data.Clear();
                data.Append("{\"participantId\":").Append(participant.ParticipantId);
                data.Append(",\"eventId\":");
                ServerLogLine.AppendJsonString(data, _eventId);
                data.Append(",\"reasonCode\":").Append((int)reason);
                data.Append(",\"sessionEpoch\":").Append(_sessionEpoch);
                data.Append(",\"combatId\":").Append(participant.CombatId);
                data.Append(",\"classId\":").Append((int)participant.PlayerClass);
                AppendFloat(data, "attack", participant.Stats.Attack);
                AppendFloat(data, "maxHealth", participant.Stats.MaxHealth);
                AppendFloat(data, "defense", participant.Stats.Defense);
                AppendFloat(data, "critChance", participant.Stats.CritChance);
                AppendFloat(data, "maxEnergy", participant.Stats.MaxEnergy);
                AppendFloat(data, "moveSpeed", participant.Stats.MoveSpeed);
                AppendFloat(data, "encounterSeconds", EncounterSeconds());
                AppendFloat(data, "activeSeconds", (float)participant.ActiveSeconds);
                AppendFloat(data, "damage", participant.Damage);
                AppendFloat(data, "bossDamage", participant.BossDamage);
                AppendFloat(data, "enemyDamage", participant.EnemyDamage);
                AppendFloat(data, "damageTaken", participant.DamageTaken);
                data.Append(",\"guardedHits\":").Append(participant.GuardedHits);
                data.Append(",\"deaths\":").Append(participant.Deaths);
                data.Append(",\"revives\":").Append(participant.Revives);
                data.Append(",\"potionsUsed\":").Append(participant.PotionsUsed);
                AppendFloat(data, "lifeSteal", participant.Stats.LifeSteal);
                AppendFloat(data, "lifeStealHealed", participant.LifeStealHealed);
                data.Append(",\"phases\":[");
                for (int phaseIndex = 0; phaseIndex < _phaseCount; ++phaseIndex)
                {
                    if (phaseIndex > 0)
                    {
                        data.Append(',');
                    }
                    ref PhaseRecord phase = ref _phases[phaseIndex];
                    data.Append("{\"phase\":").Append(phase.Phase);
                    AppendFloat(data, "seconds", (float)CombatMeterRules.Elapsed(phase.StartTime, phase.EndTime));
                    AppendFloat(data, "waitingSeconds", (float)phase.WaitingSeconds);
                    AppendFloat(data, "damage", participant.PhaseDamage[phaseIndex]);
                    data.Append('}');
                }
                data.Append("]}");
                try
                {
                    if (ServerJsonLogHandler.IsInstalled)
                    {
                        ServerJsonLogHandler.LogParticipantEvent(ServerLogLine.LEVEL_INFO, SUMMARY_EVENT,
                            participant.ApiParticipantId, data.ToString());
                    }
                    else
                    {
                        Debug.Log($"[CombatMeter] sessionId={_sessionId}, eventId={_eventId}, reason={(int)reason}, {data}");
                    }
                }
                catch (Exception exception)
                {
                    // 운영 로그 출력 장애도 세션 정산을 멈추지 않는다.
                    ReportFault(exception);
                    return;
                }
            }
        }

        private static void AppendFloat(StringBuilder data, string key, float value)
        {
            data.Append(",\"").Append(key).Append("\":");
            data.Append(value.ToString("R", CultureInfo.InvariantCulture));
        }
    }
}
