// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Common;

namespace Shinshi.AI
{
    // 서버가 확정한 보스 공격 예고를 화면에 그리는 표현 진입점이다. 네트워크 수신부가 서버 알림을 아래 공개 메서드로 바꿔 부른다
    // 클라이언트는 타깃 추적, 벽 검사, 피해 판정을 하지 않는다. 받은 방향과 길이와 시각을 그대로 그린다
    // 이 클래스는 서버 어셈블리(Shinshi.Server)와 Mirror에 의존하지 않는다. 서버 시각은 수신부가 SetServerClock으로 넘긴다
    // 부채꼴과 원형은 서버가 발동을 알리기 전까지 충전이 끝난 모습으로 멈춰 있고, 알림이 오면 이어서 폭발한다. 취소가 늦게 와도 터지지 않게 하려는 것이다
    /// <summary> 월백 P01 돌진, P02 부채꼴, P03 원형, P06 직선 방출의 예고, 발동, 취소 표현을 시전 번호별로 관리한다 </summary>
    public sealed class BossTelegraphPresenter : ClientView
    {
        /****************************************
        *              Constants
        ****************************************/

        // 표현 상수다, 밸런스 값이 아니다
        private const float  CONFIRM_GRACE_SECONDS = 0.5f; // 예고가 끝난 뒤 서버 확정을 더 기다리는 시간
        private const float  AIM_SMOOTH_DEGREES    = 720f; // 도/초, 서버 갱신 사이를 부드럽게 이을 때만 쓴다
        private const float  FULL_CIRCLE_ANGLE     = 360f;
        private const int    ENDED_HISTORY         = 32;   // 끝난 시전 번호를 기억하는 수, 늦게 온 알림이 되살리지 못하게 한다
        private const float  NO_DELAY              = 0f;
        private const float  NOT_EXECUTED          = -1f;
        private const float  PROJECTILE_HEAD_LENGTH = 1.5f; // m, 투사체를 길쭉한 머리로 그릴 때의 길이, 판정 길이가 아니다
        private const float  PROJECTILE_DISSOLVE    = 0.25f; // 초, 소멸 연출 길이
        private const float  AREA_FADE             = 0.3f;  // 초, 장판이 사라질 때 옅어지는 시간

        /****************************************
        *                Fields
        ****************************************/

        // 진행 중인 표현 한 건, 모델과 그리는 대상을 묶는다
        private sealed class Entry
        {
            public BossTelegraphCast          Cast;
            public SectorVFXController        Sector;
            public RectTelegraphVFXController Rect;
            public Quaternion                 DisplayRotation;

            // 부채꼴 연출이 스스로 끝났다. 풀이 같은 연출을 다른 시전에 곧바로 빌려줘도 이 표현이 그것을 자기 것으로 착각하지 않게 하려고
            // 끝나는 순간 이벤트로 표시한다
            public bool                       SectorCompleted;
            public Action<SectorVFXController> OnSectorCompleted;
        }

        // 날고 있는 투사체 한 발, 예고 표현과 따로 산다. 패턴이 끝나거나 같은 보스의 다음 시전이 시작돼도 이어서 그린다
        private sealed class ProjectileEntry
        {
            public BossProjectileTrack        Track;
            public RectTelegraphVFXController Rect;
            public Quaternion                 Rotation;
            public bool                       EndSeen;
            public double                     EndSeenAt;
        }

        // 활성된 장판 한 개, 예고 표현과 따로 산다. 패턴이 끝나거나 같은 보스의 다음 시전이 시작돼도 지속 시간 동안 그린다
        private sealed class AreaEntry
        {
            public BossAreaTrack            Track;
            public AreaGroundVFXController  Visual;
            public bool                     EndSeen;
            public double                   EndSeenAt;
        }

        [SerializeField] private AreaGroundVFXPool _areaPool;
        [SerializeField] private SectorVFXPool _sectorPool;
        [SerializeField] private RectTelegraphVFXPool _rectPool;

        private readonly List<Entry>    _active       = new List<Entry>();
        private readonly List<ProjectileEntry> _projectiles = new List<ProjectileEntry>();
        private readonly List<AreaEntry> _areas       = new List<AreaEntry>();
        private readonly HashSet<int>   _areaEndedIds   = new HashSet<int>();
        private readonly Queue<int>     _areaEndedOrder = new Queue<int>();
        private readonly HashSet<int>   _projectileEndedIds   = new HashSet<int>();
        private readonly Queue<int>     _projectileEndedOrder = new Queue<int>();
        private readonly HashSet<int>   _endedIds     = new HashSet<int>();
        private readonly Queue<int>     _endedOrder   = new Queue<int>();

