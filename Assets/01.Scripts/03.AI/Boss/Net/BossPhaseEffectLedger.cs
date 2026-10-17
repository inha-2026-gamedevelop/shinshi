// System
using System.Collections.Generic;
using System.IO;

// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.AI
{
    // 기본 기록(BossEffectLedger)과 같은 규칙이다: 받은 값을 바꾸지 않고 지금 진행 중인 것만 남기며, 끝난 것은 바로 지운다.
    // 서버와 클라이언트가 같은 사건으로 같은 기록을 이어 가고, 새 관찰자에게는 스폰 초기 상태에 실어 보낸다.
    // 보스는 한 번에 한 시전만 하므로 종류마다 한 건만 둔다. 추적 장판만 한 시전이 여럿을 깔 수 있어 목록이다.
    // 지우는 시점은 종류마다 다르다:
    //  - 타 목록, 연속 돌진, 전장 공격, 돌진 결과, 별 단계, 격자, 난사, 동시 폭발: 시전 종료(OnCastEnded)나 중단
    //  - 잔류 부채꼴: 위험 종료나 중단. 잔류 위험이 시전의 회복보다 길 수 있어 시전 종료로는 지우지 않는다
    //  - 추적 장판: 같은 번호의 장판 활성(OnAreaActivated)이나 활성 전 취소. 예고가 시전보다 오래 남는다
    //  - 돌진 유도 그로기: 그로기 취소나 다음 시전의 시작(OnCastStarted). 그로기는 시전 회복 뒤에 시작해 시전 종료로는 지우지 않고,
    //    끝은 시각으로 정해져 끝 사건이 없다. 그로기 중에는 서버가 새 선택을 막으므로 다음 시전이 시작됐으면 그로기는 끝났다
    // 판정은 하지 않는다. 돌진 정지 거리만 표현 계약(스냅샷)의 정의대로 환산한다
    /// <summary> 무철과 월백 하드 2페이즈 전용 공격의 진행 중 기록, 늦은 접속 복원에 쓴다 </summary>
    public sealed class BossPhaseEffectLedger : IBossMucheolEventListener, IBossHardPhaseEventListener
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float NOT_STOPPED = -1f;

        /****************************************
        *                Fields
        ****************************************/

        private readonly List<BossTrackingAreaSnapshot> _trackingAreas =
            new List<BossTrackingAreaSnapshot>(Constants.Network.BOSS_RESTORE_MAX_TRACKING_AREAS);
        private readonly List<BossBarrageShot>          _barrageShots  =
            new List<BossBarrageShot>(Constants.Network.BOSS_BARRAGE_MAX_SHOTS);

        private BossStrikeChainSnapshot _strikeChain;
        private bool                    _hasStrikeChain = false;
        private BossMultiDashSnapshot   _multiDash;
        private bool                    _hasMultiDash   = false;
        private BossBattlefieldSnapshot _battlefield;
        private bool                    _hasBattlefield = false;
        private BossChargeOutcome       _charge;
        private bool                    _hasCharge      = false;
        private BossChargeGroggy        _groggy;
        private bool                    _hasGroggy      = false;
        private BossStarStageSnapshot   _starStage;
        private bool                    _hasStarStage   = false;
        private BossGridBeamSnapshot    _gridBeam;
        private bool                    _hasGridBeam    = false;
        private BossChainFanSnapshot    _chainFan;
        private bool                    _hasChainFan    = false;
        private BossBarrageStart        _barrage;
        private int                     _barrageFired;
        private bool                    _hasBarrage     = false;
        private BossCircleBurstSnapshot _circleBurst;
        private bool                    _hasCircleBurst = false;

        /****************************************
        *              Properties
        ****************************************/

        public bool HasStrikeChain => _hasStrikeChain;

        public BossStrikeChainSnapshot StrikeChain => _strikeChain;

        public bool HasMultiDash => _hasMultiDash;

        public BossMultiDashSnapshot MultiDash => _multiDash;

        public bool HasBattlefield => _hasBattlefield;

        public BossBattlefieldSnapshot Battlefield => _battlefield;

        public bool HasCharge => _hasCharge;

        public BossChargeOutcome Charge => _charge;

        /// <summary> 대기 중이거나 진행 중인 돌진 유도 그로기가 있다, 끝 시각이 지났어도 다음 시전 전까지 남는다 </summary>
        public bool HasGroggy => _hasGroggy;

        public BossChargeGroggy Groggy => _groggy;

        public bool HasStarStage => _hasStarStage;

        public BossStarStageSnapshot StarStage => _starStage;

        public bool HasGridBeam => _hasGridBeam;

        public BossGridBeamSnapshot GridBeam => _gridBeam;

        public bool HasChainFan => _hasChainFan;

        public BossChainFanSnapshot ChainFan => _chainFan;

        /// <summary> 활성 전인 추적 장판 수 </summary>
        public int TrackingAreaCount => _trackingAreas.Count;

        public bool HasBarrage => _hasBarrage;

        /// <summary> 진행 중인 난사에서 지금까지 예고한 발 수 </summary>
        public int BarrageShotCount => _barrageShots.Count;

        /// <summary> 진행 중인 난사에서 발사 처리가 끝난 발 수 </summary>
        public int BarrageFiredCount => _barrageFired;

        public bool HasCircleBurst => _hasCircleBurst;

        public BossCircleBurstSnapshot CircleBurst => _circleBurst;

        /// <summary> 남은 기록이 하나도 없다 </summary>
        public bool IsEmpty => (!_hasStrikeChain) && (!_hasMultiDash) && (!_hasBattlefield) && (!_hasCharge) && (!_hasGroggy)
            && (!_hasStarStage)
            && (!_hasGridBeam) && (!_hasChainFan) && (!_hasBarrage) && (!_hasCircleBurst) && (_trackingAreas.Count == 0);

        /****************************************
        *                Methods
        ****************************************/

        public BossTrackingAreaSnapshot GetTrackingArea(int index)
        {
            return _trackingAreas[index];
        }

        public void Clear()
        {
            _hasStrikeChain = false;
            _strikeChain    = default;
            _hasMultiDash   = false;
            _multiDash      = default;
            _hasBattlefield = false;
            _battlefield    = default;
            _hasCharge      = false;
            _charge         = default;
            _hasGroggy      = false;
            _groggy         = default;
            _hasStarStage   = false;
            _starStage      = default;
            _hasGridBeam    = false;
            _gridBeam       = default;
            _hasChainFan    = false;
            _chainFan       = default;
            _hasCircleBurst = false;
            _circleBurst    = default;
            _trackingAreas.Clear();
            ClearBarrage();
        }

        // 기본 사건의 정상 종료(OnFinished)와 중단(OnCancelled)이 부른다. 돌진 결과처럼 중단 사건이 따로 없는 기록도 여기서 지워진다
        /// <summary> 시전이 끝났다, 그 시전에 묶인 기록을 지운다. 잔류 부채꼴과 추적 장판은 남긴다 </summary>
        public void OnCastEnded(int castId)
        {
            if ((_hasStrikeChain) && (_strikeChain.Start.CastId == castId))
            {
                _hasStrikeChain = false;
                _strikeChain    = default;
            }

            if ((_hasMultiDash) && (_multiDash.Start.CastId == castId))
            {
                _hasMultiDash = false;
                _multiDash    = default;
            }

            if ((_hasBattlefield) && (_battlefield.Start.CastId == castId))
            {
                _hasBattlefield = false;
                _battlefield    = default;
            }

            if ((_hasCharge) && (_charge.CastId == castId))
            {
                _hasCharge = false;
                _charge    = default;
            }

            if ((_hasStarStage) && (_starStage.Start.CastId == castId))
            {
                _hasStarStage = false;
                _starStage    = default;
            }

            if ((_hasGridBeam) && (_gridBeam.Start.CastId == castId))
            {
                _hasGridBeam = false;
                _gridBeam    = default;
            }

            if ((_hasBarrage) && (_barrage.CastId == castId))
            {
                ClearBarrage();
            }

            if ((_hasCircleBurst) && (_circleBurst.Start.CastId == castId))
            {
                _hasCircleBurst = false;
                _circleBurst    = default;
            }
        }

        /// <summary> 새 시전이 시작됐다, 다른 시전의 그로기 기록은 끝난 것이라 지운다 </summary>
        public void OnCastStarted(int castId)
        {
            if ((_hasGroggy) && (_groggy.CastId != castId))
            {
                _hasGroggy = false;
                _groggy    = default;
            }
        }

        /// <summary> 같은 번호의 장판이 활성됐다, 이동 예고는 끝났고 장판은 기본 기록이 이어 맡는다 </summary>
        public void OnAreaActivated(int areaId)
        {
            RemoveTrackingArea(areaId);
        }

        /****************************************
        *               Mucheol
        ****************************************/

        public void OnStrikeChainStarted(in BossStrikeChainStart start)
        {
            OnCastStarted(start.CastId);
            _strikeChain = new BossStrikeChainSnapshot
            {
                Start             = start,
                Direction         = start.Direction,
                IsDirectionLocked = false,
                StruckCount       = 0
            };
            _hasStrikeChain = true;
        }

        public void OnStrikeChainAimChanged(EBossStrikeChainKind kind, int castId, Vector3 direction, double serverTime)
        {
            if ((!IsCurrentStrikeChain(castId)) || (_strikeChain.IsDirectionLocked))
            {
                return;
            }

            _strikeChain.Direction = direction;
        }

        public void OnStrikeChainDirectionLocked(EBossStrikeChainKind kind, int castId, Vector3 direction)
        {
            if (!IsCurrentStrikeChain(castId))
            {
                return;
            }

            _strikeChain.Direction         = direction;
            _strikeChain.IsDirectionLocked = true;
        }

        // 타는 순서대로 확정되므로 번호 + 1이 확정된 타 수다. 순서가 뒤바뀌어 와도 줄이지 않는다
        public void OnStrikeChainHit(EBossStrikeChainKind kind, int castId, int stepIndex, double serverTime)
        {
            if (!IsCurrentStrikeChain(castId))
            {
                return;
            }

            _strikeChain.StruckCount       = Mathf.Max(_strikeChain.StruckCount, stepIndex + 1);
            _strikeChain.IsDirectionLocked = true;
        }

        public void OnStrikeChainCancelled(EBossStrikeChainKind kind, int castId)
        {
            if (!IsCurrentStrikeChain(castId))
            {
                return;
            }

            _hasStrikeChain = false;
            _strikeChain    = default;
        }

        public void OnMultiDashStarted(in BossMultiDashStart start)
        {
            OnCastStarted(start.CastId);
            _multiDash = new BossMultiDashSnapshot
            {
                Start                    = start,
                DashIndex                = 0,
                Origin                   = start.Origin,
                Direction                = start.Direction,
                Length                   = start.Length,
                Width                    = start.Width,
                IsDirectionLocked        = false,
                TelegraphStartServerTime = start.StartServerTime,
                TelegraphSeconds         = start.TelegraphSeconds,
                HasDashStarted           = false,
                StoppedDistance          = NOT_STOPPED,
                StoppedServerTime        = double.NaN
            };
            _hasMultiDash = true;
        }

        public void OnMultiDashAimChanged(int castId, int dashIndex, Vector3 direction, float length, double serverTime)
        {
            if ((!IsCurrentDash(castId, dashIndex)) || (_multiDash.IsDirectionLocked) || (_multiDash.HasDashStarted))
            {
                return;
            }

            _multiDash.Direction = direction;
            _multiDash.Length    = length;
        }

        public void OnMultiDashDirectionLocked(int castId, int dashIndex, Vector3 direction, float length)
        {
            if (!IsCurrentDash(castId, dashIndex))
            {
                return;
            }

            _multiDash.Direction         = direction;
            _multiDash.Length            = length;
            _multiDash.IsDirectionLocked = true;
        }

        // 재조준이 오면 앞 돌진은 끝난 것이다. 지금 돌진 값을 통째로 새 돌진으로 바꾼다
        public void OnMultiDashReaimStarted(in BossMultiDashReaim reaim)
        {
            if ((!_hasMultiDash) || (_multiDash.Start.CastId != reaim.CastId))
            {
                return;
            }

            _multiDash.DashIndex                = reaim.DashIndex;
            _multiDash.Origin                   = reaim.Origin;
            _multiDash.Direction                = reaim.Direction;
            _multiDash.Length                   = reaim.Length;
            _multiDash.Width                    = reaim.Width;
            _multiDash.IsDirectionLocked        = false;
            _multiDash.TelegraphStartServerTime = reaim.StartServerTime;
            _multiDash.TelegraphSeconds         = reaim.ReaimSeconds;
            _multiDash.HasDashStarted           = false;
            _multiDash.DashStartServerTime      = 0.0;
            _multiDash.DashDistance             = 0f;
            _multiDash.DashSeconds              = 0f;
            _multiDash.DashSpeed                = 0f;
            _multiDash.StoppedDistance          = NOT_STOPPED;
            _multiDash.StoppedByWall            = false;
            _multiDash.StoppedServerTime        = double.NaN;
        }

        public void OnMultiDashDashStarted(int castId, int dashIndex, float distance, float seconds, float speed,
            double startServerTime)
        {
            if ((!IsCurrentDash(castId, dashIndex)) || (_multiDash.HasDashStarted))
            {
                return;
            }

            _multiDash.HasDashStarted      = true;
            _multiDash.IsDirectionLocked   = true;
            _multiDash.DashStartServerTime = startServerTime;
            _multiDash.DashDistance        = distance;
            _multiDash.DashSeconds         = seconds;
            _multiDash.DashSpeed           = speed;
            _multiDash.StoppedDistance     = NOT_STOPPED;
            _multiDash.StoppedByWall       = false;
            _multiDash.StoppedServerTime   = double.NaN;
        }

        // 정지 거리는 그 돌진의 시작 위치에서 고정 방향으로 잰 거리다. 기본 기록의 돌진 정지와 같은 정의다
        public void OnMultiDashDashStopped(int castId, int dashIndex, Vector3 position, bool stoppedByWall, double stopServerTime)
        {
            if ((!IsCurrentDash(castId, dashIndex)) || (!_multiDash.HasDashStarted))
            {
                return;
            }

            Vector3 offset    = position - _multiDash.Origin;
            Vector3 direction = _multiDash.Direction;
            float   along     = (offset.x * direction.x) + (offset.z * direction.z);

            _multiDash.StoppedDistance   = Mathf.Clamp(along, 0f, _multiDash.DashDistance);
            _multiDash.StoppedByWall     = stoppedByWall;
            _multiDash.StoppedServerTime = stopServerTime;
        }

        public void OnMultiDashTruncated(int castId, int plannedDashCount, int completedDashCount)
        {
            if ((!_hasMultiDash) || (_multiDash.Start.CastId != castId))
            {
                return;
            }

            _multiDash.IsTruncated = true;
        }

        public void OnMultiDashCancelled(int castId)
        {
            if ((!_hasMultiDash) || (_multiDash.Start.CastId != castId))
            {
                return;
            }

            _hasMultiDash = false;
            _multiDash    = default;
        }

        public void OnBattlefieldStarted(in BossBattlefieldStart start)
        {
            OnCastStarted(start.CastId);
            _battlefield = new BossBattlefieldSnapshot
            {
                Start    = start,
                IsStruck = false
            };
            _hasBattlefield = true;
        }

        public void OnBattlefieldHit(int castId, double serverTime)
        {
            if ((!_hasBattlefield) || (_battlefield.Start.CastId != castId) || (_battlefield.IsStruck))
            {
                return;
            }

            _battlefield.IsStruck         = true;
            _battlefield.StruckServerTime = serverTime;
        }

        public void OnBattlefieldCancelled(int castId)
        {
            if ((!_hasBattlefield) || (_battlefield.Start.CastId != castId))
            {
                return;
            }

            _hasBattlefield = false;
            _battlefield    = default;
        }

        public void OnChargeEnded(in BossChargeOutcome outcome)
        {
            _charge    = outcome;
            _hasCharge = true;
        }

        // 대기와 시작은 같은 그로기의 앞뒤 값이라 늘 덮어쓴다. 보스는 한 번에 한 그로기만 한다
        public void OnChargeGroggyPending(in BossChargeGroggy groggy)
        {
            _groggy    = groggy;
            _hasGroggy = true;
        }

        public void OnChargeGroggyStarted(in BossChargeGroggy groggy)
        {
            _groggy    = groggy;
            _hasGroggy = true;
        }

        public void OnChargeGroggyCancelled(int castId)
        {
            if ((!_hasGroggy) || (_groggy.CastId != castId))
            {
                return;
            }

            _hasGroggy = false;
            _groggy    = default;
        }

        // 구조물 파괴는 진행 중인 공격이 아니라 전투 내내 남는 상태라 구조물 기록(BossChargeStructureLedger)이 맡는다
        public void OnChargeStructureDestroyed(int castId, int structureId, double serverTime)
        {
        }

        /****************************************
        *          Wolbaek Hard Phase 2
        ****************************************/

        public void OnStarStageStarted(in BossStarStageStart start)
        {
            _starStage = new BossStarStageSnapshot
            {
                Start       = start,
                StruckCount = 0
            };
            _hasStarStage = true;
        }

        public void OnStarStageStruck(int castId, int stageIndex, float rotationDegrees, double scheduledServerTime)
        {
            if ((!_hasStarStage) || (_starStage.Start.CastId != castId))
            {
                return;
            }

            _starStage.StruckCount = Mathf.Max(_starStage.StruckCount, stageIndex + 1);
        }

        public void OnStarStageCancelled(int castId)
        {
            if ((!_hasStarStage) || (_starStage.Start.CastId != castId))
            {
                return;
            }

            _hasStarStage = false;
            _starStage    = default;
        }

        public void OnGridBeamStarted(in BossGridBeamStart start)
        {
            _gridBeam = new BossGridBeamSnapshot
            {
                Start            = start,
                TelegraphedCount = 0,
                StruckCount      = 0
            };
            _hasGridBeam = true;
        }

        public void OnGridBeamLineTelegraphed(int castId, int lineId, bool isHorizontal, int lineIndex,
            double telegraphServerTime, double strikeServerTime)
        {
            if ((!_hasGridBeam) || (_gridBeam.Start.CastId != castId))
            {
                return;
            }

            _gridBeam.TelegraphedCount = Mathf.Max(_gridBeam.TelegraphedCount, ToGridOrder(isHorizontal, lineIndex) + 1);
        }

        public void OnGridBeamLineStruck(int castId, int lineId, bool isHorizontal, int lineIndex, double scheduledServerTime)
        {
            if ((!_hasGridBeam) || (_gridBeam.Start.CastId != castId))
            {
                return;
            }

            _gridBeam.StruckCount = Mathf.Max(_gridBeam.StruckCount, ToGridOrder(isHorizontal, lineIndex) + 1);
        }

        public void OnGridBeamCancelled(int castId)
        {
            if ((!_hasGridBeam) || (_gridBeam.Start.CastId != castId))
            {
                return;
            }

            _hasGridBeam = false;
            _gridBeam    = default;
        }

        public void OnChainFanStarted(in BossChainFanStart start)
        {
            _chainFan = new BossChainFanSnapshot
            {
                Start         = start,
                BlastedCount  = 0,
                IsDangerEnded = false
            };
            _hasChainFan = true;
        }

        public void OnChainFanBlasted(int castId, int zoneIndex, double scheduledServerTime)
        {
            if ((!_hasChainFan) || (_chainFan.Start.CastId != castId))
            {
                return;
            }

            _chainFan.BlastedCount = Mathf.Max(_chainFan.BlastedCount, zoneIndex + 1);
        }

        // 위험이 끝나면 복원할 것이 없다
        public void OnChainFanDangerEnded(int castId, double scheduledServerTime)
        {
            RemoveChainFan(castId);
        }

        public void OnChainFanCancelled(int castId)
        {
            RemoveChainFan(castId);
        }

        public void OnTrackingAreaStarted(in BossTrackingAreaStart start)
        {
            RemoveTrackingArea(start.AreaId);

            if (_trackingAreas.Count >= _trackingAreas.Capacity)
            {
                Debug.LogError($"[Boss] 복원 기록의 추적 장판이 가득 차 늦은 접속에 넘기지 못한다, areaId={start.AreaId}");
                return;
            }

            _trackingAreas.Add(new BossTrackingAreaSnapshot
            {
                Start   = start,
                Center  = start.Center,
                IsFixed = false
            });
        }

        public void OnTrackingAreaMoved(int areaId, Vector3 center, double serverTime)
        {
            int index = IndexOfTrackingArea(areaId);
            if (index < 0)
            {
                return;
            }

            BossTrackingAreaSnapshot area = _trackingAreas[index];
            if (area.IsFixed)
            {
                return;
            }

            area.Center           = center;
            _trackingAreas[index] = area;
        }

        public void OnTrackingAreaFixed(int areaId, Vector3 center, double fixedServerTime)
        {
            int index = IndexOfTrackingArea(areaId);
            if (index < 0)
            {
                return;
            }

            BossTrackingAreaSnapshot area = _trackingAreas[index];
            area.Center           = center;
            area.IsFixed          = true;
            area.FixedServerTime  = fixedServerTime;
            _trackingAreas[index] = area;
        }

        public void OnTrackingAreaCancelled(int castId, int areaId)
        {
            RemoveTrackingArea(areaId);
        }

        public void OnBarrageStarted(in BossBarrageStart start)
        {
            ClearBarrage();
            _barrage    = start;
            _hasBarrage = true;
        }

        // 발은 번호 순서대로 예고된다. 순서가 맞지 않는 예고는 복원에서 발 번호와 자리가 어긋나므로 담지 않는다
        public void OnBarrageShotTelegraphed(in BossBarrageShot shot)
        {
            if ((!_hasBarrage) || (_barrage.CastId != shot.CastId) || (shot.ShotIndex != _barrageShots.Count))
            {
                return;
            }

            if (_barrageShots.Count >= _barrageShots.Capacity)
            {
                Debug.LogError($"[Boss] 복원 기록의 난사 예고가 가득 차 늦은 접속에 넘기지 못한다, shotIndex={shot.ShotIndex}");
                return;
            }

            _barrageShots.Add(shot);
        }

        public void OnBarrageShotFired(int castId, int shotIndex, int projectileId, bool isLaunched, double scheduledServerTime)
        {
            if ((!_hasBarrage) || (_barrage.CastId != castId))
            {
                return;
            }

            _barrageFired = Mathf.Max(_barrageFired, shotIndex + 1);
        }

        public void OnBarrageCancelled(int castId)
        {
            if ((!_hasBarrage) || (_barrage.CastId != castId))
            {
                return;
            }

            ClearBarrage();
        }

        public void OnCircleBurstStarted(in BossCircleBurstStart start)
        {
            _circleBurst = new BossCircleBurstSnapshot
            {
                Start    = start,
                IsStruck = false
            };
            _hasCircleBurst = true;
        }

        public void OnCircleBurstStruck(int castId, double scheduledServerTime)
        {
            if ((!_hasCircleBurst) || (_circleBurst.Start.CastId != castId))
            {
                return;
            }

            _circleBurst.IsStruck = true;
        }

        public void OnCircleBurstCancelled(int castId)
        {
            if ((!_hasCircleBurst) || (_circleBurst.Start.CastId != castId))
            {
                return;
            }

            _hasCircleBurst = false;
            _circleBurst    = default;
        }

        /****************************************
        *           Replay / Serialize
        ****************************************/

        // 난사 스냅샷은 예고한 발 목록을 배열로 복사한다. 늦은 접속과 수신부 연결 때만 불리므로 할당을 받아들인다
        /// <summary> 진행 중인 것을 모두 복원으로 넘긴다, 수신부가 구현하지 않은 쪽은 null로 준다 </summary>
        public void Replay(IBossMucheolStateListener mucheol, IBossHardPhaseStateListener hard)
        {
            if (mucheol != null)
            {
                if (_hasStrikeChain)
                {
                    mucheol.OnStrikeChainRestored(in _strikeChain);
                }

                if (_hasMultiDash)
                {
                    mucheol.OnMultiDashRestored(in _multiDash);
                }

                if (_hasBattlefield)
                {
                    mucheol.OnBattlefieldRestored(in _battlefield);
                }

                if (_hasCharge)
                {
                    mucheol.OnChargeRestored(in _charge);
                }

                if (_hasGroggy)
                {
                    mucheol.OnChargeGroggyRestored(in _groggy);
                }
            }

            if (hard == null)
            {
                return;
            }

            if (_hasStarStage)
            {
                hard.OnStarStageRestored(in _starStage);
            }

            if (_hasGridBeam)
            {
                hard.OnGridBeamRestored(in _gridBeam);
            }

            if (_hasChainFan)
            {
                hard.OnChainFanRestored(in _chainFan);
            }

            for (int i = 0; i < _trackingAreas.Count; ++i)
            {
                BossTrackingAreaSnapshot area = _trackingAreas[i];
                hard.OnTrackingAreaRestored(in area);
            }

            if (_hasBarrage)
            {
                BossBarrageSnapshot barrage = CaptureBarrage();
                hard.OnBarrageRestored(in barrage);
            }

            if (_hasCircleBurst)
            {
                hard.OnCircleBurstRestored(in _circleBurst);
            }
        }

        // 순서를 바꾸면 Read도 함께 바꾼다
        public void Write(NetworkWriter writer)
        {
            writer.WriteBool(_hasStrikeChain);
            if (_hasStrikeChain)
            {
                writer.WriteBossStrikeChainSnapshot(_strikeChain);
            }

            writer.WriteBool(_hasMultiDash);
            if (_hasMultiDash)
            {
                writer.WriteBossMultiDashSnapshot(_multiDash);
            }

            writer.WriteBool(_hasBattlefield);
            if (_hasBattlefield)
            {
                writer.WriteBossBattlefieldSnapshot(_battlefield);
            }

            writer.WriteBool(_hasCharge);
            if (_hasCharge)
            {
                writer.WriteBossChargeOutcome(_charge);
            }

            writer.WriteBool(_hasGroggy);
            if (_hasGroggy)
            {
                writer.WriteBossChargeGroggy(_groggy);
            }

            writer.WriteBool(_hasStarStage);
            if (_hasStarStage)
            {
                writer.WriteBossStarStageSnapshot(_starStage);
            }

            writer.WriteBool(_hasGridBeam);
            if (_hasGridBeam)
            {
                writer.WriteBossGridBeamSnapshot(_gridBeam);
            }

            writer.WriteBool(_hasChainFan);
            if (_hasChainFan)
            {
                writer.WriteBossChainFanSnapshot(_chainFan);
            }

            writer.WriteVarUInt((uint)_trackingAreas.Count);
            for (int i = 0; i < _trackingAreas.Count; ++i)
            {
                writer.WriteBossTrackingAreaSnapshot(_trackingAreas[i]);
            }

            writer.WriteBool(_hasBarrage);
            if (_hasBarrage)
            {
                writer.WriteBossBarrageSnapshot(CaptureBarrage());
            }

            writer.WriteBool(_hasCircleBurst);
            if (_hasCircleBurst)
            {
                writer.WriteBossCircleBurstSnapshot(_circleBurst);
            }
        }

        // 기존 기록을 버리고 받은 값으로 바꾼다. 상한을 넘는 개수는 서버가 쓰지 않으므로 프로토콜 불일치로 보고 멈춘다
        public void Read(NetworkReader reader)
        {
            Clear();

            _hasStrikeChain = reader.ReadBool();
            if (_hasStrikeChain)
            {
                _strikeChain = reader.ReadBossStrikeChainSnapshot();
            }

            _hasMultiDash = reader.ReadBool();
            if (_hasMultiDash)
            {
                _multiDash = reader.ReadBossMultiDashSnapshot();
            }

            _hasBattlefield = reader.ReadBool();
            if (_hasBattlefield)
            {
                _battlefield = reader.ReadBossBattlefieldSnapshot();
            }

            _hasCharge = reader.ReadBool();
            if (_hasCharge)
            {
                _charge = reader.ReadBossChargeOutcome();
            }

            _hasGroggy = reader.ReadBool();
            if (_hasGroggy)
            {
                _groggy = reader.ReadBossChargeGroggy();
            }

            _hasStarStage = reader.ReadBool();
            if (_hasStarStage)
            {
                _starStage = reader.ReadBossStarStageSnapshot();
            }

            _hasGridBeam = reader.ReadBool();
            if (_hasGridBeam)
            {
                _gridBeam = reader.ReadBossGridBeamSnapshot();
            }

            _hasChainFan = reader.ReadBool();
            if (_hasChainFan)
            {
                _chainFan = reader.ReadBossChainFanSnapshot();
            }

            uint areaCount = reader.ReadVarUInt();
            if (areaCount > (uint)_trackingAreas.Capacity)
            {
                throw new InvalidDataException($"보스 복원 추적 장판 수가 상한을 넘는다, count={areaCount}");
            }

            for (uint i = 0; i < areaCount; ++i)
            {
                _trackingAreas.Add(reader.ReadBossTrackingAreaSnapshot());
            }

            _hasBarrage = reader.ReadBool();
            if (_hasBarrage)
            {
                BossBarrageSnapshot barrage = reader.ReadBossBarrageSnapshot();
                _barrage      = barrage.Start;
                _barrageFired = barrage.FiredCount;
                _barrageShots.AddRange(barrage.Shots);
            }

            _hasCircleBurst = reader.ReadBool();
            if (_hasCircleBurst)
            {
                _circleBurst = reader.ReadBossCircleBurstSnapshot();
            }
        }

        /// <summary> 진행 중인 난사의 현재 상태, 없으면 거짓이다 </summary>
        public bool TryGetBarrage(out BossBarrageSnapshot snapshot)
        {
            if (!_hasBarrage)
            {
                snapshot = default;
                return false;
            }

            snapshot = CaptureBarrage();
            return true;
        }

        private BossBarrageSnapshot CaptureBarrage()
        {
            return new BossBarrageSnapshot
            {
                Start      = _barrage,
                FiredCount = _barrageFired,
                Shots      = _barrageShots.ToArray()
            };
        }

        private void ClearBarrage()
        {
            _hasBarrage   = false;
            _barrage      = default;
            _barrageFired = 0;
            _barrageShots.Clear();
        }

        private bool IsCurrentStrikeChain(int castId)
        {
            return (_hasStrikeChain) && (_strikeChain.Start.CastId == castId);
        }

        // 앞 돌진에 늦게 온 사건은 지금 돌진의 값을 바꾸지 못한다
        private bool IsCurrentDash(int castId, int dashIndex)
        {
            return (_hasMultiDash) && (_multiDash.Start.CastId == castId) && (_multiDash.DashIndex == dashIndex);
        }

        // 가로줄이 먼저고 세로줄이 뒤다. 발사 순서로 센 번호다
        private int ToGridOrder(bool isHorizontal, int lineIndex)
        {
            return isHorizontal ? lineIndex : (_gridBeam.Start.LineCount + lineIndex);
        }

        private void RemoveChainFan(int castId)
        {
            if ((!_hasChainFan) || (_chainFan.Start.CastId != castId))
            {
                return;
            }

            _hasChainFan = false;
            _chainFan    = default;
        }

        private void RemoveTrackingArea(int areaId)
        {
            int index = IndexOfTrackingArea(areaId);
            if (index >= 0)
            {
                _trackingAreas.RemoveAt(index);
            }
        }

        private int IndexOfTrackingArea(int areaId)
        {
            for (int i = 0; i < _trackingAreas.Count; ++i)
            {
                if (_trackingAreas[i].Start.AreaId == areaId)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
