// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Combat;

namespace Shinshi.AI
{
    // 다른 복원 기록(BossEffectLedger, BossPhaseEffectLedger)과 같은 규칙이다: 받은 값을 바꾸지 않고 지금 진행 중인 것만 남기며 끝난 것은 바로 지운다.
    // 서버와 클라이언트가 같은 사건으로 같은 기록을 이어 가고, 새 관찰자에게는 스폰 초기 상태에 실어 보낸다.
    // 기믹은 종류마다 한 번에 하나라 종류마다 한 건만 둔다.
    //
    // 일회성 효과(공격 판정, 광역 판정, 거리 피해, 휩쓸기 판정, 빨간 폭발, 부활 창구)는 기록하지 않는다. 재접속에서 지난 효과를 다시 터뜨리지 않기 위해서다.
    // 판정은 상태(다음 공격 번호, 지금 차례의 파괴 핵, 노란 여우불 생존)에만 반영한다.
    //
    // 추적 중인 여우불 위치는 서버가 틱마다 바꾸지만 사건이 없어 기록하지 않는다. 떨어져 나간 것과 고정된 것만 위치가 있고 나머지는 NaN이다.
    // 기믹 상태(전환과 활성 같은)는 사건이 오는 순간만 안다. 부적진은 시작부터 유지나 공격 사건이 올 때까지 Transition으로 둔다.
    //
    // 지우는 시점: 부적진과 봉인은 반격 종료나 정리, 색 결계는 성공이나 정리, 최후의 영역은 성공이나 정리, 워프 출발은 그 시전의 종료나 중단이다
    /// <summary> 월백 체력 기믹 4종과 워프 출발의 진행 중 기록, 늦은 접속 복원에 쓴다 </summary>
    public sealed class BossWolbaekGimmickLedger : IBossWolbaekGimmickEventListener
    {
        /****************************************
        *              Constants
        ****************************************/

        private const double UNKNOWN_TIME = double.NaN;
        private const int    MASK_BITS    = 8;

        private static readonly Vector3 G_UnknownPosition = new Vector3(float.NaN, float.NaN, float.NaN);

        /****************************************
        *                Fields
        ****************************************/

        private BossWolbaekTalismanRestore _talisman;
        private bool                       _hasTalisman = false;

        private WolbaekWardStart  _wardStart;
        private EWolbaekWardState _wardState;
        private bool              _wardHasHold;
        private double            _wardHoldStart;
        private WolbaekWardStage  _wardStage;
        private int               _wardNextStrike;
        private double[]          _wardGraceEnds = Array.Empty<double>();
        private bool              _wardHasPending;
        private WolbaekWardStrike _wardPending;
        private bool              _hasWard = false;

        private WolbaekSealStart  _sealStart;
        private EWolbaekSealState _sealState;
        private EWolbaekSealStep  _sealStep;
        private WolbaekSealTurn   _sealTurn;
        private int               _sealSuccessCount;
        private Vector3[]         _sealPositions = Array.Empty<Vector3>();
        private bool[]            _sealDetached  = Array.Empty<bool>();
        private bool              _sealLocked;
        private bool              _sealHasSweep;
        private WolbaekSealSweep  _sealSweep;
        private double            _sealCounterEnd;
        private byte              _sealMask;
        private bool              _hasSeal = false;

        private WolbaekFinalStart    _finalStart;
        private EWolbaekFinalStep    _finalStep;
        private bool                 _finalFloorRemoved;
        private WolbaekFinalYellow[] _finalYellows = Array.Empty<WolbaekFinalYellow>();
        private float[]              _finalHealth  = Array.Empty<float>();
        private bool                 _finalHasBundle;
        private WolbaekFinalBundle   _finalBundle;
        private Vector3[]            _finalRedPositions = Array.Empty<Vector3>();
        private bool[]               _finalRedDetached  = Array.Empty<bool>();
        private bool                 _finalRedsLocked;
        private bool                 _hasFinal = false;

        private int     _warpCastId;
        private Vector3 _warpDeparture;
        private double  _warpTime;
        private bool    _hasWarp = false;