        private Func<double> _serverClock;
        private bool         _isServerBuild;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 진행 중인 표현 수 </summary>
        public int ActiveCount => _active.Count;

        /// <summary> 단계가 바뀔 때 알린다, 시전 번호와 새 단계. 시험과 진단용이다 </summary>
        public event Action<int, EBossTelegraphPhase> PhaseChanged;

        /// <summary> 진행 중인 투사체 수 </summary>
        public int ProjectileCount => _projectiles.Count;

        /// <summary> 투사체의 소멸이 확인될 때 알린다, 시전 번호와 이유. 시험과 진단용이다 </summary>
        public event Action<int, EBossProjectileFate> ProjectileFateChanged;

        /// <summary> 진행 중인 장판 수 </summary>
        public int AreaCount => _areas.Count;

        /// <summary> 장판의 소멸이 확인될 때 알린다, 장판 번호와 이유. 시험과 진단용이다 </summary>
        public event Action<int, EBossAreaFate> AreaFateChanged;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnServerBuildAwake()
        {
            _isServerBuild = true;
        }

        private void Update()
        {
            double now = ReadServerTime();

            for (int i = _active.Count - 1; i >= 0; --i)
            {
                if (UpdateEntry(_active[i], now))
                {
                    _active.RemoveAt(i);
                }
            }

            for (int i = _projectiles.Count - 1; i >= 0; --i)
            {
                if (UpdateProjectile(_projectiles[i], now))
                {
                    _projectiles.RemoveAt(i);
                }
            }

            for (int i = _areas.Count - 1; i >= 0; --i)
            {
                if (UpdateArea(_areas[i], now))
                {
                    _areas.RemoveAt(i);
                }
            }

            // 진행 중인 표현이 없으면 갱신을 멈춘다
            if ((_active.Count == 0) && (_projectiles.Count == 0) && (_areas.Count == 0))
            {
                enabled = false;
            }
        }

