// System
using System;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.AI
{
    // 시험 씬(Dev_WolbaekAppearPreview)에서 확정한 패턴 VFX를 서버 사건만으로 부른다. 위치와 방향과 크기와 시각은 서버 사건 값에서 읽고 시험용 고정 좌표나 자체 판정 시각은 쓰지 않는다
    //   투사체(1P P04, 이지 2P P05): 발사 사건의 방향, 속도, 사거리로 비행 효과를 날리고 서버가 알린 소멸 위치에 타격 효과를 낸다
    //   대형 방출(1P P06): 예고 사건의 직사각형으로 바닥 예고를 보이고 서버 타격 확정 사건에서만 확정한다
    //   부채꼴(1P P02), 원형(1P P03, 이지 2P P06): 서버 타격 확정 사건에서 서드파티 효과를 낸다
    //   장판(1P P05, 2P P04): 활성 사건의 중심과 반경으로 효과를 켜고 소멸 사건에서 끈다
    //   평행 빔(이지 2P P02), 별 빔(이지 2P P01): 서버 빔과 별 사건의 배치와 시각으로 예고, 확정, 회전을 그린다
    //   하드 2페이즈: 단계 별(P01), 격자(P02), 잔류 부채꼴 연쇄(P03), 난사(P05, 투사체는 기본 발사 사건), 원형 폭발(P06)은 하드 전용 사건으로 부른다. 추적 장판(P04)은 활성 뒤 기본 장판 사건이다
    // 재접속 복원: 서버 값으로 정확히 이어 그릴 수 있는 진행 중 효과(투사체 비행, 장판, 평행 빔, 별 빔, 대형 방출, 돌진 예고)만 현재 시점부터 복원하고
    // 이미 지난 일회성 폭발은 다시 만들지 않는다. 하드 단계 별과 격자 줄은 복원하지 않는다(실행 검증 전)
    /// <summary> 월백 패턴 VFX를 서버 사건에 맞춰 부르는 표현, 연결하지 않은 항목은 비워 두면 기본 표현을 그대로 쓴다 </summary>
    public sealed class WolbaekPatternVfxView : ClientView
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float  END_HEIGHT           = 1f;    // m, 비행 효과가 도착하는 높이
        private const float  FALLBACK_HAND_HEIGHT = 1.4f;  // m, 손 뼈를 찾지 못했을 때 발사 높이
        private const float  FLIGHT_GRACE_SECONDS = 1.5f;  // 초, 서버 소멸 사건이 오지 않을 때 비행 효과를 끊기까지
        private const float  FAN_HEIGHT           = 2f;    // m, 세로 콘 메시가 위에서 아래로 향해 바닥에 묻히지 않게 올린다
        private const float  FAN_SECTOR_DEGREES   = 60f;   // 도, 부채꼴 효과 한 장이 덮는 각도
        private const float  FAN_PREFAB_YAW       = 180f;  // 도, 부채꼴 프리팹은 로컬 -Z로 펼쳐진다
        private const float  FAN_SCALE_PER_METER  = 0.5f;  // 반경 6m에서 효과 크기 3
        private const float  CIRCLE_HEIGHT        = 0.1f;  // m
        private const float  CIRCLE_REFERENCE_RADIUS = 5f; // m
        private const float  CIRCLE_REFERENCE_SCALE  = 1.92f;
        private const float  ZONE_HEIGHT          = 0.1f;  // m
        private const float  ZONE_REFERENCE_RADIUS = 3f;   // m, 효과 크기 1일 때의 서버 반경
        private const float  ZONE_GRACE_SECONDS   = 1f;    // 초, 서버 소멸 사건이 오지 않아도 지속 시간 뒤에 끄기까지
        private const float  EFFECT_LIFETIME      = 4f;    // 초, 한 번 터지는 효과를 지우기까지
        private const float  FADE_SECONDS         = 3f;    // 초, 생성을 멈춘 효과의 남은 파티클이 사라지기를 기다리는 시간
        private const float  RELEASE_ACTIVE_SECONDS = 1.4f; // 초, 대형 방출 파면의 표현 지속 시간(서버 값이 아니다)
        private const float  BEAM_HEIGHT          = 2f;    // m, 빔이 바닥에 깔려 발밑을 가리지 않게 올린다
        private const float  DASH_TELEGRAPH_HEIGHT = 0.02f; // m
        private const float  DASH_TELEGRAPH_SCALE  = 0.4f;
        private const float  DASH_TELEGRAPH_LIFE   = 6f;   // 초, 예고 효과 중 가장 긴 파티클이 사라지는 시간
        private const float  STAGE_ACTIVE_SECONDS  = 0.8f; // 초, 단계 별 한 단계의 확정 표시 시간(서버 값이 아니다)
        private const float  GRID_HIT_SECONDS      = 0.4f; // 초, 격자 한 줄의 타격 표시 시간(서버 값이 아니다)
        private const float  GRID_IDLE_GRACE       = 3f;   // 초
        private const float  HARD_CIRCLE_MULTIPLIER = 0.8f; // 하드 원형 폭발은 효과 크기를 줄여 쓴다
        private const float  HALF                  = 0.5f;
        private const float  FULL_TURN             = 360f;
        private const int    NO_CAST               = -1;
        private const int    STAGE_SLOT_GROUPS     = 2;
        private const int    MAX_TRACKED_CASTS     = 4;
        private const int    RELEASE_LINE_ID      = 1;
        private const float  RELEASE_LIFT         = 0.3f;  // m, 대형 방출이 바닥 메시와 겹쳐 가려지지 않게 올린다
        private const uint   REVISION             = 1;
        private const int    MAX_AREAS            = 8;
        private const int    MAX_BEAMS            = 8;
        private const int    ZONE_SOUND_KEY_OFFSET = 1 << 20; // 반복 소리 키가 시전 번호와 겹치지 않게 장판 번호에 더한다
        private const int    BEAM_HOLD_SOUND_KEY   = 1 << 21; // 평행 빔 유지 소리는 한 번에 하나만 둔다
        private const int    STAR_HOLD_SOUND_KEY   = 1 << 22; // 별 빔 유지 소리는 한 번에 하나만 둔다

        /****************************************
        *              Nested Types
        ****************************************/

        private struct Shot
        {
            public int     ProjectileId;
            public uint    FlightId;
            public Vector3 Start;
            public Vector3 End;
            public float   GroundY;
            public float   PathLength;
            public float   Speed;
            public double  Launch;
            public bool    IsActive;
        }

        private struct Zone
        {
            public int        AreaId;
            public GameObject Instance;
            public double     EndServerTime;
        }

        private struct ChainFanState
        {
            public int     CastId;
            public Vector3 Origin;
            public Vector3 SafeDirection;
            public float   Range;
            public int     ZoneCount;
            public float   ZoneArc;
            public bool    IsUsed;
        }

        private struct BurstState
        {
            public int       CastId;
            public Vector3[] Centers;
            public float[]   Radii;
            public bool      IsUsed;
        }

        private struct BeamLine
        {
            public int    BeamId;
            public float  Duration;
            public bool   IsUsed;
        }

        /****************************************
        *                Fields
        ****************************************/

        [Header("투사체")]
        [SerializeField] private Animator _animator;
        [SerializeField] private WolbaekFoxfirePlayer[] _flights;
        [SerializeField] private WolbaekFoxfirePlayer[] _impacts;

        [Header("효과음")]
        [SerializeField] private WolbaekSoundPlayer _sound; // 비어 있으면 소리를 생략한다

        [Header("빔")]
        [SerializeField] private WolbaekBeamPlayer _release;
        [SerializeField] private WolbaekBeamPlayer _sustain;
        [SerializeField] private WolbaekBeamPlayer _star;

        [SerializeField] private WolbaekBeamPlayer _grid;

        [Header("서드파티 폭발과 장판")]
        [SerializeField] private GameObject _dashTelegraphEffect;
        [SerializeField] private GameObject _fanEffect;
        [SerializeField] private GameObject _circleEffect;
        [SerializeField] private GameObject _zoneEffect;

        private Transform             _effectsRoot;
        private BossTelegraphPresenter _presenter;
        private Func<double>           _clock;
        private readonly WolbaekServerEventGate _gate = new WolbaekServerEventGate(); // 시계 오차로 서버 시각이 아직 오지 않은 확정 사건을 그 시각까지 보류한다
        private Shot[]                 _shots;
        private Zone[]                 _zones;
        private BeamLine[]             _beamLines;
        private int                    _impactCursor;

        private readonly int[]  _barrageCastIds = new int[MAX_TRACKED_CASTS];
        private int             _barrageCursor;
        private readonly ChainFanState[] _chainFans = new ChainFanState[MAX_TRACKED_CASTS];
        private int             _chainCursor;
        private readonly BurstState[] _bursts = new BurstState[MAX_TRACKED_CASTS];
        private int             _burstCursor;

        private int    _stageCastId;
        private BossStarStageStart _stageStart;
        private bool   _isStageStar;

        private int    _gridCastId;
        private uint   _gridPlayback;
        private BossGridBeamStart _gridStart;
        private double _gridEnd;

        private int    _releaseCastId;
        private uint   _releasePlayback;
        private double _releaseEnd;

        private int    _sustainCastId;
        private int    _beamSoundCastId = NO_CAST;  // 방출과 유지 소리를 이미 시작한 빔 시전
        private Vector3 _beamSoundPosition;
        private uint   _sustainPlayback;
        private BossBeamStart _lateBeam;    // 복원 때 예고 시간이 지나 활성 알림을 기다리는 빔
        private bool   _hasLateBeam;
        private BossStarStart _lateStar;    // 복원 때 예고 시간이 지나 활성 알림을 기다리는 별
        private bool   _hasLateStar;

        private int          _starId;
        private uint         _starPlayback;
        private BossStarShape _starShape;
        private double       _starActivate;
        private float        _starDuration;
        private bool         _isStarActive;
        private double       _starEnd;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            for (int i = 0; i < _barrageCastIds.Length; ++i)
            {
                _barrageCastIds[i] = NO_CAST;
            }

            _shots     = new Shot[(_flights != null) ? _flights.Length : 0];
            _zones     = new Zone[MAX_AREAS];
            _beamLines = new BeamLine[MAX_BEAMS];

            // 효과 재생기는 부모 회전 0, 스케일 1을 요구하고 서버 월드 좌표로 움직인다. 보스는 방향을 따라 돌기 때문에 월드 루트로 옮긴다
            _effectsRoot = new GameObject("WolbaekPatternEffects").transform;
            Reparent(_flights);
            Reparent(_impacts);
            Reparent(_release);
            Reparent(_sustain);
            Reparent(_star);
            Reparent(_grid);
        }

        private void OnDestroy()
        {
            Unbind();

            if (_effectsRoot != null)
            {
                Destroy(_effectsRoot.gameObject);
            }
        }

        private void Reparent(Component player)
        {
            if (player != null)
            {
                player.transform.SetParent(_effectsRoot, false);
            }
        }

        private void Reparent(Component[] players)
        {
            for (int i = 0; (players != null) && (i < players.Length); ++i)
            {
                Reparent(players[i]);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 예고 표현 진입점에 연결한다, 연결된 효과가 있는 종류만 기본 표현을 대신한다 </summary>
        public void Bind(BossTelegraphPresenter presenter, Func<double> clock)
        {
            Unbind();

            _presenter = presenter;
            _clock     = clock;

            // 같은 보스 프리팹의 재생기를 찾아 쓴다, 프리팹에 직접 넣는 단계가 필요 없다
            if (_sound == null)
            {
                _sound = GetComponentInParent<WolbaekSoundPlayer>();
            }

            if (_presenter == null)
            {
                return;
            }

            bool hasProjectile = (_flights != null) && (_flights.Length > 0) && (_impacts != null) && (_impacts.Length > 0);
            _presenter.HasExternalProjectileVisual = hasProjectile;
            _presenter.HasExternalAreaVisual       = _zoneEffect != null;
            _presenter.HasExternalBeamVisual       = _sustain != null;
            _presenter.HasExternalStarVisual       = _star != null;

            if (_release != null)
            {
                _presenter.RegisterExternalVisual("wolbaek_p06");
                _presenter.RegisterExternalVisual(BossPatternCatalogSO.WOLBAEK_HARD_P06_ID);
            }

            _presenter.ProjectileStarted += OnProjectileStarted;
            _presenter.ProjectileEnded   += OnProjectileEnded;
            _presenter.AreaStarted       += OnAreaStarted;
            _presenter.AreaFateChanged   += OnAreaFateChanged;
            _presenter.BeamStarted       += OnBeamStarted;
            _presenter.BeamActivated     += OnBeamActivated;
            _presenter.BeamFateChanged   += OnBeamFateChanged;
            _presenter.StarStarted       += OnStarStarted;
            _presenter.StarActivated     += OnStarActivated;
            _presenter.StarEnded         += OnStarEnded;
        }

        public void Unbind()
        {
            if (_presenter != null)
            {
                _presenter.ProjectileStarted -= OnProjectileStarted;
                _presenter.ProjectileEnded   -= OnProjectileEnded;
                _presenter.AreaStarted       -= OnAreaStarted;
                _presenter.AreaFateChanged   -= OnAreaFateChanged;
                _presenter.BeamStarted       -= OnBeamStarted;
                _presenter.BeamActivated     -= OnBeamActivated;
                _presenter.BeamFateChanged   -= OnBeamFateChanged;
                _presenter.StarStarted       -= OnStarStarted;
                _presenter.StarActivated     -= OnStarActivated;
                _presenter.StarEnded         -= OnStarEnded;
                _presenter.HasExternalProjectileVisual = false;
                _presenter.HasExternalAreaVisual       = false;
                _presenter.HasExternalBeamVisual       = false;
                _presenter.HasExternalStarVisual       = false;
            }

            _presenter = null;
            StopAll();
        }

        /// <summary> 진행 중인 공격 효과(여우불 비행, 장판, 빔, 별, 격자, 대형 방출)를 끊는다. 보스 처치 때 서버 취소 사건이 없어도 남지 않게 쓴다 </summary>
        public void StopAttackEffects()
        {
            StopAll();
        }

        /// <summary> 매 프레임 서버 시계를 전달한다 </summary>
        public void Tick(double now)
        {
            _gate.Flush(now);
            TickShots(now);
            TickImpacts(now);
            TickRelease(now);
            TickSustain(now);
            TickStar(now);
            TickGrid(now);
            TickZones(now);
        }

        /// <summary> 시전 단계 알림, 대형 방출은 예고와 확정을, 부채꼴과 원형은 타격 확정을 받는다 </summary>
        public void OnCastPhase(BossTelegraphCast cast, EBossTelegraphPhase phase, double now)
        {
            if (cast == null)
            {
                return;
            }

            if (IsBeamRelease(cast.PatternId))
            {
                OnReleasePhase(cast, phase, now);
                return;
            }

            if ((phase == EBossTelegraphPhase.Telegraph) && WolbaekMotionCatalog.IsDash(cast.PatternId))
            {
                SpawnDashTelegraph(cast);
                StartLoopSound(cast.CastId, EWolbaekSound.ChargeLoop, cast.Origin);
                return;
            }

            if (phase != EBossTelegraphPhase.Execute)
            {
                return;
            }

            // 돌진이 시작되면 예고 차징을 끊고 이동음을 낸다
            if (WolbaekMotionCatalog.IsDash(cast.PatternId))
            {
                StopLoopSound(cast.CastId);
                PlaySound(EWolbaekSound.Dash, cast.Origin);
                return;
            }

            // 재접속 복원의 발동 단계는 이미 지난 타격이다, 일회성 폭발을 다시 만들지 않는다
            if (IsRestoring)
            {
                return;
            }

            if (IsFan(cast.PatternId))
            {
                SpawnFan(cast);
            }
            else if (IsCircle(cast.PatternId))
            {
                SpawnCircle(cast);
            }
        }

        /// <summary> 시전이 끝나거나 취소됐다. 취소된 대형 방출은 효과를 끊고 정상 종료는 파면이 지속 시간까지 이어진다 </summary>
        public void OnCastEnded(int castId, bool isCancelled)
        {
            StopLoopSound(castId);

            if (isCancelled && (_releaseCastId == castId))
            {
                StopRelease();
            }
        }

        // 재접속 스냅샷을 복원하는 알림 안인가
        private bool IsRestoring => (_presenter != null) && _presenter.IsRestoring;

        private static bool IsBeamRelease(string patternId)
        {
            return (patternId == "wolbaek_p06") || (patternId == BossPatternCatalogSO.WOLBAEK_HARD_P06_ID);
        }

        private static bool IsFan(string patternId)
        {
            return (patternId == "wolbaek_p02") || (patternId == BossPatternCatalogSO.WOLBAEK_HARD_P02_ID)
                || (patternId == BossPatternCatalogSO.WOLBAEK_P2_P03_ID);
        }

        private static bool IsCircle(string patternId)
        {
            return (patternId == "wolbaek_p03") || (patternId == BossPatternCatalogSO.WOLBAEK_HARD_P03_ID)
                || (patternId == BossPatternCatalogSO.WOLBAEK_P2_P06_ID);
        }

        /****************************************
        *               Projectile
        ****************************************/

        private void OnProjectileStarted(BossProjectileStart start)
        {
            if ((_shots == null) || (_shots.Length == 0))
            {
                return;
            }

            int slot = FindFreeShot();
            if (slot < 0)
            {
                return;
            }

            Vector3 direction = start.Direction;
            direction.y = 0f;
            direction = (direction.sqrMagnitude > 0f) ? direction.normalized : Vector3.forward;

            double now = ReadClock();
            bool isRestoring = IsRestoring;

            // 재접속으로 이미 도착했을 시각이면 지난 일회성 비행이라 다시 날리지 않는다
            if (isRestoring && (start.Speed > 0f) && (now > (start.LaunchServerTime + (start.PathLength / start.Speed))))
            {
                return;
            }

            // 복원 때는 지금의 손 위치가 발사 때의 손 위치가 아니므로 서버 발사 위치에서 고정 높이로 시작한다
            Vector3 hand = isRestoring ? (start.Origin + (Vector3.up * FALLBACK_HAND_HEIGHT))
                : (IsBarrageCast(start.CastId) ? GetChestPosition(start.Origin) : GetHandPosition(start.Origin));
            Vector3 end  = start.Origin + (direction * start.PathLength);
            end.y = start.Origin.y + END_HEIGHT;

            uint flight = _flights[slot].PlayFlight(hand, Quaternion.LookRotation(end - hand), start.LaunchServerTime, now);
            if (flight == 0)
            {
                return;
            }

            _shots[slot] = new Shot
            {
                ProjectileId = start.ProjectileId,
                FlightId     = flight,
                Start        = hand,
                End          = end,
                GroundY      = start.Origin.y,
                PathLength   = start.PathLength,
                Speed        = start.Speed,
                Launch       = start.LaunchServerTime,
                IsActive     = true
            };

            // 비행 효과가 발사 시각에 맞춰 시작하므로 소리도 같은 만큼 늦춘다
            PlaySound(EWolbaekSound.FoxfireShot, hand, (float)(start.LaunchServerTime - now));
        }

        private void OnProjectileEnded(int projectileId, EBossProjectileFate fate, Vector3 position)
        {
            for (int i = 0; (_shots != null) && (i < _shots.Length); ++i)
            {
                if ((!_shots[i].IsActive) || (_shots[i].ProjectileId != projectileId))
                {
                    continue;
                }

                _flights[i].Stop(_shots[i].FlightId);
                _shots[i].IsActive = false;

                // 서버가 알린 소멸 위치에서 타격을 낸다, 취소된 투사체는 효과 없이 끝낸다
                if (fate != EBossProjectileFate.Cancelled)
                {
                    PlayImpact(new Vector3(position.x, _shots[i].GroundY, position.z));
                }

                return;
            }
        }

        private void TickShots(double now)
        {
            for (int i = 0; (_shots != null) && (i < _shots.Length); ++i)
            {
                if (!_shots[i].IsActive)
                {
                    continue;
                }

                Shot shot = _shots[i];
                float flightSeconds = (shot.Speed > 0f) ? (shot.PathLength / shot.Speed) : 0f;

                // 서버 소멸 사건이 오지 않으면 사거리 끝에서 멈춘 채 기다리다가 끊는다
                if (now > (shot.Launch + flightSeconds + FLIGHT_GRACE_SECONDS))
                {
                    _flights[i].Stop(shot.FlightId);
                    _shots[i].IsActive = false;
                    continue;
                }

                float progress = (shot.PathLength > 0f) ? Mathf.Clamp01((float)((now - shot.Launch) * shot.Speed / shot.PathLength)) : 1f;
                _flights[i].SetFlightPose(shot.FlightId, Vector3.Lerp(shot.Start, shot.End, progress), Quaternion.LookRotation(shot.End - shot.Start));
                _flights[i].Tick(shot.FlightId, now);
            }
        }

        private void TickImpacts(double now)
        {
            for (int i = 0; (_impacts != null) && (i < _impacts.Length); ++i)
            {
                if (_impacts[i].IsRunning)
                {
                    _impacts[i].Tick(_impacts[i].PlaybackId, now);
                }
            }
        }

        private int FindFreeShot()
        {
            for (int i = 0; i < _shots.Length; ++i)
            {
                if (!_shots[i].IsActive)
                {
                    return i;
                }
            }

            return -1;
        }

        // 재접속 복원 중에는 이미 지난 일회성 소리를 다시 내지 않는다
        private void PlaySound(EWolbaekSound id, Vector3 position, float delaySeconds = 0f)
        {
            if ((_sound == null) || IsRestoring)
            {
                return;
            }

            _sound.Play(id, position, delaySeconds);
        }

        private void StartLoopSound(int key, EWolbaekSound id, Vector3 position)
        {
            if ((_sound == null) || IsRestoring)
            {
                return;
            }

            _sound.StartLoop(key, id, position);
        }

        // 복원 중이어도 멈추는 것은 막지 않는다
        private void StopLoopSound(int key)
        {
            if (_sound != null)
            {
                _sound.StopLoop(key);
            }
        }

        private void PlayImpact(Vector3 groundPoint)
        {
            PlaySound(EWolbaekSound.FoxfireBurst, groundPoint);

            if ((_impacts == null) || (_impacts.Length == 0))
            {
                return;
            }

            double now = ReadClock();
            int slot = _impactCursor % _impacts.Length;
            ++_impactCursor;
            _impacts[slot].Stop();
            _impacts[slot].PlayImpact(groundPoint, EWolbaekFoxfireVariant.Blue, now, now);
        }

        // 난사는 시전 시작 위치(가슴)에서 전방위로 쏜다
        private Vector3 GetChestPosition(Vector3 fallbackOrigin)
        {
            Transform chest = (_animator != null) ? _animator.GetBoneTransform(HumanBodyBones.Chest) : null;
            return (chest != null) ? chest.position : (fallbackOrigin + (Vector3.up * FALLBACK_HAND_HEIGHT));
        }

        private bool IsBarrageCast(int castId)
        {
            for (int i = 0; i < _barrageCastIds.Length; ++i)
            {
                if (_barrageCastIds[i] == castId)
                {
                    return true;
                }
            }

            return false;
        }

        private Vector3 GetHandPosition(Vector3 fallbackOrigin)
        {
            Transform hand = (_animator != null) ? _animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
            return (hand != null) ? hand.position : (fallbackOrigin + (Vector3.up * FALLBACK_HAND_HEIGHT));
        }

        /****************************************
        *            Beam Release
        ****************************************/

        private void OnReleasePhase(BossTelegraphCast cast, EBossTelegraphPhase phase, double now)
        {
            if (_release == null)
            {
                return;
            }

            // 재접속으로 발동 단계에서 받으면 예고 알림이 없었으므로 확정된 서버 시각을 예정 시각으로 삼아 이어서 그린다
            bool isRestoredExecute = IsRestoring && (phase == EBossTelegraphPhase.Execute) && (_releaseCastId != cast.CastId);

            if ((phase == EBossTelegraphPhase.Telegraph) || isRestoredExecute)
            {
                StopRelease();
                _releaseCastId = cast.CastId;
                _releasePlayback = _release.BeginCast((ulong)cast.CastId, now);

                if (_releasePlayback == 0)
                {
                    return;
                }

                if (!isRestoredExecute)
                {
                    StartLoopSound(cast.CastId, EWolbaekSound.ChargeLoop, cast.Origin);
                }

                double expected = isRestoredExecute ? cast.ExecuteServerTime : (cast.Start.StartServerTime + cast.Start.TelegraphSeconds);
                _releaseEnd = expected + RELEASE_ACTIVE_SECONDS;

                // 파면 지속 시간이 이미 지났으면 지난 일회성 방출이라 그리지 않는다
                if (isRestoredExecute && (now > _releaseEnd))
                {
                    StopRelease();
                    return;
                }

                _release.Warn(_releasePlayback, RELEASE_LINE_ID, REVISION, BuildReleaseShape(cast), cast.Start.StartServerTime,
                    expected, _releaseEnd, now);

                if (isRestoredExecute)
                {
                    _release.ConfirmActive(_releasePlayback, RELEASE_LINE_ID, REVISION, cast.ExecuteServerTime, _releaseEnd, now);
                }
            }
            else if ((phase == EBossTelegraphPhase.Execute) && (_releaseCastId == cast.CastId) && (_releasePlayback != 0))
            {
                // 서버 확정 시각이 표시 시계보다 앞서 도착하면 재생기가 거절하므로 그 시각까지 보류한다
                if (_gate.MustDefer(cast.ExecuteServerTime, now))
                {
                    uint playback = _releasePlayback;
                    _gate.Defer(cast.ExecuteServerTime, now, () => ConfirmRelease(cast, playback, ReadClock()));
                    return;
                }

                ConfirmRelease(cast, _releasePlayback, now);
            }
        }

        // 서버 타격 확정 사건을 받았을 때만 확정한다. 보류 중 취소되거나 새 시전으로 바뀌었으면 버린다
        private void ConfirmRelease(BossTelegraphCast cast, uint playback, double now)
        {
            if ((_release == null) || (_releasePlayback != playback) || (_releaseCastId != cast.CastId))
            {
                return;
            }

            _releaseEnd = cast.ExecuteServerTime + RELEASE_ACTIVE_SECONDS;
            _release.ConfirmActive(playback, RELEASE_LINE_ID, REVISION, cast.ExecuteServerTime, _releaseEnd, now);

            StopLoopSound(cast.CastId);
            PlaySound(EWolbaekSound.WaveBeam, cast.Origin);
        }

        private static WolbaekBeamShape BuildReleaseShape(BossTelegraphCast cast)
        {
            Vector3 direction = cast.Direction;
            direction.y = 0f;
            Vector3 start = cast.Origin + (Vector3.up * RELEASE_LIFT);
            Vector3 end = start + (direction.normalized * cast.Length);
            return new WolbaekBeamShape(start, end, cast.Width);
        }

        private void TickRelease(double now)
        {
            if (_releasePlayback == 0)
            {
                return;
            }

            // 예고 중에는 서버가 알린 조준 방향과 길이를 따라 바닥 예고를 갱신한다, 확정 뒤에는 방향이 고정이다
            if ((_presenter != null) && _presenter.TryGetVisuals(_releaseCastId, out BossTelegraphCast cast, out _, out _) && (!cast.HasExecuted))
            {
                _release.UpdateGeometry(_releasePlayback, RELEASE_LINE_ID, REVISION, BuildReleaseShape(cast), now);
            }

            _release.Tick(_releasePlayback, now);

            if (now > _releaseEnd)
            {
                StopRelease();
            }
        }

        private void StopRelease()
        {
            if ((_releasePlayback != 0) && (_release != null))
            {
                _release.Stop(_releasePlayback);
            }

            _releasePlayback = 0;
            _releaseCastId = 0;
        }

        /****************************************
        *            Fan And Circle
        ****************************************/

        // 부채꼴 프리팹 한 장은 약 60도라 서버 각도를 좌우로 나눈 두 장으로 덮는다
        private void SpawnFan(BossTelegraphCast cast)
        {
            PlaySound(EWolbaekSound.BigBlast, cast.Origin);

            if (_fanEffect == null)
            {
                return;
            }

            Vector3 direction = cast.Direction;
            direction.y = 0f;
            Quaternion baseRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            float spread = Mathf.Max(0f, (cast.ArcAngle * HALF) - (FAN_SECTOR_DEGREES * HALF));

            // 각도가 한 장으로 덮이는 구역(반월 구역 45도 등)은 한 장만 낸다
            float[] spreads = (spread > Mathf.Epsilon) ? new[] { -spread, spread } : new[] { 0f };

            for (int i = 0; i < spreads.Length; ++i)
            {
                Quaternion rotation = baseRotation * Quaternion.Euler(0f, spreads[i] + FAN_PREFAB_YAW, 0f);
                GameObject instance = Instantiate(_fanEffect, cast.Origin + (Vector3.up * FAN_HEIGHT), rotation);
                instance.transform.localScale = _fanEffect.transform.localScale * (cast.Length * FAN_SCALE_PER_METER);
                Destroy(instance, EFFECT_LIFETIME);
            }
        }

        // 돌진 예고 동안 몸 모션은 Idle이고 발밑에서 예고 효과를 한 번 터뜨린다
        private void SpawnDashTelegraph(BossTelegraphCast cast)
        {
            if (_dashTelegraphEffect == null)
            {
                return;
            }

            GameObject instance = Instantiate(_dashTelegraphEffect, cast.Origin + (Vector3.up * DASH_TELEGRAPH_HEIGHT), Quaternion.identity);
            instance.transform.localScale = _dashTelegraphEffect.transform.localScale * DASH_TELEGRAPH_SCALE;
            Destroy(instance, DASH_TELEGRAPH_LIFE);
        }

        private void SpawnCircle(BossTelegraphCast cast)
        {
            PlaySound(EWolbaekSound.BigBlast, cast.Origin);

            if (_circleEffect == null)
            {
                return;
            }

            GameObject instance = Instantiate(_circleEffect, cast.Origin + (Vector3.up * CIRCLE_HEIGHT), Quaternion.identity);
            instance.transform.localScale = _circleEffect.transform.localScale * (CIRCLE_REFERENCE_SCALE * cast.Length / CIRCLE_REFERENCE_RADIUS);
            Destroy(instance, EFFECT_LIFETIME);
        }

        /****************************************
        *                 Zone
        ****************************************/

        private void OnAreaStarted(BossAreaStart start)
        {
            if (_zoneEffect == null)
            {
                return;
            }

            // 재접속으로 지속 시간이 이미 끝난 장판은 그리지 않는다
            if (IsRestoring && (ReadClock() > start.EndServerTime))
            {
                return;
            }

            for (int i = 0; i < _zones.Length; ++i)
            {
                if (_zones[i].Instance != null)
                {
                    continue;
                }

                GameObject instance = Instantiate(_zoneEffect, start.Center + (Vector3.up * ZONE_HEIGHT), Quaternion.identity);
                instance.transform.localScale = _zoneEffect.transform.localScale * (start.Radius / ZONE_REFERENCE_RADIUS);
                _zones[i] = new Zone { AreaId = start.AreaId, Instance = instance, EndServerTime = start.EndServerTime };

                PlaySound(EWolbaekSound.FoxfireIgnite, start.Center);
                StartLoopSound(ZONE_SOUND_KEY_OFFSET + start.AreaId, EWolbaekSound.FoxfireHoldLoop, start.Center);
                return;
            }
        }

        private void OnAreaFateChanged(int areaId, EBossAreaFate fate)
        {
            for (int i = 0; i < _zones.Length; ++i)
            {
                if ((_zones[i].Instance != null) && (_zones[i].AreaId == areaId))
                {
                    EndZone(i, fate == EBossAreaFate.Cancelled);
                    return;
                }
            }
        }

        // 서버 소멸 사건이 오지 않아도 지속 시간이 지나면 생성을 멈춘다
        private void TickZones(double now)
        {
            for (int i = 0; i < _zones.Length; ++i)
            {
                if ((_zones[i].Instance != null) && (now > (_zones[i].EndServerTime + ZONE_GRACE_SECONDS)))
                {
                    EndZone(i, false);
                }
            }
        }

        private void EndZone(int index, bool isImmediate)
        {
            GameObject instance = _zones[index].Instance;
            StopLoopSound(ZONE_SOUND_KEY_OFFSET + _zones[index].AreaId);
            _zones[index] = default;

            if (isImmediate)
            {
                Destroy(instance);
                return;
            }

            foreach (ParticleSystem system in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                system.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }

            Destroy(instance, FADE_SECONDS);
        }

        /****************************************
        *            Parallel Beams
        ****************************************/

        private void OnBeamStarted(BossBeamStart start)
        {
            if (_sustain == null)
            {
                return;
            }

            double now = ReadClock();

            // 재접속으로 예고 시간이 이미 지났으면 활성 알림이 이어서 올 때 그린다. 서버가 활성을 확정하지 않은 빔은 그리지 않는다
            if (IsRestoring && (now > start.TelegraphEndServerTime))
            {
                _lateBeam    = start;
                _hasLateBeam = true;
                return;
            }

            WarnBeam(in start, start.TelegraphEndServerTime, now);
        }

        private void WarnBeam(in BossBeamStart start, double expectedActive, double now)
        {
            if ((_sustainPlayback == 0) || (_sustainCastId != start.CastId))
            {
                StopSustain();
                _sustainCastId = start.CastId;
                _sustainPlayback = _sustain.BeginCast((ulong)start.CastId, now);
            }

            if (_sustainPlayback == 0)
            {
                return;
            }

            BossBeamShape shape = start.Shape;
            if (!BossBeamGeometry.TryGetCenterLineChord(in shape, out Vector3 from, out Vector3 to))
            {
                return;
            }

            from.y += BEAM_HEIGHT;
            to.y += BEAM_HEIGHT;
            Vector3 center = shape.ArenaCenter;
            center.y += BEAM_HEIGHT;

            // 같은 시전의 여러 줄은 대표음 하나만 쓴다, 같은 키가 이미 있으면 재생기가 무시한다
            _beamSoundPosition = center;
            StartLoopSound(start.CastId, EWolbaekSound.ChargeLoop, center);

            _sustain.Warn(_sustainPlayback, start.BeamId, REVISION, new WolbaekBeamShape(from, to, shape.Width, center, shape.ArenaRadius),
                start.StartServerTime, expectedActive, expectedActive + start.DurationSeconds, now);
            RememberBeam(start.BeamId, start.DurationSeconds);
        }

        private void OnBeamActivated(int beamId, double activateServerTime)
        {
            if (_sustain == null)
            {
                return;
            }

            // 복원: 예고 시간이 지난 뒤 받은 활성 빔은 지금까지 흐른 상태로 바로 활성으로 그린다.
            // 05.Art 재생기는 예정 활성 시각 + 유예 뒤의 확정을 거절하므로 예정 시각 자리에 복원 시각을 넣는다(서버 값이 아니다)
            if (IsRestoring && _hasLateBeam && (_lateBeam.BeamId == beamId))
            {
                _hasLateBeam = false;
                double restoreNow = ReadClock();

                if (restoreNow <= (activateServerTime + _lateBeam.DurationSeconds))
                {
                    WarnBeam(in _lateBeam, restoreNow, restoreNow);
                }
            }

            if ((_sustainPlayback == 0) || (!TryFindBeam(beamId, out _)))
            {
                return;
            }

            // 복원 중에는 이미 지난 시각이라 보류하지 않는다
            double now = ReadClock();
            if ((!IsRestoring) && _gate.MustDefer(activateServerTime, now))
            {
                uint playback = _sustainPlayback;
                _gate.Defer(activateServerTime, now, () => ConfirmBeam(beamId, activateServerTime, playback));
                return;
            }

            ConfirmBeam(beamId, activateServerTime, _sustainPlayback);
        }

        // 보류 중 빔이 사라졌거나 새 시전으로 바뀌었으면 버린다
        private void ConfirmBeam(int beamId, double activateServerTime, uint playback)
        {
            if ((_sustain == null) || (_sustainPlayback != playback) || (!TryFindBeam(beamId, out int index)))
            {
                return;
            }

            _sustain.ConfirmActive(playback, beamId, REVISION, activateServerTime,
                activateServerTime + _beamLines[index].Duration, ReadClock());

            // 시전마다 첫 활성에서만 차징을 끊고 방출과 유지 루프를 시작한다
            if (_beamSoundCastId != _sustainCastId)
            {
                _beamSoundCastId = _sustainCastId;
                StopLoopSound(_sustainCastId);
                PlaySound(EWolbaekSound.WaveBeam, _beamSoundPosition);
                StartLoopSound(BEAM_HOLD_SOUND_KEY, EWolbaekSound.BeamHoldLoop, _beamSoundPosition);
            }
        }

        private void OnBeamFateChanged(int beamId, EBossAreaFate fate)
        {
            if (!TryFindBeam(beamId, out int index))
            {
                return;
            }

            _beamLines[index] = default;

            if ((fate == EBossAreaFate.Cancelled) && (_sustainPlayback != 0))
            {
                _sustain.StopLine(_sustainPlayback, beamId, REVISION);
                StopLoopSound(_sustainCastId);
            }
        }

        private void TickSustain(double now)
        {
            if (_sustainPlayback != 0)
            {
                _sustain.Tick(_sustainPlayback, now);

                if (!_sustain.IsRunning)
                {
                    _sustainPlayback = 0;
                    StopBeamSound();
                }
            }
        }

        private void StopSustain()
        {
            if ((_sustainPlayback != 0) && (_sustain != null))
            {
                _sustain.Stop(_sustainPlayback);
            }

            _sustainPlayback = 0;
            _sustainCastId = 0;
            StopBeamSound();
        }

        private void StopBeamSound()
        {
            _beamSoundCastId = NO_CAST;
            StopLoopSound(BEAM_HOLD_SOUND_KEY);
        }

        private void RememberBeam(int beamId, float duration)
        {
            for (int i = 0; i < _beamLines.Length; ++i)
            {
                if (!_beamLines[i].IsUsed)
                {
                    _beamLines[i] = new BeamLine { BeamId = beamId, Duration = duration, IsUsed = true };
                    return;
                }
            }
        }

        private bool TryFindBeam(int beamId, out int index)
        {
            for (int i = 0; i < _beamLines.Length; ++i)
            {
                if (_beamLines[i].IsUsed && (_beamLines[i].BeamId == beamId))
                {
                    index = i;
                    return true;
                }
            }

            index = -1;
            return false;
        }

        /****************************************
        *                 Star
        ****************************************/

        private void OnStarStarted(BossStarStart start)
        {
            if (_star == null)
            {
                return;
            }

            // 재접속으로 예고 시간이 이미 지났으면 활성 알림이 이어서 올 때 그린다
            if (IsRestoring && (ReadClock() > start.TelegraphEndServerTime))
            {
                _lateStar    = start;
                _hasLateStar = true;
                return;
            }

            WarnStar(in start, start.TelegraphEndServerTime);
        }

        private void WarnStar(in BossStarStart start, double expectedActive)
        {
            StopStar();

            double now = ReadClock();
            _starPlayback = _star.BeginCast((ulong)start.CastId, now);
            if (_starPlayback == 0)
            {
                return;
            }

            _starId       = start.StarId;
            _starShape    = start.Shape;
            _starDuration = start.DurationSeconds;
            _isStarActive = false;
            _starEnd      = expectedActive + start.DurationSeconds;

            for (int i = 0; i < _starShape.BeamCount; ++i)
            {
                _star.Warn(_starPlayback, i, REVISION, BuildStarBeam(i, 0f), start.StartServerTime, expectedActive, _starEnd, now);
            }
        }

        private void OnStarActivated(int starId, double activateServerTime)
        {
            if (_star == null)
            {
                return;
            }

            // 복원: 활성 확정을 05.Art 재생기가 받으려면 예정 활성 시각이 지금 근처여야 한다. 복원 시각을 넣어 이어서 그린다(서버 값이 아니다)
            if (IsRestoring && _hasLateStar && (_lateStar.StarId == starId))
            {
                _hasLateStar = false;
                double restoreNow = ReadClock();

                if (restoreNow <= (activateServerTime + _lateStar.DurationSeconds))
                {
                    WarnStar(in _lateStar, restoreNow);
                }
            }

            if ((_starPlayback == 0) || (_starId != starId))
            {
                return;
            }

            double clock = ReadClock();
            if ((!IsRestoring) && _gate.MustDefer(activateServerTime, clock))
            {
                uint playback = _starPlayback;
                _gate.Defer(activateServerTime, clock, () => ConfirmStar(starId, activateServerTime, playback));
                return;
            }

            ConfirmStar(starId, activateServerTime, _starPlayback);
        }

        // 보류 중 별이 끊겼거나 새 시전으로 바뀌었으면 버린다
        private void ConfirmStar(int starId, double activateServerTime, uint playback)
        {
            if ((_star == null) || (_starPlayback != playback) || (_starId != starId))
            {
                return;
            }

            _starActivate = activateServerTime;
            _starEnd      = activateServerTime + _starDuration;
            _isStarActive = true;
            double now = ReadClock();

            for (int i = 0; i < _starShape.BeamCount; ++i)
            {
                _star.ConfirmActive(_starPlayback, i, REVISION, activateServerTime, _starEnd, now);
            }

            PlaySound(EWolbaekSound.WaveBeam, transform.position);
            StartLoopSound(STAR_HOLD_SOUND_KEY, EWolbaekSound.BeamHoldLoop, transform.position);
        }

        private void OnStarEnded(int starId, EBossAreaFate fate)
        {
            if ((_starId == starId) && (fate == EBossAreaFate.Cancelled))
            {
                StopStar();
            }
        }

        // 확정 뒤에는 서버가 정한 방향과 속도로 활성 서버 시각부터 흐른 시간만큼 돈다
        private void TickStar(double now)
        {
            if (_starPlayback == 0)
            {
                return;
            }

            if (_isStarActive)
            {
                float rotation = BossStarGeometry.RotationAt(in _starShape, now - _starActivate, _starDuration);

                for (int i = 0; i < _starShape.BeamCount; ++i)
                {
                    _star.UpdateGeometry(_starPlayback, i, REVISION, BuildStarBeam(i, rotation), now);
                }
            }

            _star.Tick(_starPlayback, now);

            if (now > (_starEnd + FADE_SECONDS))
            {
                StopStar();
            }
        }

        private WolbaekBeamShape BuildStarBeam(int index, float rotation)
        {
            BossStarGeometry.GetBeam(in _starShape, index, rotation, out Vector3 from, out Vector3 to);
            from.y += BEAM_HEIGHT;
            to.y += BEAM_HEIGHT;
            return new WolbaekBeamShape(from, to, _starShape.BeamWidth);
        }

        private void StopStar()
        {
            if ((_starPlayback != 0) && (_star != null))
            {
                _star.Stop(_starPlayback);
            }

            _starPlayback = 0;
            _isStarActive = false;
            _isStageStar = false;
            _stageCastId = 0;
            StopLoopSound(STAR_HOLD_SOUND_KEY);
        }

        /****************************************
        *            Hard Phase Events
        ****************************************/

        /// <summary> 하드 2P P01 단계 별: 지금 단계는 확정, 다음 단계는 예고로 두 묶음을 번갈아 쓴다 </summary>
        public void OnStarStageStarted(in BossStarStageStart start)
        {
            if (_star == null)
            {
                return;
            }

            StopStar();
            double now = ReadClock();
            _starPlayback = _star.BeginCast((ulong)start.CastId, now);
            if (_starPlayback == 0)
            {
                return;
            }

            _stageCastId = start.CastId;
            _stageStart  = start;
            _starShape   = start.Shape;
            _isStageStar = true;
            _starEnd     = start.CastStartServerTime + start.FirstStrikeSeconds + ((start.StageCount - 1) * start.StrikeIntervalSeconds)
                + STAGE_ACTIVE_SECONDS;

            WarnStage(0, now);

            if (start.StageCount > 1)
            {
                WarnStage(1, now);
            }
        }

        public void OnStarStageStruck(int castId, int stageIndex, double scheduledServerTime)
        {
            if ((!_isStageStar) || (_starPlayback == 0) || (_stageCastId != castId))
            {
                return;
            }

            double clock = ReadClock();
            if ((!IsRestoring) && _gate.MustDefer(scheduledServerTime, clock))
            {
                uint playback = _starPlayback;
                _gate.Defer(scheduledServerTime, clock, () => ConfirmStage(castId, stageIndex, scheduledServerTime, playback));
                return;
            }

            ConfirmStage(castId, stageIndex, scheduledServerTime, _starPlayback);
        }

        // 보류 중 단계 별이 끊겼거나 새 시전으로 바뀌었으면 버린다
        private void ConfirmStage(int castId, int stageIndex, double scheduledServerTime, uint playback)
        {
            if ((!_isStageStar) || (_starPlayback != playback) || (_stageCastId != castId))
            {
                return;
            }

            double now = ReadClock();

            // 단계마다 줄 전체가 동시에 나가므로 대표음 한 번만 낸다
            PlaySound(EWolbaekSound.WaveBeam, transform.position);

            for (int i = 0; i < _starShape.BeamCount; ++i)
            {
                _star.ConfirmActive(_starPlayback, StageLineId(stageIndex, i), StageRevision(stageIndex), scheduledServerTime,
                    scheduledServerTime + STAGE_ACTIVE_SECONDS, now);
            }

            // 방금 끝난 묶음이 쓰던 슬롯을 두 단계 뒤의 예고로 다시 쓴다
            if ((stageIndex >= 1) && ((stageIndex + 1) < _stageStart.StageCount))
            {
                WarnStage(stageIndex + 1, now);
            }
        }

        private void WarnStage(int stage, double now)
        {
            double expected = _stageStart.CastStartServerTime + _stageStart.FirstStrikeSeconds + (stage * _stageStart.StrikeIntervalSeconds);
            float rotation = stage * _stageStart.StepDegrees;

            for (int i = 0; i < _starShape.BeamCount; ++i)
            {
                _star.Warn(_starPlayback, StageLineId(stage, i), StageRevision(stage), BuildStarBeam(i, rotation),
                    _stageStart.CastStartServerTime, expected, expected + STAGE_ACTIVE_SECONDS, now);
            }
        }

        private int StageLineId(int stage, int beamIndex)
        {
            return ((stage % STAGE_SLOT_GROUPS) * _starShape.BeamCount) + beamIndex;
        }

        private static uint StageRevision(int stage)
        {
            return REVISION + (uint)(stage / STAGE_SLOT_GROUPS);
        }

        /// <summary> 하드 2P P02 순차 격자: 서버가 알린 줄마다 예고와 타격을 그린다 </summary>
        public void OnGridBeamStarted(in BossGridBeamStart start)
        {
            if (_grid == null)
            {
                return;
            }

            StopGrid();
            double now = ReadClock();
            _gridPlayback = _grid.BeginCast((ulong)start.CastId, now);
            if (_gridPlayback == 0)
            {
                return;
            }

            _gridCastId = start.CastId;
            _gridStart  = start;
            int last = start.LineCount - 1;
            _gridEnd = start.CastStartServerTime + start.FirstStrikeSeconds + (last * start.StrikeIntervalSeconds)
                + start.AxisGapSeconds + (last * start.StrikeIntervalSeconds) + GRID_HIT_SECONDS;
        }

        public void OnGridBeamLineTelegraphed(int castId, int lineId, bool isHorizontal, int lineIndex, double telegraphServerTime,
            double strikeServerTime)
        {
            if ((_gridPlayback == 0) || (_gridCastId != castId))
            {
                return;
            }

            _grid.Warn(_gridPlayback, lineId, REVISION, BuildGridLine(isHorizontal, lineIndex), telegraphServerTime, strikeServerTime,
                strikeServerTime + GRID_HIT_SECONDS, ReadClock());
        }

        public void OnGridBeamLineStruck(int castId, int lineId, bool isHorizontal, int lineIndex, double scheduledServerTime)
        {
            if ((_gridPlayback == 0) || (_gridCastId != castId))
            {
                return;
            }

            double now = ReadClock();
            if ((!IsRestoring) && _gate.MustDefer(scheduledServerTime, now))
            {
                uint playback = _gridPlayback;
                _gate.Defer(scheduledServerTime, now, () => ConfirmGridLine(castId, lineId, scheduledServerTime, playback));
                return;
            }

            ConfirmGridLine(castId, lineId, scheduledServerTime, _gridPlayback);
        }

        // 보류 중 격자가 끊겼거나 새 시전으로 바뀌었으면 버린다
        private void ConfirmGridLine(int castId, int lineId, double scheduledServerTime, uint playback)
        {
            if ((_grid == null) || (_gridPlayback != playback) || (_gridCastId != castId))
            {
                return;
            }

            _grid.ConfirmActive(playback, lineId, REVISION, scheduledServerTime, scheduledServerTime + GRID_HIT_SECONDS, ReadClock());
            PlaySound(EWolbaekSound.WaveBeam, transform.position);
        }

        // 서버 BossGridBeamLayout과 같은 인자다. 가로줄은 월드 -X, 세로줄은 월드 +Z 방향이고 오프셋은 줄 간격의 절반이다
        private WolbaekBeamShape BuildGridLine(bool isHorizontal, int lineIndex)
        {
            Vector3 direction = isHorizontal ? Vector3.left : Vector3.forward;
            Vector3 center = _gridStart.ArenaCenter;
            BossBeamShape beam = BossBeamGeometry.Place(center, _gridStart.ArenaRadius, direction, lineIndex, _gridStart.LineCount,
                _gridStart.LineSpacing * HALF, _gridStart.LineWidth);
            BossBeamGeometry.TryGetCenterLineChord(in beam, out Vector3 from, out Vector3 to);
            from.y += BEAM_HEIGHT;
            to.y += BEAM_HEIGHT;
            center.y += BEAM_HEIGHT;
            return new WolbaekBeamShape(from, to, _gridStart.LineWidth, center, _gridStart.ArenaRadius);
        }

        private void TickGrid(double now)
        {
            if (_gridPlayback == 0)
            {
                return;
            }

            _grid.Tick(_gridPlayback, now);

            if (now > (_gridEnd + GRID_IDLE_GRACE))
            {
                StopGrid();
            }
        }

        private void StopGrid()
        {
            if ((_gridPlayback != 0) && (_grid != null))
            {
                _grid.Stop(_gridPlayback);
            }

            _gridPlayback = 0;
            _gridCastId = 0;
        }

        /// <summary> 하드 2P P03 잔류 부채꼴 연쇄: 구역이 폭발하는 서버 사건마다 그 구역 방향으로 부채꼴 효과를 낸다 </summary>
        public void OnChainFanStarted(in BossChainFanStart start)
        {
            _chainFans[_chainCursor % _chainFans.Length] = new ChainFanState
            {
                CastId        = start.CastId,
                Origin        = start.Origin,
                SafeDirection = start.SafeDirection,
                Range         = start.Range,
                ZoneCount     = start.ZoneCount,
                ZoneArc       = start.ZoneArc,
                IsUsed        = true
            };
            ++_chainCursor;
        }

        public void OnChainFanBlasted(int castId, int zoneIndex, double scheduledServerTime)
        {
            if (_fanEffect == null)
            {
                return;
            }

            for (int i = 0; i < _chainFans.Length; ++i)
            {
                if ((!_chainFans[i].IsUsed) || (_chainFans[i].CastId != castId))
                {
                    continue;
                }

                ChainFanState fan = _chainFans[i];
                PlaySound(EWolbaekSound.BigBlast, fan.Origin);
                Vector3 safe = fan.SafeDirection;
                safe.y = 0f;
                safe = (safe.sqrMagnitude > 0f) ? safe.normalized : Vector3.forward;

                // 서버 구역 배치와 같다: 피난 각도의 절반에 구역 각도를 이어 붙인 중심 방향
                float safeArc = FULL_TURN - (fan.ZoneCount * fan.ZoneArc);
                float offset = (safeArc * HALF) + (fan.ZoneArc * (zoneIndex + HALF));
                Vector3 direction = Quaternion.AngleAxis(offset, Vector3.up) * safe;

                Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up) * Quaternion.Euler(0f, FAN_PREFAB_YAW, 0f);
                GameObject instance = Instantiate(_fanEffect, fan.Origin + (Vector3.up * FAN_HEIGHT), rotation);
                instance.transform.localScale = _fanEffect.transform.localScale * (fan.Range * FAN_SCALE_PER_METER);
                Destroy(instance, EFFECT_LIFETIME);
                return;
            }
        }

        /// <summary> 하드 2P P06 원형 폭발: 서버가 알린 원마다 같은 순간에 폭발 효과를 낸다 </summary>
        public void OnCircleBurstStarted(in BossCircleBurstStart start)
        {
            var centers = new Vector3[start.CircleCount];
            var radii = new float[start.CircleCount];

            for (int i = 0; i < start.CircleCount; ++i)
            {
                centers[i] = start.Centers[i];
                radii[i] = start.Radii[i];
            }

            _bursts[_burstCursor % _bursts.Length] = new BurstState { CastId = start.CastId, Centers = centers, Radii = radii, IsUsed = true };
            ++_burstCursor;
        }

        public void OnCircleBurstStruck(int castId, double scheduledServerTime)
        {
            if (_circleEffect == null)
            {
                return;
            }

            for (int i = 0; i < _bursts.Length; ++i)
            {
                if ((!_bursts[i].IsUsed) || (_bursts[i].CastId != castId))
                {
                    continue;
                }

                // 여러 원이 같은 순간에 터지므로 대표음 한 번만 낸다
                PlaySound(EWolbaekSound.BigBlast, _bursts[i].Centers[0]);

                for (int c = 0; c < _bursts[i].Centers.Length; ++c)
                {
                    GameObject instance = Instantiate(_circleEffect, _bursts[i].Centers[c] + (Vector3.up * CIRCLE_HEIGHT), Quaternion.identity);
                    instance.transform.localScale = _circleEffect.transform.localScale
                        * (CIRCLE_REFERENCE_SCALE * _bursts[i].Radii[c] / CIRCLE_REFERENCE_RADIUS * HARD_CIRCLE_MULTIPLIER);
                    Destroy(instance, EFFECT_LIFETIME);
                }

                return;
            }
        }

        /// <summary> 하드 2P P05 난사 시작, 같은 시전의 투사체는 시전 시작 위치(가슴)에서 나간다 </summary>
        public void OnBarrageStarted(in BossBarrageStart start)
        {
            _barrageCastIds[_barrageCursor % _barrageCastIds.Length] = start.CastId;
            ++_barrageCursor;
        }

        /// <summary> 재접속 복원: 아직 폭발하지 않은 구역이 남은 연쇄만 등록한다. 이미 폭발한 구역은 다시 터뜨리지 않고 남은 구역은 서버 사건으로 터진다 </summary>
        public void RestoreChainFan(in BossChainFanSnapshot snapshot)
        {
            if ((!snapshot.IsDangerEnded) && (snapshot.BlastedCount < snapshot.Start.ZoneCount))
            {
                OnChainFanStarted(snapshot.Start);
            }
        }

        /// <summary> 재접속 복원: 서버 시각상 아직 진행 중인 별 단계만 이어 그린다. 지난 단계의 타격 파티클은 만들지 않는다 </summary>
        public void RestoreStarStage(in BossStarStageSnapshot snapshot)
        {
            BossStarStageStart start = snapshot.Start;
            if ((start.StageCount <= 0) || (ReadClock() > start.CastStartServerTime + start.FirstStrikeSeconds
                + ((start.StageCount - 1) * start.StrikeIntervalSeconds) + STAGE_ACTIVE_SECONDS))
            {
                return;
            }

            OnStarStageStarted(in start);
            for (int stage = 0; stage < snapshot.StruckCount && stage < start.StageCount; ++stage)
            {
                double strikeTime = start.CastStartServerTime + start.FirstStrikeSeconds + (stage * start.StrikeIntervalSeconds);
                OnStarStageStruck(start.CastId, stage, strikeTime);
            }
        }

        /// <summary> 재접속 복원: 아직 터지지 않은 원형 폭발만 등록한다, 터진 폭발은 다시 만들지 않는다 </summary>
        public void RestoreCircleBurst(in BossCircleBurstSnapshot snapshot)
        {
            if (!snapshot.IsStruck)
            {
                OnCircleBurstStarted(snapshot.Start);
            }
        }

        /// <summary> 재접속 복원: 난사 시전을 등록한다, 남은 발의 투사체는 기본 투사체 복원이 날린다 </summary>
        public void RestoreBarrage(in BossBarrageSnapshot snapshot)
        {
            OnBarrageStarted(snapshot.Start);
        }

        /// <summary> 하드 전용 사건 시전이 취소됐다 </summary>
        public void OnHardCancelled(int castId)
        {
            if (_stageCastId == castId)
            {
                StopStar();
            }

            if (_gridCastId == castId)
            {
                StopGrid();
            }
        }

        /****************************************
        *               Common
        ****************************************/

        private double ReadClock()
        {
            return (_clock != null) ? _clock() : Time.timeAsDouble;
        }

        private void StopAll()
        {
            _gate.Clear();

            for (int i = 0; (_shots != null) && (i < _shots.Length); ++i)
            {
                if (_shots[i].IsActive)
                {
                    _flights[i].Stop(_shots[i].FlightId);
                    _shots[i].IsActive = false;
                }
            }

            for (int i = 0; (_zones != null) && (i < _zones.Length); ++i)
            {
                if (_zones[i].Instance != null)
                {
                    EndZone(i, true);
                }
            }

            _hasLateBeam = false;
            _hasLateStar = false;

            if (_sound != null)
            {
                _sound.StopAll();
            }

            StopRelease();
            StopSustain();
            StopStar();
            StopGrid();
        }
    }
}