        /****************************************
        *              Properties
        ****************************************/

        public bool HasTalisman => _hasTalisman;
        public bool HasWard     => _hasWard;
        public bool HasSeal     => _hasSeal;
        public bool HasFinal    => _hasFinal;
        public bool HasWarp     => _hasWarp;

        /// <summary> 남은 기록이 하나도 없다 </summary>
        public bool IsEmpty => (!_hasTalisman) && (!_hasWard) && (!_hasSeal) && (!_hasFinal) && (!_hasWarp);

        public BossWolbaekTalismanRestore Talisman => _talisman;

        public BossWolbaekWardRestore Ward => CaptureWard();

        public BossWolbaekSealRestore Seal => CaptureSeal();

        public BossWolbaekFinalRestore Final => CaptureFinal();

        public int     WarpCastId     => _warpCastId;
        public Vector3 WarpDeparture  => _warpDeparture;
        public double  WarpServerTime => _warpTime;

        /****************************************
        *                Methods
        ****************************************/

        public void Clear()
        {
            ClearTalisman();
            ClearWard();
            ClearSeal();
            ClearFinal();
            ClearWarp();
        }

        // 기본 사건의 정상 종료(OnFinished)와 중단(OnCancelled)이 부른다. 워프 출발은 그 시전과 함께 끝난다
        /// <summary> 시전이 끝났다, 그 시전의 워프 출발 기록을 지운다 </summary>
        public void OnCastEnded(int castId)
        {
            if ((_hasWarp) && (_warpCastId == castId))
            {
                ClearWarp();
            }
        }

        /****************************************
        *            75% Talisman
        ****************************************/

        public void OnTalismanStarted(in WolbaekTalismanStart start)
        {
            if (!BossWolbaekNetSerializer.CanWrite(in start))
            {
                Debug.LogError($"[Boss] 부적진 구조물 수가 상한을 넘어 복원 기록에 남기지 않는다, castId={start.CastInstanceId}");
                return;
            }

            _talisman = new BossWolbaekTalismanRestore
            {
                Snapshot          = new WolbaekTalismanSnapshot(EWolbaekTalismanState.Transition, in start, false, 0d, false,
                    default, 0d),
                SuccessServerTime = UNKNOWN_TIME,
                HasLastReset      = false
            };
            _hasTalisman = true;
        }

        public void OnTalismanHoldStarted(int castId, double serverTime)
        {
            if (!IsTalisman(castId))
            {
                return;
            }

            WolbaekTalismanSnapshot s = _talisman.Snapshot;
            SetTalisman(EWolbaekTalismanState.Active, true, serverTime, s.HasAttack, s.Attack);
            _talisman.HasLastReset = false;
        }

        public void OnTalismanHoldReset(int castId, double serverTime, EWolbaekHoldReset reason, int combatId)
        {
            if (!IsTalisman(castId))
            {
                return;
            }

            WolbaekTalismanSnapshot s = _talisman.Snapshot;
            SetTalisman(EWolbaekTalismanState.Active, false, 0d, s.HasAttack, s.Attack);
            _talisman.HasLastReset        = true;
            _talisman.LastResetServerTime = serverTime;
            _talisman.LastResetReason     = reason;
            _talisman.LastResetCombatId   = combatId;
        }

        public void OnTalismanAttackTelegraphed(int castId, in WolbaekTalismanAttack attack)
        {
            if (!IsTalisman(castId))
            {
                return;
            }

            WolbaekTalismanSnapshot s = _talisman.Snapshot;
            SetTalisman(EWolbaekTalismanState.Active, s.HasHold, s.HoldStartServerTime, true, attack);
        }

        public void OnTalismanAttackStruck(int castId, int attackId, double strikeServerTime)
        {
            EndTalismanAttack(castId, attackId);
        }

        public void OnTalismanAttackCancelled(int castId, int attackId)
        {
            EndTalismanAttack(castId, attackId);
        }

