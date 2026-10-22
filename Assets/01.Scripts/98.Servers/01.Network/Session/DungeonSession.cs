// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Systems;

namespace Shinshi.Network
{
    /// <summary> 던전 참가자, 웨이브 결과와 개인 드롭을 저장 큐에 연결한다, 서버 부트스트랩이 실행 중에 만든다 </summary>
    public sealed class DungeonSession : IWaveSessionListener, IDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int PENDING_EVENTS_PER_PARTICIPANT = 7;

        private readonly string                   _sessionId;
        private readonly ParticipantRoster        _roster;
        private readonly SessionEventQueue        _events;
        private readonly IWaveSession             _wave;
        private readonly bool                     _isBossSession;
        private readonly double                   _timeLimitSeconds;
        private readonly int[]                    _expectedKills;
        private readonly int[]                    _killsPerWave;
        private readonly bool[]                   _clearedWaves;
        private readonly string[]                 _waveExperienceSourceIds;
        private readonly Dictionary<string, int>  _sourceIndices;
        private readonly int[]                    _sourceWaves;
        private readonly int[] _sourceExperienceKinds;
        private CharacterLevelSettings _experienceSettings;
        private string _experienceRegion;
        private string _experienceHash;
        private readonly EnemyLootRule[]          _sourceRules;
        private readonly bool[]                   _defeatedSources;
        private readonly string[][]               _participantEffects;
        private readonly SessionEventData[]       _pending;
        private readonly SessionConfigSO          _sessionConfig;
        private readonly byte[]                   _lootSalt;
        private readonly SettlementLedger         _ledger;

        // 명단 순서별 개인 이탈(ParticipantSettled) 저장 순번, 0이면 진행 중 개인 이탈이 없었다
        private readonly long[]                   _departureSequences;

        // 시작 전에 GameDB에서 복사한다. 일반전은 웨이브 보상이 없으면 시작하지 않는다
        private SettlementRewardRule _rewardRule;
        private BossRewardRule       _bossRule;
        private bool                 _hasRewardRule = false;
        private bool                 _hasBossRule   = false;

        // 보스전만 있다. 일반전은 부활 횟수 제한과 전멸 실패가 없다
        private readonly ReviveBudget             _revives;

        // 종료 이벤트 전에 마지막 이벤트(미적용 물약 되돌림)를 넣을 곳, 없으면 부르지 않는다
        private ISessionEndingListener _endingListener = null;

        private ICombatMeterSink _combatMeterSink = null;

        private double            _serverTime          = 0.0;
        private double            _startTime           = 0.0;
        private long              _killSequence        = 0;
        private long              _endEventSequence    = 0;
        private int               _pendingCount        = 0;
        private int               _clearedCount        = 0;
        private bool              _started             = false;
        private bool              _inTick              = false;
        private bool              _successPending      = false;
        private bool              _timeoutPending      = false;
        private bool              _finalSuccessPending = false;
        private bool              _failurePending      = false;
        private bool              _storageFault        = false;
        private bool              _disposed            = false;
        private ESessionEndReason _endReason;
        private ESessionEndReason _failureReason       = ESessionEndReason.AllDead;
        private ESessionEndReason _lastDepartureReason = ESessionEndReason.Abandon;

        /****************************************
        *              Properties
        ****************************************/

        public ParticipantRoster Roster            => _roster;
        public SessionEventQueue Events            => _events;
        public bool              IsRunning         => (_started) && (!IsEnded) && (!_disposed);
        public bool              IsEnded           { get; private set; }
        public ESessionEndReason EndReason         => _endReason;
        public int               StartingCount     { get; private set; }
        public float             EnemyHealthScale  { get; private set; }
        public float             BossHealthScale   { get; private set; }
        public float             DamageScale       { get; private set; }
        public long              KillSequence      => _killSequence;
        public int               PendingEventCount => _pendingCount;
        public bool              HasStorageFault   => _storageFault;
        public long              EndEventSequence  => _endEventSequence;
        public bool              CanReturn         => (IsEnded) && (_endEventSequence > 0) && (_events.LastAcknowledgedSequence >= _endEventSequence);
        public bool              IsBossSession     => _isBossSession;

        // 참가자별 확보 보상 원장, 명단 순서로 읽는다
        public SettlementLedger Ledger => _ledger;

        // 저장 큐가 확정 거절로 멈췄다. 종료 이벤트도 저장되지 않으므로 API 장애 취소가 출정을 닫는다
        public bool HasTerminalStorage => _events.IsTerminal;

        // 보스전 공용 부활 잔여, 일반전은 -1
        public int RemainingRevives => (_revives != null) ? _revives.Remaining : -1;