        // 꺼지거나 파괴될 때 진행 중인 표현을 모두 지운다. 풀에서 빌린 것을 돌려줘야 다음에 다시 쓸 수 있다
        private void OnDisable()
        {
            ClearAll();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 서버 시각을 읽는 함수를 정한다, 네트워크 수신부가 서버 시각 동기화 값을 넘긴다. 비우면 로컬 시각을 쓰며 샘플 입력용이다 </summary>
        public void SetServerClock(Func<double> serverClock)
        {
            _serverClock = serverClock;
        }

        /// <summary> 예고를 시작한다. 이미 있는 시전 번호나 이미 끝난 시전 번호면 무시한다. 늦게 받았으면 흐른 만큼 건너뛰어 그린다 </summary>
        public bool BeginTelegraph(in BossTelegraphStart start)
        {
            if (_isServerBuild || IsKnown(start.CastId))
            {
                return false;
            }

            double now = ReadServerTime();
            if (now >= (start.StartServerTime + start.TelegraphSeconds + CONFIRM_GRACE_SECONDS))
            {
                // 확정을 기다릴 시간까지 지난 예고는 그리지 않는다, 늦게 온 취소나 발동이 되살리지도 못한다
                RememberEnded(start.CastId);
                return false;
            }

            RetireSameBoss(start.BossId);
            return Add(new BossTelegraphCast(start), now);
        }

        /// <summary> 추적 중 방향과 길이 갱신, 고정 뒤나 발동 뒤나 오래된 갱신이면 무시한다 </summary>
        public bool UpdateAim(int castId, Vector3 direction, float length, double serverTime)
        {
            if (!TryFind(castId, out Entry entry))
            {
                return false;
            }

            return entry.Cast.TryUpdateAim(direction, length, serverTime);
        }

        /// <summary> 방향 고정 </summary>
        public bool LockDirection(int castId, Vector3 direction, float length)
        {
            if (!TryFind(castId, out Entry entry))
            {
                return false;
            }

            return entry.Cast.TryLockDirection(direction, length);
        }

        /// <summary> 부채꼴, 원형, 직사각형(P02, P03, P06)의 타격 확정, 서버의 OnHit에 해당한다 </summary>
        public bool Strike(int castId, double serverTime)
        {
            if (!TryFind(castId, out Entry entry) || (!entry.Cast.TryStrike(serverTime)))
            {
                return false;
            }

            ApplyRotation(entry, true);

            // 충전 끝에서 멈춰 있던 부채꼴과 원형이 이어서 폭발한다
            if (entry.Sector != null)
            {
                entry.Sector.Confirm();
            }

            PhaseChanged?.Invoke(castId, EBossTelegraphPhase.Execute);
            return true;
        }

        /// <summary> 돌진(P01) 시작, distance는 벽 앞 거리이고 speed는 서버가 정한 일정한 속도다. startServerTime은 이동을 시작한 서버 시각이다 </summary>
        public bool BeginDash(int castId, float distance, float seconds, float speed, double startServerTime)
        {
            if (!TryFind(castId, out Entry entry) || (!entry.Cast.TryBeginDash(distance, seconds, speed, startServerTime)))
            {
                return false;
            }

            ApplyRotation(entry, true);
            PhaseChanged?.Invoke(castId, EBossTelegraphPhase.Execute);
            return true;
        }

        /// <summary> 돌진 정지, 정해진 거리에 닿았거나 벽에서 멈췄다. 정지 위치 이후로는 더 그리지 않는다 </summary>
        public bool StopDash(int castId, Vector3 position, bool stoppedByWall)
        {
            if (!TryFind(castId, out Entry entry))
            {
                return false;
            }

            return entry.Cast.TryStopDash(position, stoppedByWall);
        }

        /// <summary> 중단, 서버가 확정한 발동 전이나 돌진 중에만 받아들인다. 모르는 시전 번호면 나중에 늦게 오는 시작이 되살아나지 않게 기억해 둔다 </summary>
        public bool Cancel(int castId)
        {
            if (_isServerBuild)
            {
                return false;
            }

            if (!TryFind(castId, out Entry entry))
            {
                RememberEnded(castId);
                return false;
            }

            if (!entry.Cast.TryCancel())
            {
                return false;
            }

            Remove(entry);
            PhaseChanged?.Invoke(castId, EBossTelegraphPhase.Cancelled);
            return true;
        }

        /// <summary> 정상 종료, 발동한 표현은 남은 연출을 마치고 사라진다. 발동을 받지 못한 채 끝난 시전은 확정이 없었으므로 지운다 </summary>
        public bool Finish(int castId)
        {
            if (!TryFind(castId, out Entry entry))
            {
                return false;
            }

            if (!entry.Cast.HasExecuted)
            {
                return Cancel(castId);
            }

            if (!entry.Cast.TryFinish())
            {
                return false;
            }

            PhaseChanged?.Invoke(castId, EBossTelegraphPhase.Finished);
            return true;
        }

        /// <summary> 늦게 접속했거나 다시 관찰을 시작할 때 진행 중인 공격을 스냅샷으로 복원한다. 처음부터 다시 재생하지 않고 서버 시각 기준으로 이어서 그린다 </summary>
        public bool Restore(in BossTelegraphSnapshot snapshot)
        {
            int castId = snapshot.Start.CastId;
            if (_isServerBuild || IsKnown(castId))
            {
                return false;
            }

            if ((snapshot.Phase == EBossTelegraphPhase.Finished) || (snapshot.Phase == EBossTelegraphPhase.Cancelled))
            {
                RememberEnded(castId);
                return false;
            }

            double now = ReadServerTime();
            if ((snapshot.Phase == EBossTelegraphPhase.Telegraph)
                && (now >= (snapshot.Start.StartServerTime + snapshot.Start.TelegraphSeconds + CONFIRM_GRACE_SECONDS)))
            {
                RememberEnded(castId);
                return false;
            }

            RetireSameBoss(snapshot.Start.BossId);
            return Add(BossTelegraphCast.FromSnapshot(in snapshot), now);
        }

        /// <summary> 진행 중인 표현의 현재 상태, 복원에 넘길 값이다 </summary>
        public bool TryGetSnapshot(int castId, out BossTelegraphSnapshot snapshot)
        {
            if (TryFind(castId, out Entry entry))
            {
                snapshot = entry.Cast.ToSnapshot();
                return true;
            }

            snapshot = default;
            return false;
        }

        /// <summary> 투사체 발사, 같은 시전 번호의 예고가 있으면 그 예고는 발사와 함께 마무리하고 투사체는 따로 그린다. 이미 있는 번호나 끝난 번호면 무시한다 </summary>
        public bool LaunchProjectile(in BossProjectileStart start)
        {
            int id = start.ProjectileId;
            if (_isServerBuild || IsProjectileKnown(id))
            {
                return false;
            }

            double now = ReadServerTime();
            BossProjectileTrack track = new BossProjectileTrack(start);

            // 소멸까지 이미 다 지난 늦은 발사는 그리지 않고, 뒤늦게 온 소멸이 되살리지도 못한다
            if (now >= (start.LaunchServerTime + track.FlightSeconds + PROJECTILE_DISSOLVE))
            {
                RememberProjectileEnded(id);
                return false;
            }

            if (TryFind(id, out Entry telegraph) && (telegraph.Cast.Shape == EBossTelegraphShape.Projectile))
            {
                Strike(id, start.LaunchServerTime);
            }

            AddProjectile(track);
            return true;
        }

        /// <summary> 투사체 소멸, 서버가 알린 위치와 이유로 멈춘다. 모르는 번호면 나중에 늦게 오는 발사가 되살아나지 않게 기억해 둔다 </summary>
        public bool EndProjectile(int projectileId, EBossProjectileFate fate, Vector3 position)
        {
            if (_isServerBuild)
            {
                return false;
            }

            if (fate == EBossProjectileFate.Cancelled)
            {
                return CancelProjectile(projectileId);
            }

            if (!TryFindProjectile(projectileId, out ProjectileEntry entry))
            {
                RememberProjectileEnded(projectileId);
                return false;
            }

            if (!entry.Track.TryEnd(fate, position))
            {
                return false;
            }

            ProjectileFateChanged?.Invoke(projectileId, fate);
            return true;
        }

        /// <summary> 투사체 제거(보스 사망, 페이즈 전환, 전투 종료), 표시는 바로 지운다 </summary>
        public bool CancelProjectile(int projectileId)
        {
            if (_isServerBuild)
            {
                return false;
            }

            if (!TryFindProjectile(projectileId, out ProjectileEntry entry))
            {
                RememberProjectileEnded(projectileId);
                return false;
            }

            if (!entry.Track.TryCancel())
            {
                return false;
            }

            RemoveProjectile(entry);
            ProjectileFateChanged?.Invoke(projectileId, EBossProjectileFate.Cancelled);
            return true;
        }

        /// <summary> 날고 있는 투사체를 스냅샷으로 복원한다, 이미 소멸했거나 소멸 연출까지 끝난 것은 무시한다. 처음부터 다시 날리지 않고 서버 시각 기준으로 이어서 그린다 </summary>
        public bool RestoreProjectile(in BossProjectileSnapshot snapshot)
        {
            int id = snapshot.Start.ProjectileId;
            if (_isServerBuild || IsProjectileKnown(id))
            {
                return false;
            }

            if (snapshot.Fate == EBossProjectileFate.Cancelled)
            {
                RememberProjectileEnded(id);
                return false;
            }

            BossProjectileTrack track = BossProjectileTrack.FromSnapshot(in snapshot);
            double now = ReadServerTime();
            if (now >= (track.LaunchServerTime + track.FlightSeconds + PROJECTILE_DISSOLVE))
            {
                RememberProjectileEnded(id);
                return false;
            }

            AddProjectile(track);
            return true;
        }

        /// <summary> 진행 중인 투사체의 현재 상태, 복원에 넘길 값이다 </summary>
        public bool TryGetProjectileSnapshot(int projectileId, out BossProjectileSnapshot snapshot)
        {
            if (TryFindProjectile(projectileId, out ProjectileEntry entry))
            {
                snapshot = entry.Track.ToSnapshot();
                return true;
            }

            snapshot = default;
            return false;
        }

        /// <summary> 진행 중인 투사체의 모델과 그리는 대상, 시험과 진단용이다. 풀이 비었으면 rect는 null이다 </summary>
        public bool TryGetProjectile(int projectileId, out BossProjectileTrack track, out RectTelegraphVFXController rect)
        {
            if (TryFindProjectile(projectileId, out ProjectileEntry entry))
            {
                track = entry.Track;
                rect  = entry.Rect;
                return true;
            }

            track = null;
            rect  = null;
            return false;
        }

        /// <summary> 장판 활성, 서버가 활성을 알린 뒤에만 장판을 그린다(예고가 끝났다는 이유만으로 그리지 않는다). 같은 번호의 원형 예고가 있으면 그 예고는 활성과 함께 마무리하고 장판은 따로 그린다. 이미 있는 번호나 끝난 번호면 무시한다 </summary>
        public bool ActivateArea(in BossAreaStart start)
        {
            int id = start.AreaId;
            if (_isServerBuild || IsAreaKnown(id))
            {
                return false;
            }

            double now = ReadServerTime();
            BossAreaTrack track = new BossAreaTrack(start);

            // 소멸 연출까지 이미 다 지난 늦은 활성은 그리지 않고, 뒤늦게 온 알림이 되살리지도 못한다
            if (now >= (start.EndServerTime + AREA_FADE))
            {
                RememberAreaEnded(id);
                return false;
            }

            if (TryFind(id, out Entry telegraph) && (telegraph.Cast.Shape == EBossTelegraphShape.Circle))
            {
                Strike(id, start.ActivateServerTime);
            }

            AddArea(track);
            return true;
        }

        /// <summary> 서버가 알린 타격 회차, 활성 순간이 0회차다. 이미 표시한 회차나 끝난 장판이면 무시한다 </summary>
        public bool TickArea(int areaId, int hitIndex, double roundServerTime)
        {
            return TryFindArea(areaId, out AreaEntry entry) && entry.Track.TryTick(hitIndex, roundServerTime);
        }

        /// <summary> 장판 소멸, Expired는 옅어지며 사라지고 Cancelled는 즉시 지운다. 모르는 번호면 나중에 늦게 오는 활성이 되살아나지 않게 기억해 둔다 </summary>
        public bool EndArea(int areaId, EBossAreaFate fate)
        {
            if (_isServerBuild)
            {
                return false;
            }

            if (fate == EBossAreaFate.Cancelled)
            {
                return CancelArea(areaId);
            }

            if (!TryFindArea(areaId, out AreaEntry entry))
            {
                RememberAreaEnded(areaId);
                return false;
            }

            if (!entry.Track.TryEnd(fate))
            {
                return false;
            }

            AreaFateChanged?.Invoke(areaId, fate);
            return true;
        }

        /// <summary> 장판 제거(보스 사망, 페이즈 전환, 전투 종료), 표시는 바로 지운다 </summary>
        public bool CancelArea(int areaId)
        {
            if (_isServerBuild)
            {
                return false;
            }

            if (!TryFindArea(areaId, out AreaEntry entry))
            {
                RememberAreaEnded(areaId);
                return false;
            }

            if (!entry.Track.TryCancel())
            {
                return false;
            }

            RemoveArea(entry);
            AreaFateChanged?.Invoke(areaId, EBossAreaFate.Cancelled);
            return true;
        }

        /// <summary> 진행 중인 장판을 스냅샷으로 복원한다, 이미 끝났거나 소멸 연출까지 지난 것은 무시한다. 처음부터 다시 그리지 않고 서버 시각 기준으로 이어서 그린다 </summary>
        public bool RestoreArea(in BossAreaSnapshot snapshot)
        {
            int id = snapshot.Start.AreaId;
            if (_isServerBuild || IsAreaKnown(id))
            {
                return false;
            }

            if (snapshot.Fate == EBossAreaFate.Cancelled)
            {
                RememberAreaEnded(id);
                return false;
            }

            BossAreaTrack track = BossAreaTrack.FromSnapshot(in snapshot);
            if (ReadServerTime() >= (track.EndServerTime + AREA_FADE))
            {
                RememberAreaEnded(id);
                return false;
            }

            AddArea(track);
            return true;
        }

        /// <summary> 진행 중인 장판의 현재 상태, 복원에 넘길 값이다 </summary>
        public bool TryGetAreaSnapshot(int areaId, out BossAreaSnapshot snapshot)
        {
            if (TryFindArea(areaId, out AreaEntry entry))
            {
                snapshot = entry.Track.ToSnapshot();
                return true;
            }

            snapshot = default;
            return false;
        }

        /// <summary> 진행 중인 장판의 모델과 그리는 대상, 시험과 진단용이다. 풀이 비었으면 visual은 null이다 </summary>
        public bool TryGetArea(int areaId, out BossAreaTrack track, out AreaGroundVFXController visual)
        {
            if (TryFindArea(areaId, out AreaEntry entry))
            {
                track  = entry.Track;
                visual = entry.Visual;
                return true;
            }

            track  = null;
            visual = null;
            return false;
        }

        /// <summary> 진행 중인 표현의 모델과 그리는 대상, 시험과 진단용이다. 풀이 비었거나 형태에 맞지 않는 쪽은 null이다 </summary>
        public bool TryGetVisuals(int castId, out BossTelegraphCast cast, out SectorVFXController sector, out RectTelegraphVFXController rect)
        {
            if (TryFind(castId, out Entry entry))
            {
                cast   = entry.Cast;
                sector = entry.Sector;
                rect   = entry.Rect;
                return true;
            }

            cast   = null;
            sector = null;
            rect   = null;
            return false;
        }

        /// <summary> 진행 중인 표현을 모두 즉시 지우고 풀에 돌려준다. 장면 전환이나 보스전 종료 때 부른다 </summary>
        public void ClearAll()
        {
            for (int i = _active.Count - 1; i >= 0; --i)
            {
                Entry entry = _active[i];
                entry.Cast.TryCancel();
                entry.Cast.TryExpire();
                Remove(entry);
            }

            for (int i = _projectiles.Count - 1; i >= 0; --i)
            {
                _projectiles[i].Track.TryCancel();
                RemoveProjectile(_projectiles[i]);
            }

            for (int i = _areas.Count - 1; i >= 0; --i)
            {
                _areas[i].Track.TryCancel();
                RemoveArea(_areas[i]);
            }
        }

        // 표현 한 건을 갱신한다, 끝났으면 참이다
        private bool UpdateEntry(Entry entry, double now)
        {
            BossTelegraphCast cast = entry.Cast;

            if (!cast.HasExecuted)
            {
                // 예고가 끝난 뒤에도 서버 확정이 오지 않으면 지운다
                if (now >= (cast.TelegraphEndServerTime + CONFIRM_GRACE_SECONDS))
                {
                    cast.TryExpire();
                    ReleaseVisuals(entry);
                    RememberEnded(cast.CastId);
                    PhaseChanged?.Invoke(cast.CastId, EBossTelegraphPhase.Cancelled);
                    return true;
                }

                ApplyRotation(entry, false);
                if (entry.Rect != null)
                {
                    entry.Rect.SetGeometry(entry.DisplayRotation, cast.Length);
                    entry.Rect.Render(cast.GetTelegraphProgress(now), NOT_EXECUTED, 0f, NOT_EXECUTED,
                        cast.Shape == EBossTelegraphShape.Dash);
                }

                return false;
            }

            bool isDone = RenderExecuting(entry, now);
            if (!isDone)
            {
                return false;
            }

            if (cast.TryFinish())
            {
                PhaseChanged?.Invoke(cast.CastId, EBossTelegraphPhase.Finished);
            }

            ReleaseVisuals(entry);
            RememberEnded(cast.CastId);
            return true;
        }

        // 발동 뒤의 한 프레임을 그린다, 연출이 모두 끝났으면 참이다
        private bool RenderExecuting(Entry entry, double now)
        {
            BossTelegraphCast cast = entry.Cast;

            if (entry.Sector != null)
            {
                return entry.SectorCompleted;
            }

            if (entry.Rect == null)
            {
                return true;
            }

            float age = cast.GetExecuteAge(now);
            entry.Rect.SetGeometry(entry.DisplayRotation, cast.Length);

            if (cast.Shape == EBossTelegraphShape.Dash)
            {
                float ratio       = (cast.Length > 0f) ? (cast.GetDashTraveled(now) / cast.Length) : 0f;
                float dissolveAge = age - cast.GetDashEndAge();
                entry.Rect.Render(1f, age, ratio, (dissolveAge >= 0f) ? dissolveAge : NOT_EXECUTED, true);
                return dissolveAge >= entry.Rect.DissolveSeconds;
            }

            float lineDissolve = age - entry.Rect.FlashSeconds;
            entry.Rect.Render(1f, age, 0f, (lineDissolve >= 0f) ? lineDissolve : NOT_EXECUTED, false);
            return lineDissolve >= entry.Rect.DissolveSeconds;
        }

        // 표시 방향을 서버가 보낸 방향으로 맞춘다. 갱신 사이는 부드럽게 잇고 snap이면 바로 맞춘다
        private void ApplyRotation(Entry entry, bool snap)
        {
            Quaternion target = Quaternion.LookRotation(entry.Cast.Direction, Vector3.up);
            entry.DisplayRotation = snap
                ? target
                : Quaternion.RotateTowards(entry.DisplayRotation, target, AIM_SMOOTH_DEGREES * Time.deltaTime);

            if (entry.Sector != null)
            {
                entry.Sector.transform.rotation = entry.DisplayRotation;
            }
        }

        private bool Add(BossTelegraphCast cast, double now)
        {
            Entry entry = new Entry
            {
                Cast            = cast,
                DisplayRotation = Quaternion.LookRotation(cast.Direction, Vector3.up)
            };

            Show(entry, now);
            _active.Add(entry);
            enabled = true;
            PhaseChanged?.Invoke(cast.CastId, cast.Phase);
            return true;
        }

        // 형태에 맞는 연출을 풀에서 빌려 켠다. 풀이 비었거나 연결되지 않았으면 그리지 않고 모델만 따라간다
        private void Show(Entry entry, double now)
        {
            BossTelegraphCast cast = entry.Cast;

            if ((cast.Shape == EBossTelegraphShape.Fan) || (cast.Shape == EBossTelegraphShape.Circle))
            {
                if (_sectorPool == null)
                {
                    return;
                }

                SectorShape shape = SectorShape.Default;
                shape.Angle       = (cast.Shape == EBossTelegraphShape.Circle) ? FULL_CIRCLE_ANGLE : cast.ArcAngle;
                shape.Radius      = cast.Length;
                shape.InnerRadius = 0f;

                // 서버는 한 번에 타격하므로 지점별 시간차 없이 동시에 터뜨린다
                SectorBehaviour behaviour = SectorBehaviour.Default;
                behaviour.Mode              = ESectorExplosionMode.Simultaneous;
                behaviour.SegmentCount      = 1;
                behaviour.SegmentDelay      = NO_DELAY;
                behaviour.TelegraphDuration = cast.Start.TelegraphSeconds;

                // 발동을 이미 받은 스냅샷이면 충전을 마친 시점에서 흐른 만큼 이어 간다
                bool executed     = cast.HasExecuted;
                float startElapsed = executed
                    ? (cast.Start.TelegraphSeconds + cast.GetExecuteAge(now))
                    : Mathf.Max(0f, (float)(now - cast.Start.StartServerTime));

                entry.Sector = _sectorPool.Play(cast.Origin, entry.DisplayRotation, shape, behaviour, startElapsed, !executed);
                if (entry.Sector != null)
                {
                    entry.OnSectorCompleted = _ => entry.SectorCompleted = true;
                    entry.Sector.Completed += entry.OnSectorCompleted;
                }

                return;
            }

            if (_rectPool == null)
            {
                return;
            }

            entry.Rect = _rectPool.Acquire();
            if (entry.Rect != null)
            {
                entry.Rect.Show(cast.Origin, cast.Direction, cast.Width, cast.Length);
            }
        }

        // 그리던 연출을 정리한다. 부채꼴은 풀이 정상 종료를 스스로 반환하므로 멈춰 있는 것이나 취소한 것만 돌려준다
        private void ReleaseVisuals(Entry entry)
        {
            if (entry.Sector != null)
            {
                entry.Sector.Completed -= entry.OnSectorCompleted;

                // 스스로 끝난 연출은 이미 풀이 돌려받았고 다른 시전에 빌려줬을 수 있어 건드리지 않는다
                if ((_sectorPool != null) && (!entry.SectorCompleted))
                {
                    _sectorPool.Release(entry.Sector);
                }

                entry.Sector = null;
            }

            if (entry.Rect != null)
            {
                if (_rectPool != null)
                {
                    _rectPool.Release(entry.Rect);
                }

                entry.Rect = null;
            }
        }

        private void Remove(Entry entry)
        {
            ReleaseVisuals(entry);
            _active.Remove(entry);
            RememberEnded(entry.Cast.CastId);
        }

        // 같은 보스의 새 시전이 시작되면 이전 표현은 끝난 것이다. 발동한 것은 연출을 마저 하게 두지 않고 정리한다
        private void RetireSameBoss(int bossId)
        {
            for (int i = _active.Count - 1; i >= 0; --i)
            {
                Entry entry = _active[i];
                if (entry.Cast.BossId != bossId)
                {
                    continue;
                }

                if (entry.Cast.HasExecuted)
                {
                    entry.Cast.TryFinish();
                }
                else
                {
                    entry.Cast.TryCancel();
                }

                _active.RemoveAt(i);
                ReleaseVisuals(entry);
                RememberEnded(entry.Cast.CastId);
            }
        }

        private bool TryFind(int castId, out Entry entry)
        {
            for (int i = 0; i < _active.Count; ++i)
            {
                if (_active[i].Cast.CastId == castId)
                {
                    entry = _active[i];
                    return true;
                }
            }

            entry = null;
            return false;
        }

        private bool IsKnown(int castId)
        {
            return _endedIds.Contains(castId) || TryFind(castId, out Entry _);
        }

        private void RememberEnded(int castId)
        {
            if (!_endedIds.Add(castId))
            {
                return;
            }

            _endedOrder.Enqueue(castId);
            while (_endedOrder.Count > ENDED_HISTORY)
            {
                _endedIds.Remove(_endedOrder.Dequeue());
            }
        }

        private void AddProjectile(BossProjectileTrack track)
        {
            ProjectileEntry entry = new ProjectileEntry
            {
                Track    = track,
                Rotation = Quaternion.LookRotation(track.Direction, Vector3.up)
            };

            if (_rectPool != null)
            {
                entry.Rect = _rectPool.Acquire();
                if (entry.Rect != null)
                {
                    entry.Rect.Show(track.Origin, track.Direction, track.Width, PROJECTILE_HEAD_LENGTH);
                }
            }

            _projectiles.Add(entry);
            enabled = true;
        }

        // 투사체 한 프레임, 소멸 연출까지 끝났으면 참이다. 경로 길이에 닿으면 서버 알림이 없어도 소멸 연출을 시작한다
        private bool UpdateProjectile(ProjectileEntry entry, double now)
        {
            BossProjectileTrack track = entry.Track;
            float distance = track.GetDistance(now);

            if ((!entry.EndSeen) && track.HasReachedEnd(now))
            {
                entry.EndSeen   = true;
                entry.EndSeenAt = now;
            }

            float dissolveAge = entry.EndSeen ? Mathf.Max(0f, (float)(now - entry.EndSeenAt)) : NOT_EXECUTED;
            bool  isDone      = entry.EndSeen && ((entry.Rect == null) || (dissolveAge >= entry.Rect.DissolveSeconds));

            if (entry.Rect != null)
            {
                float length = Mathf.Min(PROJECTILE_HEAD_LENGTH, distance);
                Vector3 tail = track.Origin + (track.Direction * (distance - length));
                entry.Rect.SetPose(tail, entry.Rotation, length);
                entry.Rect.Render(1f, Mathf.Max(0f, (float)(now - track.LaunchServerTime)), 1f, dissolveAge, true);
            }

            if (isDone)
            {
                RemoveProjectile(entry, false);
            }

            return isDone;
        }

        private void RemoveProjectile(ProjectileEntry entry, bool removeFromList = true)
        {
            if ((entry.Rect != null) && (_rectPool != null))
            {
                _rectPool.Release(entry.Rect);
            }

            entry.Rect = null;
            if (removeFromList)
            {
                _projectiles.Remove(entry);
            }

            RememberProjectileEnded(entry.Track.ProjectileId);
        }

        private bool TryFindProjectile(int projectileId, out ProjectileEntry entry)
        {
            for (int i = 0; i < _projectiles.Count; ++i)
            {
                if (_projectiles[i].Track.ProjectileId == projectileId)
                {
                    entry = _projectiles[i];
                    return true;
                }
            }

            entry = null;
            return false;
        }

        private bool IsProjectileKnown(int projectileId)
        {
            return _projectileEndedIds.Contains(projectileId) || TryFindProjectile(projectileId, out ProjectileEntry _);
        }

        private void RememberProjectileEnded(int projectileId)
        {
            if (!_projectileEndedIds.Add(projectileId))
            {
                return;
            }

            _projectileEndedOrder.Enqueue(projectileId);
            while (_projectileEndedOrder.Count > ENDED_HISTORY)
            {
                _projectileEndedIds.Remove(_projectileEndedOrder.Dequeue());
            }
        }

        private void AddArea(BossAreaTrack track)
        {
            AreaEntry entry = new AreaEntry { Track = track };

            if (_areaPool != null)
            {
                entry.Visual = _areaPool.Acquire();
                if (entry.Visual != null)
                {
                    entry.Visual.Show(track.Center, track.Radius);
                }
            }

            _areas.Add(entry);
            enabled = true;
        }

        // 장판 한 프레임, 옅어지는 연출까지 끝났으면 참이다. 지속 시간이 지나면 서버 알림이 없어도 옅어지며 사라진다
        private bool UpdateArea(AreaEntry entry, double now)
        {
            BossAreaTrack track = entry.Track;

            if ((!entry.EndSeen) && (track.IsEnded || track.HasExpiredByTime(now)))
            {
                entry.EndSeen   = true;
                entry.EndSeenAt = now;
            }

            float fadeAge = entry.EndSeen ? Mathf.Max(0f, (float)(now - entry.EndSeenAt)) : 0f;
            float fade    = entry.EndSeen ? (1f - Mathf.Clamp01(fadeAge / AREA_FADE)) : 1f;
            bool  isDone  = entry.EndSeen && (fadeAge >= AREA_FADE);

            if (entry.Visual != null)
            {
                entry.Visual.Render(track.GetLifeRatio(now), track.GetPulseAge(now), fade);
            }

            if (isDone)
            {
                RemoveArea(entry, false);
            }

            return isDone;
        }

        private void RemoveArea(AreaEntry entry, bool removeFromList = true)
        {
            if ((entry.Visual != null) && (_areaPool != null))
            {
                _areaPool.Release(entry.Visual);
            }

            entry.Visual = null;
            if (removeFromList)
            {
                _areas.Remove(entry);
            }

            RememberAreaEnded(entry.Track.AreaId);
        }

        private bool TryFindArea(int areaId, out AreaEntry entry)
        {
            for (int i = 0; i < _areas.Count; ++i)
            {
                if (_areas[i].Track.AreaId == areaId)
                {
                    entry = _areas[i];
                    return true;
                }
            }

            entry = null;
            return false;
        }

        private bool IsAreaKnown(int areaId)
        {
            return _areaEndedIds.Contains(areaId) || TryFindArea(areaId, out AreaEntry _);
        }

        private void RememberAreaEnded(int areaId)
        {
            if (!_areaEndedIds.Add(areaId))
            {
                return;
            }

            _areaEndedOrder.Enqueue(areaId);
            while (_areaEndedOrder.Count > ENDED_HISTORY)
            {
                _areaEndedIds.Remove(_areaEndedOrder.Dequeue());
            }
        }

        private double ReadServerTime()
        {
            return (_serverClock != null) ? _serverClock() : Time.timeAsDouble;
        }
    }
}