        public void OnTalismanSucceeded(int castId, double serverTime, double counterEndServerTime)
        {
            if (!IsTalisman(castId))
            {
                return;
            }

            WolbaekTalismanStart start = _talisman.Snapshot.Start;
            _talisman.Snapshot = new WolbaekTalismanSnapshot(EWolbaekTalismanState.Counter, in start, false, 0d, false, default,
                counterEndServerTime);
            _talisman.SuccessServerTime = serverTime;
            _talisman.HasLastReset      = false;
        }

        public void OnTalismanCounterEnded(int castId)
        {
            if (IsTalisman(castId))
            {
                ClearTalisman();
            }
        }

        public void OnTalismanCleared(int castId)
        {
            if (IsTalisman(castId))
            {
                ClearTalisman();
            }
        }

        /****************************************
        *            50% Color Ward
        ****************************************/

        public void OnWardStarted(in WolbaekWardStart start)
        {
            if (!BossWolbaekNetSerializer.CanWrite(in start))
            {
                Debug.LogError($"[Boss] 색 결계 명단 수가 상한을 넘어 복원 기록에 남기지 않는다, castId={start.CastInstanceId}");
                return;
            }

            ClearWard();
            _wardStart     = start;
            _wardState     = EWolbaekWardState.Gather;
            _wardGraceEnds = new double[(start.Members != null) ? start.Members.Count : 0];
            _hasWard       = true;
        }

        public void OnGatherHoldStarted(int castId, double serverTime)
        {
            if ((!IsWard(castId)) || (_wardState != EWolbaekWardState.Gather))
            {
                return;
            }

            _wardHasHold   = true;
            _wardHoldStart = serverTime;
        }

        public void OnGatherHoldReset(int castId)
        {
            if (!IsWard(castId))
            {
                return;
            }

            _wardHasHold   = false;
            _wardHoldStart = 0d;
        }

        public void OnGatherCompleted(int castId)
        {
            OnGatherHoldReset(castId);
        }

        public void OnStageStarted(int castId, in WolbaekWardStage stage)
        {
            if (!IsWard(castId))
            {
                return;
            }

            _wardState      = EWolbaekWardState.Search;
            _wardHasHold    = false;
            _wardStage      = stage;
            _wardNextStrike = 0;
            _wardHasPending = false;
            _wardPending    = default;
        }

        public void OnStrikeTelegraphed(int castId, in WolbaekWardStrike strike)
        {
            if ((!IsWard(castId)) || (_wardState == EWolbaekWardState.Gather) || (strike.StageIndex != _wardStage.Index)
                || (strike.StrikeIndex < _wardNextStrike))
            {
                return;
            }

            _wardState      = EWolbaekWardState.Strike;
            _wardHasPending = true;
            _wardPending    = strike;
        }

        // 판정은 일회성이라 남기지 않는다. 다음 공격 번호만 올린다
        public void OnStrikeResolved(int castId, int stageIndex, int strikeIndex, double strikeServerTime)
        {
            if ((!IsWard(castId)) || (stageIndex != _wardStage.Index))
            {
                return;
            }

            _wardNextStrike = Mathf.Max(_wardNextStrike, strikeIndex + 1);
            if ((_wardHasPending) && (_wardPending.StrikeIndex <= strikeIndex))
            {
                _wardHasPending = false;
                _wardPending    = default;
            }
        }

        public void OnLinkDamaged(int castId, double serverTime)
        {
        }

        public void OnGraceStarted(int castId, int combatId, double graceEndServerTime)
        {
            SetGrace(castId, combatId, graceEndServerTime);
        }

        public void OnGraceEnded(int castId, int combatId)
        {
            SetGrace(castId, combatId, 0d);
        }

        public void OnReviveWindowOpened(int castId)
        {
        }

        public void OnWardSucceeded(int castId, ushort phaseGenerationAtSuccess)
        {
            if (IsWard(castId))
            {
                ClearWard();
            }
        }

        public void OnWardCleared(int castId)
        {
            if (IsWard(castId))
            {
                ClearWard();
            }
        }

        /****************************************
        *            25% Foxfire Seal
        ****************************************/