        // Begin이 받은 서버 시각, 시작 전은 0이다
        public double StartTime => _startTime;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> config와 waveConfig는 GameDB 초기화 뒤 넘긴다, wave가 null이면 개발용 보스 종료만 제공한다.
        /// lootSalt는 start 확정 때 만든 출정 비밀이고 null이면 여기서 만든다 </summary>
        public DungeonSession(string sessionId, ParticipantRoster roster, SessionEventQueue events,
            SessionConfigSO config, WaveConfigSO waveConfig, IWaveSession wave = null, byte[] lootSalt = null)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                throw new ArgumentException("출정 ID가 비어 있다", nameof(sessionId));
            }

            if ((roster == null) || (events == null))
            {
                throw new ArgumentNullException((roster == null) ? nameof(roster) : nameof(events));
            }

            if (!string.Equals(sessionId, events.SessionId, StringComparison.Ordinal))
            {
                throw new ArgumentException("저장 큐와 던전의 출정 ID가 다르다", nameof(events));
            }

            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if ((wave != null) && (waveConfig == null))
            {
                throw new ArgumentNullException(nameof(waveConfig));
            }

            if ((lootSalt != null) && (lootSalt.Length != Constants.Session.LOOT_SALT_BYTES))
            {
                throw new ArgumentException("출정 비밀 salt 길이가 맞지 않다", nameof(lootSalt));
            }

            _sessionId          = sessionId;
            _roster             = roster;
            _events             = events;
            _wave               = wave;
            _isBossSession      = wave == null;
            _sessionConfig      = config;
            _lootSalt           = (lootSalt != null) ? (byte[])lootSalt.Clone() : LootRoller.CreateSalt();
            _revives            = _isBossSession ? new ReviveBudget(config.BossSharedRevives) : null;
            _timeLimitSeconds   = (waveConfig != null) ? waveConfig.TimeLimitSeconds : 0.0;
            int waveCount       = (wave != null) ? waveConfig.Waves.Count : 0;
            int sourceCount     = (wave != null) ? waveConfig.CountTotalEnemies() : 0;
            _expectedKills      = new int[waveCount];
            _killsPerWave       = new int[waveCount];
            _clearedWaves       = new bool[waveCount];
            _waveExperienceSourceIds = new string[waveCount];
            _sourceIndices      = new Dictionary<string, int>(sourceCount, StringComparer.Ordinal);
            _sourceWaves        = new int[sourceCount];
            _sourceExperienceKinds = new int[sourceCount];
            _sourceRules        = new EnemyLootRule[sourceCount];
            _defeatedSources    = new bool[sourceCount];
            _participantEffects = new string[Constants.Session.MAX_PARTY_SIZE][];
            _ledger             = new SettlementLedger(Constants.Session.MAX_PARTY_SIZE);
            _departureSequences = new long[Constants.Session.MAX_PARTY_SIZE];
            // 저장이 멈추면 새 처치, 웨이브, 재접속을 받지 않고 그 틱 끝에 출정이 끝난다. 그래서 보존 버퍼에 쌓일 수 있는 것은
            // 참가자마다 처치 또는 웨이브 결과와 기본, 추가 적립(3), 연결 종료(1), 개인 종료 두 이벤트(2), 그리고 세션 종료 하나다
            _pending = new SessionEventData[(Constants.Session.MAX_PARTY_SIZE * PENDING_EVENTS_PER_PARTICIPANT) + 1];

            for (int i = 0; i < waveCount; ++i)
            {
                _expectedKills[i] = waveConfig.Waves[i].CountEnemies();
                _waveExperienceSourceIds[i] = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            _roster.ParticipantClosed += OnParticipantClosed;
            _wave?.SetListener(this);
        }

        /// <summary> 서버 부트 이후 GameDB 원본으로 만든다, source ID와 직업 효과는 시작 전에 등록한다 </summary>
        public static DungeonSession CreateNormal(string sessionId, ParticipantRoster roster,
            SessionEventQueue events, IWaveSession wave)
        {
            if (wave == null)
            {
                throw new ArgumentNullException(nameof(wave));
            }

            return CreateNormal(sessionId, roster, events, GameDB.Session, GameDB.Waves, GameDB.Rewards, wave, null);
        }

        /// <summary> 넘긴 원본으로 일반전을 만든다, 던전 호스트와 시험이 쓴다. lootSalt는 start 확정 때 만든 출정 비밀이다 </summary>
        public static DungeonSession CreateNormal(string sessionId, ParticipantRoster roster, SessionEventQueue events,
            SessionConfigSO config, WaveConfigSO waves, RewardCatalogSO rewards, IWaveSession wave, byte[] lootSalt)
        {
            if (wave == null)
            {
                throw new ArgumentNullException(nameof(wave));
            }

            var session = new DungeonSession(sessionId, roster, events, config, waves, wave, lootSalt);
            session.RegisterRewards(rewards);
            return session;
        }

        /// <summary> 웨이브 완료 보상과 정련편, 티켓 ID를 시작 전에 복사한다. 일반전은 등록하지 않으면 시작하지 않는다 </summary>
        public bool RegisterRewards(RewardCatalogSO rewards)
        {
            if ((_started) || (_disposed) || (rewards == null))
            {
                return false;
            }

            _rewardRule    = new SettlementRewardRule(rewards);
            _hasRewardRule = true;
            return true;
        }

        /// <summary> 보스전 처치 성공 보상을 시작 전에 복사한다, 등록하지 않은 보스전 성공은 기본 보상 없이 정산한다 </summary>
        public bool RegisterBossReward(RewardCatalogSO.BossRewardSpec spec)
        {
            if ((_started) || (_disposed) || (!_isBossSession) || (spec == null))
            {
                return false;
            }

            _bossRule    = new BossRewardRule(spec);
            _hasBossRule = true;
            return true;
        }

        /// <summary> 실제 스폰 계획의 source ID와 적 드롭 명세를 시작 전에 등록한다, 등록 순서는 처치 순번과 무관하다 </summary>
        public bool RegisterEnemySource(int waveIndex, string sourceId, RewardCatalogSO.EnemyDropSpec drop)
        {
            return RegisterEnemySource(waveIndex, sourceId, drop, 0);
        }

        public bool ConfigureExperience(CharacterLevelSettings settings, string regionId)
        {
            if (_started || (settings == null) || !settings.IsValid()
                || (settings.GetGrant(regionId, CharacterExperienceSource.NORMAL_ENEMY) == 0))
            {
                return false;
            }
            for (int i = 0; i < _sourceIndices.Count; ++i)
            {
                if ((_sourceExperienceKinds[i] < CharacterExperienceSource.NORMAL_ENEMY)
                    || (_sourceExperienceKinds[i] > CharacterExperienceSource.ELITE_ENEMY))
                {
                    return false;
                }
            }
            _experienceSettings = settings;
            _experienceRegion = regionId;
            _experienceHash = CharacterLevelRules.SettingsHash(settings);
            return true;
        }

        public bool RegisterEnemySource(int waveIndex, string sourceId, RewardCatalogSO.EnemyDropSpec drop, int experienceKind)
        {
            if ((_started) || (_disposed) || (waveIndex < 0) || (waveIndex >= _expectedKills.Length)
                || (string.IsNullOrEmpty(sourceId)) || (drop == null) || (_sourceIndices.ContainsKey(sourceId))
                || (_sourceIndices.Count >= _sourceWaves.Length)
                || ((_experienceSettings != null) && ((experienceKind < CharacterExperienceSource.NORMAL_ENEMY)
                    || (experienceKind > CharacterExperienceSource.ELITE_ENEMY))))
            {
                return false;
            }

            int count = 0;
            for (int i = 0; i < _sourceIndices.Count; ++i)
            {
                if (_sourceWaves[i] == waveIndex)
                {
                    ++count;
                }
            }

            if (count >= _expectedKills[waveIndex])
            {
                return false;
            }

            var rule = new EnemyLootRule(drop);
            int index = _sourceIndices.Count;
            _sourceWaves[index] = waveIndex;
            _sourceRules[index] = rule;
            _sourceExperienceKinds[index] = experienceKind;
            _sourceIndices.Add(sourceId, index);
            return true;
        }

        /// <summary> 자기 직업의 전체 효과 ID를 시작 전에 복사한다, 이미 가진 효과를 제외하지 않는다 </summary>
        public bool RegisterParticipantLoot(int participantId, IReadOnlyList<string> effectIds)
        {
            if ((_started) || (_disposed) || (effectIds == null) || (effectIds.Count == 0))
            {
                return false;
            }

            int index = IndexOfParticipant(participantId);
            if (index < 0)
            {
                return false;
            }

            var copy = new string[effectIds.Count];
            for (int i = 0; i < effectIds.Count; ++i)
            {
                if (string.IsNullOrEmpty(effectIds[i]))
                {
                    return false;
                }

                for (int j = 0; j < i; ++j)
                {
                    if (copy[j] == effectIds[i])
                    {
                        return false;
                    }
                }

                copy[i] = effectIds[i];
            }

            // 카탈로그 순서가 바뀌어도 같은 시드의 결과를 유지하도록 ID 순서를 고정한다
            Array.Sort(copy, StringComparer.Ordinal);
            _participantEffects[index] = copy;
            _ledger.SetEffects(index, copy);
            return true;
        }

        /// <summary> start 저장 영수증 뒤 한 번 호출한다, Running 집합과 시작 인원 배율을 고정한다 </summary>
        public bool Begin(double serverTime)
        {
            if ((_started) || (_disposed) || (!IsValidTime(serverTime))
                || (serverTime < _roster.LastServerTime)
                || (_roster.Count < Constants.Session.MIN_PARTY_SIZE) || (_roster.ActiveCount != _roster.Count)
                || (_sourceIndices.Count != _sourceWaves.Length) || ((!_isBossSession) && (!_hasRewardRule)))
            {
                return false;
            }

            for (int i = 0; i < _roster.Count; ++i)
            {
                if ((_participantEffects[i] == null) && (!_isBossSession))
                {
                    return false;
                }
            }

            StartingCount    = _roster.Count;
            EnemyHealthScale = _sessionConfig.GetEnemyHealthScale(StartingCount);
            BossHealthScale  = _sessionConfig.GetBossHealthScale(StartingCount);
            DamageScale      = _sessionConfig.GetDamageScale(StartingCount);
            _roster.Seal();
            _serverTime = serverTime;
            _startTime  = serverTime;
            _started    = true;
            if (_isBossSession)
            {
                _combatMeterSink?.OnSessionStarted(serverTime);
            }

            _roster.Tick(serverTime);
            _events.Tick(serverTime);
            for (int i = 0; i < _roster.Count; ++i)
            {
                Save(SessionEventData.ParticipantJoined(serverTime, _roster.GetAt(i).ParticipantKey, _isBossSession));
            }

            if (_storageFault)
            {
                Finish(ESessionEndReason.ServiceCancelled);
                return true;
            }

            _wave?.Begin(serverTime);
            return true;
        }

        /// <summary> 피해 처리 전에 부른다, 유예 만료를 먼저 닫고 이 틱의 서버 시각을 고정한다 </summary>
        public bool BeginTick(double serverTime)
        {
            if ((!_started) || (_disposed) || (_inTick) || (!IsValidTime(serverTime)) || (serverTime < _serverTime))
            {
                return false;
            }

            if (serverTime < _roster.LastServerTime)
            {
                return false;
            }

            _serverTime = serverTime;
            _inTick     = true;
            if (!IsEnded)
            {
                _roster.Tick(serverTime);
            }

            return true;
        }

        /// <summary> 예정 피해와 사망 뒤, 부활 승인과 복제 전에 부른다, 같은 틱의 마지막 처치 성공을 시간 초과보다 우선한다 </summary>
        public void CompleteTick()
        {
            if ((!_inTick) || (_disposed))
            {
                return;
            }

            if (!IsEnded)
            {
                bool storageStopped = (_storageFault) || (_events.IsTerminal);

                // 같은 틱에 겹치면 저장 장애, 보스 기믹의 최종 성공, 기믹 실패, 전멸, 일반 성공, 전원 이탈, 시간 초과 순으로 하나만 적용한다.
                // 일반 보스 처치는 같은 틱 전원 사망에 지고, 기믹이 내준 최종 성공만 전멸보다 앞선다
                if (storageStopped)
                {
                    Finish(ESessionEndReason.ServiceCancelled);
                }
                else if (_finalSuccessPending)
                {
                    Finish(ESessionEndReason.Success);
                }
                else if (_failurePending)
                {
                    Finish(_failureReason);
                }
                else if (IsWiped())
                {
                    _revives.EvaluateWipe(0);
                    Finish(ESessionEndReason.AllDead);
                }
                else if (_successPending)
                {
                    Finish(ESessionEndReason.Success);
                }
                else if (_roster.ActiveCount == 0)
                {
                    Finish(_lastDepartureReason);
                }
                else if ((_timeoutPending) || ((!_isBossSession) && ((_serverTime - _startTime) >= _timeLimitSeconds)))
                {
                    Finish(ESessionEndReason.Timeout);
                }
            }

            if (_events.IsTerminal)
            {
                DiscardPending();
            }

            _events.Tick(_serverTime);
            FlushPending();
            _inTick = false;
        }

        // 전투 런타임이 플레이어 기술, 보스 피해 뒤 생사 보고 앞에서 부른다. 적이 플레이어를 때린 결과가 같은 틱의 생사 보고와
        // 종료 판정에 들어가고, 처치 통지(OnEnemyDefeated)는 이 틱 안에서만 받는다
        /// <summary> 일반전 웨이브를 이 틱의 서버 시각으로 진행한다, BeginTick과 CompleteTick 사이에서만 돈다 </summary>
        public void TickWave()
        {
            if ((_wave == null) || (!_inTick) || (IsEnded) || (_disposed) || (_storageFault) || (_events.IsTerminal))
            {
                return;
            }

            _wave.Tick(_serverTime);
        }

        /// <summary> 일반전 웨이브가 시작했고 아직 끝나지 않았는지, 제한 시간이 흐르는 동안만 참이다 </summary>
        public bool IsWaveRunning => (_wave != null) && (_wave.IsRunning);

        /// <summary> 일반전 진행 값, 웨이브가 없으면 거짓 </summary>
        public bool TryGetWaveProgress(out WaveProgressSnapshot progress)
        {
            progress = default;
            if (_wave == null)
            {
                return false;
            }

            progress = _wave.Progress;
            return true;
        }

        /// <summary> 진행 중에 이 참가자만 먼저 떠났는지(포기, 유예 만료), 출정 종료로 닫힌 것은 아니다 </summary>
        public bool HasDeparture(int participantId)
        {
            int index = IndexOfParticipant(participantId);
            return (index >= 0) && (_departureSequences[index] > 0);
        }

        /// <summary> 개인 이탈 기록이 저장 확인됐다, API가 계정 임대를 풀어 마을 입장권을 받을 수 있다 </summary>
        public bool IsDepartureStored(int participantId)
        {
            int index = IndexOfParticipant(participantId);
            return (index >= 0) && (_departureSequences[index] > 0)
                && (_events.LastAcknowledgedSequence >= _departureSequences[index]);
        }

        /// <summary> 부활 대기 시간과 보스 공용 횟수를 본다, 횟수는 줄이지 않는다. 안전 지점을 찾기 전에 부른다 </summary>
        public ERejectReason CheckRevive(int participantId, double deathTime, double serverTime)
        {
            if ((!IsRunning) || (!_roster.TryGet(participantId, out ParticipantSnapshot participant))
                || (participant.ConnectionState == EParticipantConnectionState.Departed))
            {
                return ERejectReason.SessionTerminal;
            }

            if (participant.LifeState == EParticipantLifeState.Alive)
            {
                return ERejectReason.WrongPhase;
            }

            if (participant.LifeState == EParticipantLifeState.Spectating)
            {
                return ERejectReason.NoRevives;
            }

            if (_isBossSession)
            {
                return _revives.CanRevive(deathTime, serverTime, _sessionConfig.BossReviveDelay);
            }

            return PlayerLifeRules.IsReviveReady(deathTime, serverTime, _sessionConfig.NormalReviveDelay)
                ? ERejectReason.None
                : ERejectReason.TooEarly;
        }

        /// <summary> 안전 지점을 찾은 뒤 부른다, 보스전은 이때만 공용 횟수를 하나 쓰고 명단 생사를 Alive로 옮긴다 </summary>
        public ERejectReason CommitRevive(int participantId, double deathTime, double serverTime, out float healthRate)
        {
            healthRate = 0f;
            ERejectReason reason = CheckRevive(participantId, deathTime, serverTime);
            if (reason != ERejectReason.None)
            {
                return reason;
            }

            if (_isBossSession)
            {
                reason = _revives.TryCommitRevive();
                if (reason != ERejectReason.None)
                {
                    return reason;
                }
            }

            healthRate = _isBossSession ? _sessionConfig.BossReviveHealthRate : _sessionConfig.NormalReviveHealthRate;
            _roster.TrySetLifeState(participantId, EParticipantLifeState.Alive);
            return ERejectReason.None;
        }

        /// <summary> 사망한 참가자가 들어갈 상태, 보스전에서 공용 횟수가 없으면 관전이다 </summary>
        public EParticipantLifeState LifeStateOnDeath()
        {
            return (_revives != null) ? _revives.LifeStateOnDeath() : EParticipantLifeState.Dead;
        }

        /// <summary> 인증과 참가자 토큰 검증 뒤 연결 축만 되돌린다, HP와 쿨다운은 전투 복원 쪽이 보존한다 </summary>
        public bool TryReconnect(int participantId, double serverTime)
        {
            if ((!IsRunning) || (_storageFault) || (!AdvanceTime(serverTime))
                || (_storageFault)
                || (!_roster.TryGet(participantId, out ParticipantSnapshot before)))
            {
                return false;
            }

            if (!_roster.TryConnect(participantId, serverTime))
            {
                return false;
            }

            if (before.ConnectionState != EParticipantConnectionState.Connected)
            {
                Save(SessionEventData.ParticipantJoined(serverTime, before.ParticipantKey, _isBossSession));
            }

            return true;
        }

        /// <summary> 서버가 연결 해제를 감지한 시각을 전달한다, 타이머와 생사 축은 계속 유지한다 </summary>
        public bool TryDisconnect(int participantId, double serverTime)
        {
            if ((!IsRunning) || (!AdvanceTime(serverTime)) || (!_roster.TryGet(participantId, out ParticipantSnapshot before)))
            {
                return false;
            }

            if (!_roster.TryDisconnect(participantId, serverTime))
            {
                return false;
            }

            if (before.ConnectionState == EParticipantConnectionState.Connected)
            {
                Save(SessionEventData.ParticipantDisconnected(serverTime, before.ParticipantKey, _isBossSession));
            }

            return true;
        }

        /// <summary> 명시 포기는 개인 종료만 처리한다, 남은 파티의 시작 인원 배율은 유지한다 </summary>
        public bool TryAbandon(int participantId, double serverTime)
        {
            if ((!IsRunning) || (!AdvanceTime(serverTime)))
            {
                return false;
            }

            return _roster.TryClose(participantId, ESessionEndReason.Abandon, serverTime);
        }

        public void OnEnemyDefeated(int waveIndex, string sourceId, string killerParticipantId)
        {
            if ((!IsRunning) || (!_inTick) || (_storageFault) || (sourceId == null)
                || (!_sourceIndices.TryGetValue(sourceId, out int index)) || (_sourceWaves[index] != waveIndex)
                || (_defeatedSources[index]))
            {
                return;
            }

            if ((_experienceSettings != null) && ((_sourceExperienceKinds[index] < CharacterExperienceSource.NORMAL_ENEMY)
                || (_sourceExperienceKinds[index] > CharacterExperienceSource.ELITE_ENEMY)))
            {
                _storageFault = true;
                return;
            }
            _defeatedSources[index] = true;
            ++_killSequence;
            ++_killsPerWave[waveIndex];
            for (int i = 0; i < _roster.Count; ++i)
            {
                ParticipantSnapshot participant = _roster.GetAt(i);
                if (!_roster.IsRewardEligible(participant.ParticipantId, _serverTime))
                {
                    continue;
                }

                LootRollResult loot = LootRoller.Roll(_lootSalt, _sessionId, participant.ParticipantKey, _killSequence,
                    in _sourceRules[index], _participantEffects[i]);
                Save(SessionEventData.EnemyKilled(_serverTime, participant.ParticipantKey, sourceId, killerParticipantId,
                    _killSequence, waveIndex, in loot));
                GrantExperience(participant.ParticipantKey, _sourceExperienceKinds[index], sourceId);
                _ledger.AddLoot(i, in loot);
                Accrue(i, participant.ParticipantKey, loot.ExtraEssence, waveIndex, _killSequence);
            }
        }

        public void OnWaveCleared(int waveIndex)
        {
            if ((!IsRunning) || (!_inTick) || (_storageFault) || (waveIndex < 0) || (waveIndex >= _expectedKills.Length)
                || (_clearedWaves[waveIndex]) || (_killsPerWave[waveIndex] != _expectedKills[waveIndex]))
            {
                return;
            }

            _clearedWaves[waveIndex] = true;
            ++_clearedCount;
            // 웨이브 보상 자격을 이 시점에 고정한다, 사망자도 접속 중이거나 유예 안이면 포함한다
            for (int i = 0; i < _roster.Count; ++i)
            {
                ParticipantSnapshot participant = _roster.GetAt(i);
                if (_roster.IsRewardEligible(participant.ParticipantId, _serverTime))
                {
                    Save(SessionEventData.WaveCleared(_serverTime, participant.ParticipantKey, waveIndex));
                    GrantExperience(participant.ParticipantKey, CharacterExperienceSource.WAVE_CLEARED,
                        _waveExperienceSourceIds[waveIndex]);
                    long baseEssence = _ledger.AddWaveBase(i, _rewardRule.BaseEssencePerWave,
                        _rewardRule.RefineShardsPerWave);
                    SaveAccrual(SessionEventData.BaseEssenceAccrued(_serverTime, participant.ParticipantKey, baseEssence,
                        waveIndex, 0));
                    Accrue(i, participant.ParticipantKey, _rewardRule.ExtraEssencePerWave, waveIndex, 0);
                }
            }

            _successPending = _clearedCount == _expectedKills.Length;
        }

        public void OnWaveSessionEnded(in WaveSessionOutcome outcome)
        {
            if ((!IsRunning) || (!_inTick) || (_storageFault))
            {
                return;
            }

            if (outcome.Reason == ESessionEndReason.Timeout)
            {
                _timeoutPending = true;
            }
            else if ((outcome.Reason == ESessionEndReason.Success) && (_clearedCount == _expectedKills.Length))
            {
                _successPending = true;
            }
        }

        // 종료는 CompleteTick이 판정한다. 같은 틱에 기믹이 실패했거나 전원이 죽었으면 그쪽이 먼저라 이 성공은 버려진다
        /// <summary> 보스 사망이 확정된 틱 안에서 부른다, 보스전이 진행 중일 때만 받는다 </summary>
        public bool ReportBossDefeated()
        {
            if ((!IsRunning) || (!_inTick) || (!_isBossSession) || (_storageFault))
            {
                return false;
            }

            _successPending = true;
            return true;
        }

        // 일반 처치(ReportBossDefeated)와 달리 같은 틱 전원 사망보다 앞선다. 보상과 정산은 일반 성공과 같은 종료 경로 한 번이다
        /// <summary> 보스 기믹이 최종 성공을 내준 틱 안에서 부른다, 보스전이 진행 중일 때만 받는다 </summary>
        public bool ReportBossFinalSuccess()
        {
            if ((!IsRunning) || (!_inTick) || (!_isBossSession) || (_storageFault))
            {
                return false;
            }

            _finalSuccessPending = true;
            return true;
        }

        // 전멸이 아닌데 더 진행할 수 없는 보스전을 닫는다. 새 종료 사유를 만들면 정산과 백엔드 계약이 바뀌므로 기존 값만 받는다.
        // 같은 틱에 두 번 오면 먼저 온 사유를 지킨다
        /// <summary> 보스 기믹이 실패한 틱 안에서 부른다, 사유는 Timeout과 AllDead만 받고 보스전이 진행 중일 때만 받는다 </summary>
        public bool ReportBossFightFailed(ESessionEndReason reason)
        {
            if ((!IsRunning) || (!_inTick) || (!_isBossSession) || (_storageFault)
                || ((reason != ESessionEndReason.Timeout) && (reason != ESessionEndReason.AllDead)))
            {
                return false;
            }

            if (!_failurePending)
            {
                _failurePending = true;
                _failureReason  = reason;
            }

            return true;
        }

        /// <summary> 임대 확인 등으로 확정한 서비스 장애 취소, 경제 환급은 백엔드가 동일 operationId로 처리한다 </summary>
        public bool CancelService(double serverTime)
        {
            if ((!IsRunning) || (!AdvanceTime(serverTime)))
            {
                return false;
            }

            Finish(ESessionEndReason.ServiceCancelled);
            return true;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary> 개발 전용 보스 처치 진입점, 출시 서버에는 메서드 자체가 포함되지 않는다 </summary>
        public bool TryCompleteBossForDevelopment(double serverTime)
        {
            if ((!IsRunning) || (!_isBossSession) || (_storageFault) || (!AdvanceTime(serverTime))
                || (_storageFault) || (_roster.ActiveCount == 0))
            {
                return false;
            }

            Finish(ESessionEndReason.Success);
            return true;
        }
#endif

        /// <summary> 시작 전에 한 번 넣는다. 종료 판정 직후 종료 이벤트보다 먼저 부른다 </summary>
        public void SetEndingListener(ISessionEndingListener listener)
        {
            _endingListener = listener;
        }

        /// <summary> 종료 사유가 확정되면 모든 사유에서 기록기를 닫는다, 참가자 명단을 닫기 전에 통지한다 </summary>
        public void SetCombatMeterSink(ICombatMeterSink sink)
        {
            _combatMeterSink = sink;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _roster.ParticipantClosed -= OnParticipantClosed;
            _wave?.SetListener(null);
            _wave?.Abort();
            _disposed = true;
        }

        private void OnParticipantClosed(int participantId, ESessionEndReason reason, double serverTime)
        {
            if ((!IsRunning) || (!_roster.TryGet(participantId, out ParticipantSnapshot participant)))
            {
                return;
            }

            _lastDepartureReason = reason;

            Save(SessionEventData.ParticipantClosed(serverTime, participant.ParticipantKey, reason, _isBossSession));
            Save(SessionEventData.ParticipantSettled(serverTime, participant.ParticipantKey, reason, _isBossSession));
        }

        private void Finish(ESessionEndReason reason)
        {
            if (IsEnded)
            {
                return;
            }

            // 아직 진행 중일 때 불러야 넣은 이벤트가 종료 이벤트보다 앞 순번이 된다. 저장이 멈췄으면 넣지 않는다
            if ((_endingListener != null) && (!_storageFault) && (_pendingCount == 0) && (!_events.IsTerminal))
            {
                _endingListener.OnSessionEnding(_serverTime);
            }

            _wave?.Abort();
            _revives?.MarkTerminal();
            _endReason = reason;
            IsEnded    = true;
            try
            {
                _combatMeterSink?.OnSessionClosed(reason, _serverTime);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            if ((reason == ESessionEndReason.Success) && (_isBossSession))
            {
                GrantBossReward();
            }

            // 전체 종료 정산은 SessionEnded 하나로 요청한다. 개인 이탈 정산을 다시 발행하지 않는다
            for (int i = 0; i < _roster.Count; ++i)
            {
                _roster.TryClose(_roster.GetAt(i).ParticipantId, reason, _serverTime);
            }

            Save(SessionEventData.SessionEnded(_serverTime, reason, _isBossSession, CreateSettlementGrants()));
        }

        // 종료 때 한 번만 만든다. API에 개인 이탈 정산이 아직 없어 원래 참가자 전원을 한 번씩 덮어야 받아 준다.
        // 먼저 이탈한 참가자는 명단에 남은 자기 이탈 원인(포기, 유예 만료)으로, 나머지는 출정 종료 원인으로 정산한다
        private SettlementGrant[] CreateSettlementGrants()
        {
            int count = _roster.Count;
            var participants = new SettlementParticipant[count];
            for (int i = 0; i < count; ++i)
            {
                ParticipantSnapshot snapshot = _roster.GetAt(i);
                string key = snapshot.ParticipantKey;
                string participantId = SessionContractIds.TryGetApiParticipantId(key, out string apiId) ? apiId : key;
                ESessionEndReason reason = (snapshot.ConnectionState == EParticipantConnectionState.Departed)
                    ? snapshot.CloseReason
                    : _endReason;
                participants[i] = new SettlementParticipant(participantId, reason);
            }

            string bossMaterialId = _hasBossRule ? _bossRule.MaterialItemId : null;
            SettlementGrant[] grants = SettlementRules.CreateGrants(participants, count, _ledger, bossMaterialId,
                out SettlementSanityReport report);
            WarnSettlementCorrections(in report);
            return grants;
        }

        // 보스 처치 순간 접속 중이거나 유예 안의 원래 참가자만 받는다. 사망 여부는 보지 않는다
        private void GrantBossReward()
        {
            if (!_hasBossRule)
            {
                return;
            }

            for (int i = 0; i < _roster.Count; ++i)
            {
                ParticipantSnapshot participant = _roster.GetAt(i);
                if (!_roster.IsRewardEligible(participant.ParticipantId, _serverTime))
                {
                    continue;
                }

                long materials = LootRoller.RollBossMaterial(_lootSalt, _sessionId, participant.ParticipantKey, in _bossRule);
                _ledger.SetBossReward(i, _bossRule.Essence, materials, _bossRule.TradeCurrency);
            }
        }

        // 원장이 받은 양만 적립 이벤트로 보낸다. 상한으로 잘린 양은 정산에서도 빠지므로 적립 합계와 정산 기준이 같다
        private void Accrue(int index, string participantKey, long amount, int waveIndex, long killSequence)
        {
            long accepted = _ledger.AccrueExtra(index, amount);
            SaveAccrual(SessionEventData.EssenceAccrued(_serverTime, participantKey, accepted, waveIndex, killSequence));
        }

        // 기본, 추가 정기 적립 이벤트. 양수만 보낸다. API가 0 이하 적립을 형식 오류로 배치 전체를 거절하기 때문이다
        private void SaveAccrual(in SessionEventData data)
        {
            if (data.Amount <= 0)
            {
                return;
            }

            Save(in data);
        }

        // 종료 때 한 번만 부른다. 정상 출정은 고칠 것이 없어 경고가 남지 않는다
        private void WarnSettlementCorrections(in SettlementSanityReport report)
        {
            int accrualClamped = 0;
            for (int i = 0; i < _roster.Count; ++i)
            {
                if (_ledger.WasAccrualClamped(i))
                {
                    ++accrualClamped;
                }
            }

            if ((!report.HasCorrections) && (accrualClamped == 0))
            {
                return;
            }

            Debug.LogWarning($"[Settlement] 정산안을 API 상한과 지급 금지 목록에 맞춰 고쳤다, session={_sessionId}, " +
                $"accrualClamped={accrualClamped}, essenceClamped={report.ClampedEssence}, " +
                $"forbiddenDropped={report.DroppedForbidden}, invalidIdDropped={report.DroppedInvalidId}, " +
                $"quantityClamped={report.ClampedQuantity}, overflowDropped={report.DroppedOverflow}, " +
                $"walletClamped={report.ClampedWallet}, talismanInvalidDropped={report.DroppedInvalidTalisman}, " +
                $"talismanClamped={report.ClampedTalisman}, talismanOverflowDropped={report.DroppedTalismanOverflow}");
        }

        private void GrantExperience(string participantKey, int sourceKind, string sourceId)
        {
            if (_isBossSession || (_experienceSettings == null))
            {
                return;
            }
            long amount = _experienceSettings.GetGrant(_experienceRegion, sourceKind);
            if (amount <= 0)
            {
                _storageFault = true;
                return;
            }
            Save(SessionEventData.ExperienceGranted(_serverTime, participantKey, sourceId, amount, sourceKind, _experienceHash));
            _events.RequestImmediateSend();
        }

        private void Save(in SessionEventData data)
        {
            // 확정 거절로 멈춘 큐에는 아무것도 저장되지 않는다. 보존 버퍼에 쌓으면 상한을 넘어 틱이 예외로 멈춘다
            if (_events.IsTerminal)
            {
                return;
            }

            if ((_pendingCount == 0) && (_events.TryEnqueue(in data, out long sequence)))
            {
                RecordSequence(in data, sequence);
                return;
            }

            // 저장이 멈춰도 확정한 드롭은 버리지 않는다. 이 틱 끝에 전투를 중단하고 큐가 비면 순서대로 저장한다
            _storageFault = true;
            if (_pendingCount == _pending.Length)
            {
                throw new InvalidOperationException("세션 종료 보존 버퍼 상한을 넘었다");
            }

            _pending[_pendingCount] = data;
            ++_pendingCount;
        }

        // 종료 이벤트와 개인 이탈의 저장 순번을 기억한다. 귀환 가능(저장 확인) 판단에 쓴다
        private void RecordSequence(in SessionEventData data, long sequence)
        {
            if (data.Type == ESessionEventType.SessionEnded)
            {
                _endEventSequence = sequence;
                return;
            }

            if (data.Type != ESessionEventType.ParticipantSettled)
            {
                return;
            }

            for (int i = 0; i < _roster.Count; ++i)
            {
                if (string.Equals(_roster.GetAt(i).ParticipantKey, data.ParticipantId, StringComparison.Ordinal))
                {
                    _departureSequences[i] = sequence;
                    return;
                }
            }
        }

        private void FlushPending()
        {
            int sent = 0;
            for (int i = 0; i < _pendingCount; ++i)
            {
                if (!_events.TryEnqueue(in _pending[i], out long sequence))
                {
                    break;
                }

                RecordSequence(in _pending[i], sequence);
                ++sent;
            }

            if (sent > 0)
            {
                Array.Copy(_pending, sent, _pending, 0, _pendingCount - sent);
                Array.Clear(_pending, _pendingCount - sent, sent);
                _pendingCount -= sent;
            }
        }

        private void DiscardPending()
        {
            if (_pendingCount == 0)
            {
                return;
            }

            Array.Clear(_pending, 0, _pendingCount);
            _pendingCount = 0;
        }

        // 보스전에서 참가 중(이탈 전)인 사람이 있는데 그중 살아 있는 사람이 없으면 전멸이다. 유예 중인 캐릭터도 서버에 남아
        // 피격되므로 살아 있으면 센다. 일반전은 부활이 무제한이라 전멸 실패가 없다
        private bool IsWiped()
        {
            return (_isBossSession) && (_roster.ActiveCount > 0) && (_roster.CountLiving() == 0);
        }

        private int IndexOfParticipant(int participantId)
        {
            for (int i = 0; i < _roster.Count; ++i)
            {
                if (_roster.GetAt(i).ParticipantId == participantId)
                {
                    return i;
                }
            }

            return -1;
        }

        private bool AdvanceTime(double serverTime)
        {
            if ((!IsValidTime(serverTime)) || (serverTime < _serverTime) || (serverTime < _roster.LastServerTime)
                || ((_inTick) && (serverTime != _serverTime)))
            {
                return false;
            }

            _serverTime = serverTime;
            _roster.Tick(serverTime);
            return true;
        }

        private static bool IsValidTime(double serverTime)
        {
            return (serverTime >= 0.0) && (!double.IsInfinity(serverTime));
        }
    }
}