        public void OnSealStarted(in WolbaekSealStart start)
        {
            ClearSeal();
            _sealStart = start;
            _sealState = EWolbaekSealState.Active;
            _sealStep  = EWolbaekSealStep.Prepare;
            _hasSeal   = true;
        }

        public void OnSealTurnStarted(int castId, in WolbaekSealTurn turn)
        {
            if ((!IsSeal(castId)) || (_sealState != EWolbaekSealState.Active))
            {
                return;
            }

            if (!BossWolbaekNetSerializer.CanWrite(in turn))
            {
                Debug.LogError($"[Boss] 봉인 차례의 핵이나 여우불 수가 상한을 넘어 복원 기록에서 지운다, castId={castId}");
                ClearSeal();
                return;
            }

            int count = (turn.Foxfires != null) ? turn.Foxfires.Count : 0;
            _sealTurn      = turn;
            _sealStep      = EWolbaekSealStep.Prepare;
            _sealPositions = new Vector3[count];
            _sealDetached  = new bool[count];
            for (int i = 0; i < count; ++i)
            {
                _sealPositions[i] = G_UnknownPosition;
            }

            _sealLocked   = false;
            _sealHasSweep = false;
            _sealSweep    = default;
            _sealMask     = 0;
        }

        public void OnSealTrackingStarted(int castId, int turnIndex, double serverTime)
        {
            if (IsSealTurn(castId, turnIndex) && (_sealStep == EWolbaekSealStep.Prepare))
            {
                _sealStep = EWolbaekSealStep.Track;
            }
        }

        public void OnSealFoxfireDetached(int castId, int turnIndex, int foxfireIndex, double serverTime, Vector3 position)
        {
            if ((!IsSealTurn(castId, turnIndex)) || (_sealLocked) || (foxfireIndex < 0) || (foxfireIndex >= _sealPositions.Length))
            {
                return;
            }

            _sealDetached[foxfireIndex]  = true;
            _sealPositions[foxfireIndex] = position;
        }

        public void OnSealSweepTelegraphed(int castId, in WolbaekSealSweep sweep)
        {
            if (!IsSealTurn(castId, sweep.TurnIndex))
            {
                return;
            }

            _sealHasSweep = true;
            _sealSweep    = sweep;
        }

        public void OnSealSweepResolved(int castId, int turnIndex, int sweepIndex, double serverTime)
        {
            if ((IsSealTurn(castId, turnIndex)) && (_sealHasSweep) && (_sealSweep.SweepIndex <= sweepIndex))
            {
                _sealHasSweep = false;
                _sealSweep    = default;
            }
        }

        public void OnSealFoxfiresLocked(int castId, int turnIndex, double serverTime, Vector3[] centers)
        {
            if ((!IsSealTurn(castId, turnIndex)) || (centers == null) || (centers.Length != _sealPositions.Length))
            {
                return;
            }

            Array.Copy(centers, _sealPositions, centers.Length);
            _sealLocked = true;
            _sealStep   = EWolbaekSealStep.Escape;
        }

        public void OnSealTurnResolved(int castId, int turnIndex, double serverTime, int successCount, byte destroyedCoreMask)
        {
            if (!IsSealTurn(castId, turnIndex))
            {
                return;
            }

            _sealStep         = EWolbaekSealStep.Cleanup;
            _sealSuccessCount = Mathf.Max(_sealSuccessCount, successCount);
            _sealMask         = destroyedCoreMask;
            _sealHasSweep     = false;
            _sealSweep        = default;
        }

        public void OnSealSucceeded(int castId, double serverTime, double counterEndServerTime)
        {
            if (!IsSeal(castId))
            {
                return;
            }

            _sealState      = EWolbaekSealState.Counter;
            _sealCounterEnd = counterEndServerTime;
            _sealHasSweep   = false;
            _sealSweep      = default;
        }

        public void OnSealCounterEnded(int castId)
        {
            if (IsSeal(castId))
            {
                ClearSeal();
            }
        }

        public void OnSealCleared(int castId)
        {
            if (IsSeal(castId))
            {
                ClearSeal();
            }
        }

        /****************************************
        *          Hard 0% Final Area
        ****************************************/

        public void OnFinalStarted(in WolbaekFinalStart start)
        {
            ClearFinal();
            _finalStart = start;
            _finalStep  = EWolbaekFinalStep.Collapse;
            _hasFinal   = true;
        }

        public void OnFinalOuterFloorRemoved(int castId, double serverTime)
        {
            if (!IsFinal(castId))
            {
                return;
            }

            _finalFloorRemoved = true;
            _finalStep         = EWolbaekFinalStep.Hunt;
        }

        public void OnFinalYellowsSpawned(int castId, WolbaekFinalYellow[] yellows)
        {
            if ((!IsFinal(castId)) || (!BossWolbaekNetSerializer.CanWrite(yellows)))
            {
                return;
            }

            _finalYellows = (WolbaekFinalYellow[])yellows.Clone();
            _finalHealth  = new float[yellows.Length];
            for (int i = 0; i < yellows.Length; ++i)
            {
                _finalHealth[i] = yellows[i].MaxHealth;
            }
        }

        public void OnFinalYellowDestroyed(int castId, int yellowIndex, double serverTime)
        {
            if ((IsFinal(castId)) && (yellowIndex >= 0) && (yellowIndex < _finalHealth.Length))
            {
                _finalHealth[yellowIndex] = 0f;
            }
        }

        public void OnFinalBundleStarted(int castId, in WolbaekFinalBundle bundle)
        {
            if ((!IsFinal(castId)) || (!BossWolbaekNetSerializer.CanWrite(in bundle)))
            {
                return;
            }

            int count = (bundle.Reds != null) ? bundle.Reds.Count : 0;
            _finalHasBundle    = true;
            _finalBundle       = bundle;
            _finalRedPositions = new Vector3[count];
            _finalRedDetached  = new bool[count];
            _finalRedsLocked   = false;
            for (int i = 0; i < count; ++i)
            {
                _finalRedPositions[i] = G_UnknownPosition;
            }
        }

        public void OnFinalRedDetached(int castId, int bundleIndex, int redIndex, double serverTime, Vector3 position)
        {
            if ((!IsBundle(castId, bundleIndex)) || (_finalRedsLocked) || (redIndex < 0) || (redIndex >= _finalRedPositions.Length))
            {
                return;
            }

            _finalRedDetached[redIndex]  = true;
            _finalRedPositions[redIndex] = position;
        }

        public void OnFinalRedsLocked(int castId, int bundleIndex, double serverTime, Vector3[] centers)
        {
            if ((!IsBundle(castId, bundleIndex)) || (centers == null) || (centers.Length != _finalRedPositions.Length))
            {
                return;
            }

            Array.Copy(centers, _finalRedPositions, centers.Length);
            _finalRedsLocked = true;
        }

        // 폭발은 일회성이라 묶음 기록만 지운다. 다음 묶음은 새 시작 사건으로 온다
        public void OnFinalBundleResolved(int castId, int bundleIndex, double serverTime)
        {
            if (IsBundle(castId, bundleIndex))
            {
                ClearBundle();
            }
        }

        public void OnFinalSucceeded(int castId, double serverTime)
        {
            if (IsFinal(castId))
            {
                ClearFinal();
            }
        }

        public void OnFinalCleared(int castId)
        {
            if (IsFinal(castId))
            {
                ClearFinal();
            }
        }

        /****************************************
        *               P07 Warp
        ****************************************/

        public void OnWarpDeparted(int castId, Vector3 departure, double serverTime)
        {
            _warpCastId    = castId;
            _warpDeparture = departure;
            _warpTime      = serverTime;
            _hasWarp       = true;
        }

        /****************************************
        *           Replay / Serialize
        ****************************************/

        // 목록은 기록의 배열을 복사해 넘긴다. 늦은 접속과 수신부 연결 때만 불리므로 할당을 받아들인다
        /// <summary> 진행 중인 것을 모두 복원으로 넘긴다 </summary>
        public void Replay(IBossWolbaekGimmickStateListener listener)
        {
            if (listener == null)
            {
                return;
            }

            if (_hasTalisman)
            {
                listener.OnTalismanRestored(in _talisman);
            }

            if (_hasWard)
            {
                BossWolbaekWardRestore ward = CaptureWard(true);
                listener.OnWardRestored(in ward);
            }

            if (_hasSeal)
            {
                BossWolbaekSealRestore seal = CaptureSeal(true);
                listener.OnSealRestored(in seal);
            }

            if (_hasFinal)
            {
                BossWolbaekFinalRestore final = CaptureFinal(true);
                listener.OnFinalRestored(in final);
            }

            if (_hasWarp)
            {
                listener.OnWarpRestored(_warpCastId, _warpDeparture, _warpTime);
            }
        }

        // 순서를 바꾸면 Read도 함께 바꾼다
        public void Write(NetworkWriter writer)
        {
            writer.WriteBool(_hasTalisman);
            if (_hasTalisman)
            {
                writer.WriteBossWolbaekTalismanRestore(_talisman);
            }

            writer.WriteBool(_hasWard);
            if (_hasWard)
            {
                writer.WriteBossWolbaekWardRestore(CaptureWard());
            }

            writer.WriteBool(_hasSeal);
            if (_hasSeal)
            {
                writer.WriteBossWolbaekSealRestore(CaptureSeal());
            }

            writer.WriteBool(_hasFinal);
            if (_hasFinal)
            {
                writer.WriteBossWolbaekFinalRestore(CaptureFinal());
            }

            writer.WriteBool(_hasWarp);
            if (_hasWarp)
            {
                writer.WriteInt(_warpCastId);
                writer.WriteVector3(_warpDeparture);
                writer.WriteDouble(_warpTime);
            }
        }

        // 기존 기록을 버리고 받은 값으로 바꾼다. 상한과 열거형 범위는 직렬화가 검사한다
        public void Read(NetworkReader reader)
        {
            Clear();

            if (reader.ReadBool())
            {
                _talisman    = reader.ReadBossWolbaekTalismanRestore();
                _hasTalisman = true;
            }

            if (reader.ReadBool())
            {
                ApplyWard(reader.ReadBossWolbaekWardRestore());
            }

            if (reader.ReadBool())
            {
                ApplySeal(reader.ReadBossWolbaekSealRestore());
            }

            if (reader.ReadBool())
            {
                ApplyFinal(reader.ReadBossWolbaekFinalRestore());
            }

            if (reader.ReadBool())
            {
                int     castId    = reader.ReadInt();
                Vector3 departure = reader.ReadVector3();
                double  time      = reader.ReadDouble();
                OnWarpDeparted(castId, departure, time);
            }
        }

        /****************************************
        *               Capture
        ****************************************/

        private BossWolbaekWardRestore CaptureWard(bool copyLists = false)
        {
            if (!_hasWard)
            {
                return default;
            }

            double[] graces = copyLists ? (double[])_wardGraceEnds.Clone() : _wardGraceEnds;
            return new BossWolbaekWardRestore
            {
                Snapshot         = new WolbaekWardSnapshot(_wardState, in _wardStart, _wardHasHold, _wardHoldStart, in _wardStage,
                    _wardNextStrike, graces),
                HasPendingStrike = _wardHasPending,
                PendingStrike    = _wardPending
            };
        }

        private BossWolbaekSealRestore CaptureSeal(bool copyLists = false)
        {
            if (!_hasSeal)
            {
                return default;
            }

            Vector3[] positions = copyLists ? (Vector3[])_sealPositions.Clone() : _sealPositions;
            bool[]    detached  = copyLists ? (bool[])_sealDetached.Clone() : _sealDetached;
            double    counter   = (_sealState == EWolbaekSealState.Counter) ? _sealCounterEnd : 0d;
            return new BossWolbaekSealRestore
            {
                Snapshot          = new WolbaekSealSnapshot(_sealState, _sealStep, in _sealStart, in _sealTurn, _sealSuccessCount,
                    positions, detached, _sealLocked, _sealHasSweep, in _sealSweep, counter),
                DestroyedCoreMask = _sealMask
            };
        }

        private BossWolbaekFinalRestore CaptureFinal(bool copyLists = false)
        {
            if (!_hasFinal)
            {
                return default;
            }

            WolbaekFinalYellow[] yellows   = copyLists ? (WolbaekFinalYellow[])_finalYellows.Clone() : _finalYellows;
            float[]              health    = copyLists ? (float[])_finalHealth.Clone() : _finalHealth;
            Vector3[]            positions = copyLists ? (Vector3[])_finalRedPositions.Clone() : _finalRedPositions;
            bool[]               detached  = copyLists ? (bool[])_finalRedDetached.Clone() : _finalRedDetached;
            return new BossWolbaekFinalRestore
            {
                Snapshot = new WolbaekFinalSnapshot(_finalStep, in _finalStart, _finalFloorRemoved, yellows, health,
                    _finalHasBundle, in _finalBundle, positions, detached, _finalRedsLocked)
            };
        }

        private void ApplyWard(BossWolbaekWardRestore restore)
        {
            WolbaekWardSnapshot snapshot = restore.Snapshot;
            _wardStart      = snapshot.Start;
            _wardState      = snapshot.State;
            _wardHasHold    = snapshot.HasHold;
            _wardHoldStart  = snapshot.HoldStartServerTime;
            _wardStage      = snapshot.Stage;
            _wardNextStrike = snapshot.NextStrikeIndex;
            _wardGraceEnds  = ToArray(snapshot.GraceEndServerTimes);
            _wardHasPending = restore.HasPendingStrike;
            _wardPending    = restore.PendingStrike;
            _hasWard        = true;
        }

        private void ApplySeal(BossWolbaekSealRestore restore)
        {
            WolbaekSealSnapshot snapshot = restore.Snapshot;
            _sealStart        = snapshot.Start;
            _sealState        = snapshot.State;
            _sealStep         = snapshot.Step;
            _sealTurn         = snapshot.Turn;
            _sealSuccessCount = snapshot.SuccessCount;
            _sealPositions    = ToArray(snapshot.FoxfirePositions);
            _sealDetached     = ToArray(snapshot.FoxfireDetached);
            _sealLocked       = snapshot.FoxfiresLocked;
            _sealHasSweep     = snapshot.HasPendingSweep;
            _sealSweep        = snapshot.PendingSweep;
            _sealCounterEnd   = snapshot.CounterEndServerTime;
            _sealMask         = restore.DestroyedCoreMask;
            _hasSeal          = true;
        }

        private void ApplyFinal(BossWolbaekFinalRestore restore)
        {
            WolbaekFinalSnapshot snapshot = restore.Snapshot;
            _finalStart        = snapshot.Start;
            _finalStep         = snapshot.Step;
            _finalFloorRemoved = snapshot.OuterFloorRemoved;
            _finalYellows      = ToArray(snapshot.Yellows);
            _finalHealth       = ToArray(snapshot.YellowHealth);
            _finalHasBundle    = snapshot.HasBundle;
            _finalBundle       = snapshot.Bundle;
            _finalRedPositions = ToArray(snapshot.RedPositions);
            _finalRedDetached  = ToArray(snapshot.RedDetached);
            _finalRedsLocked   = snapshot.RedsLocked;
            _hasFinal          = true;
        }

        /****************************************
        *               Shared
        ****************************************/

        /// <summary> 지금 차례에서 index번 핵이 파괴됐는가, 판정 전이면 거짓이다 </summary>
        public static bool IsCoreDestroyed(byte mask, int index)
        {
            return (index >= 0) && (index < MASK_BITS) && ((mask & (1 << index)) != 0);
        }

        private void SetTalisman(EWolbaekTalismanState state, bool hasHold, double holdStart, bool hasAttack,
            in WolbaekTalismanAttack attack)
        {
            WolbaekTalismanStart start = _talisman.Snapshot.Start;
            _talisman.Snapshot = new WolbaekTalismanSnapshot(state, in start, hasHold, holdStart, hasAttack, in attack, 0d);
        }

        private void EndTalismanAttack(int castId, int attackId)
        {
            if ((!IsTalisman(castId)) || (!_talisman.Snapshot.HasAttack) || (_talisman.Snapshot.Attack.AttackId != attackId))
            {
                return;
            }

            WolbaekTalismanSnapshot s = _talisman.Snapshot;
            SetTalisman(s.State, s.HasHold, s.HoldStartServerTime, false, default);
        }

        private void SetGrace(int castId, int combatId, double end)
        {
            if ((!IsWard(castId)) || (_wardStart.Members == null))
            {
                return;
            }

            for (int i = 0; (i < _wardStart.Members.Count) && (i < _wardGraceEnds.Length); ++i)
            {
                if (_wardStart.Members[i].CombatId == combatId)
                {
                    _wardGraceEnds[i] = end;
                    return;
                }
            }
        }

        // 반격 중인 부적진은 사건이 더 오지 않아 시전 번호만 본다
        private bool IsTalisman(int castId)
        {
            return (_hasTalisman) && (_talisman.Snapshot.Start.CastInstanceId == castId);
        }

        private bool IsWard(int castId)
        {
            return (_hasWard) && (_wardStart.CastInstanceId == castId);
        }

        private bool IsSeal(int castId)
        {
            return (_hasSeal) && (_sealStart.CastInstanceId == castId);
        }

        private bool IsSealTurn(int castId, int turnIndex)
        {
            return (IsSeal(castId)) && (_sealState == EWolbaekSealState.Active) && (_sealTurn.Index == turnIndex)
                && (_sealTurn.Foxfires != null);
        }

        private bool IsFinal(int castId)
        {
            return (_hasFinal) && (_finalStart.CastInstanceId == castId);
        }

        private bool IsBundle(int castId, int bundleIndex)
        {
            return (IsFinal(castId)) && (_finalHasBundle) && (_finalBundle.Index == bundleIndex);
        }

        private void ClearTalisman()
        {
            _hasTalisman = false;
            _talisman    = default;
        }

        private void ClearWard()
        {
            _hasWard        = false;
            _wardStart      = default;
            _wardState      = EWolbaekWardState.Dormant;
            _wardHasHold    = false;
            _wardHoldStart  = 0d;
            _wardStage      = default;
            _wardNextStrike = 0;
            _wardGraceEnds  = Array.Empty<double>();
            _wardHasPending = false;
            _wardPending    = default;
        }

        private void ClearSeal()
        {
            _hasSeal          = false;
            _sealStart        = default;
            _sealState        = EWolbaekSealState.Dormant;
            _sealStep         = EWolbaekSealStep.Prepare;
            _sealTurn         = default;
            _sealSuccessCount = 0;
            _sealPositions    = Array.Empty<Vector3>();
            _sealDetached     = Array.Empty<bool>();
            _sealLocked       = false;
            _sealHasSweep     = false;
            _sealSweep        = default;
            _sealCounterEnd   = 0d;
            _sealMask         = 0;
        }

        private void ClearFinal()
        {
            _hasFinal          = false;
            _finalStart        = default;
            _finalStep         = EWolbaekFinalStep.Collapse;
            _finalFloorRemoved = false;
            _finalYellows      = Array.Empty<WolbaekFinalYellow>();
            _finalHealth       = Array.Empty<float>();
            ClearBundle();
        }

        private void ClearBundle()
        {
            _finalHasBundle    = false;
            _finalBundle       = default;
            _finalRedPositions = Array.Empty<Vector3>();
            _finalRedDetached  = Array.Empty<bool>();
            _finalRedsLocked   = false;
        }

        private void ClearWarp()
        {
            _hasWarp       = false;
            _warpCastId    = CombatIds.NONE;
            _warpDeparture = default;
            _warpTime      = 0d;
        }

        private static T[] ToArray<T>(IReadOnlyList<T> values)
        {
            if ((values == null) || (values.Count == 0))
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
