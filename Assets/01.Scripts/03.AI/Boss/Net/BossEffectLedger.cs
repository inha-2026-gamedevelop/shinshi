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
    // 서버는 사건을 그대로 받아 지금 진행 중인 것만 남기고, 새 관찰자가 생기면(늦은 접속, 재접속) 스폰 초기 상태에 실어 보낸다.
    // 클라이언트도 같은 사건으로 같은 기록을 이어 가므로, 수신부가 늦게 연결돼도 지금 상태를 복원으로 넘길 수 있다.
    // 끝난 공격(정상 종료, 취소), 발사와 활성으로 넘어간 예고, 소멸한 투사체와 장판은 바로 지워 복원에 들어가지 않는다.
    // 판정은 하지 않는다. 받은 값을 바꾸지 않고 담기만 하며 돌진 정지 거리만 표현 계약(스냅샷)의 정의대로 환산한다
    // 2페이즈 빔과 별형 빔은 예고부터 소멸까지 남기고, 반월 연쇄는 짧은 시전이라 남기지 않는다.
    // 50% 결계는 전투에 하나뿐이고 예고부터 정리까지 남긴다. 단계는 시작 값의 시각으로 정해지므로 시작 값만 담는다
    /// <summary> 보스 한 개체의 진행 중인 예고, 투사체, 장판, 빔, 별형 빔, 결계 기록, 늦은 접속 복원에 쓴다 </summary>
    public sealed class BossEffectLedger : IBossEventListener
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float NOT_STOPPED = -1f;

        /****************************************
        *                Fields
        ****************************************/

        private readonly List<BossProjectileSnapshot> _projectiles =
            new List<BossProjectileSnapshot>(Constants.Network.BOSS_RESTORE_MAX_PROJECTILES);
        private readonly List<BossAreaSnapshot>       _areas       =
            new List<BossAreaSnapshot>(Constants.Network.BOSS_RESTORE_MAX_AREAS);
        private readonly List<BossBeamSnapshot>       _beams       =
            new List<BossBeamSnapshot>(Constants.Network.BOSS_RESTORE_MAX_BEAMS);
        private readonly List<BossStarSnapshot>       _stars       =
            new List<BossStarSnapshot>(Constants.Network.BOSS_RESTORE_MAX_STARS);

        private BossTelegraphSnapshot _cast;
        private bool                  _hasCast    = false;
        private BossBarrierStart      _barrier;
        private bool                  _hasBarrier = false;

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 진행 중인 예고가 있다, 보스는 한 번에 한 시전만 한다 </summary>
        public bool HasCast => _hasCast;

        public BossTelegraphSnapshot Cast => _cast;

        public int ProjectileCount => _projectiles.Count;

        public int AreaCount => _areas.Count;

        /// <summary> 예고 중이거나 활성된 빔 수 </summary>
        public int BeamCount => _beams.Count;

        /// <summary> 예고 중이거나 활성된 별형 빔 구조 수 </summary>
        public int StarCount => _stars.Count;

        /// <summary> 예고 중이거나 활성된 결계 위험 구역이 있다 </summary>
        public bool HasBarrier => _hasBarrier;

        public BossBarrierStart Barrier => _barrier;

        /****************************************
        *                Methods
        ****************************************/

        public BossProjectileSnapshot GetProjectile(int index)
        {
            return _projectiles[index];
        }

        public BossAreaSnapshot GetArea(int index)
        {
            return _areas[index];
        }

        public BossBeamSnapshot GetBeam(int index)
        {
            return _beams[index];
        }

        public BossStarSnapshot GetStar(int index)
        {
            return _stars[index];
        }

        public void Clear()
        {
            _hasCast    = false;
            _cast       = default;
            _hasBarrier = false;
            _barrier    = default;
            _projectiles.Clear();
            _areas.Clear();
            _beams.Clear();
            _stars.Clear();
        }

        // 같은 보스의 새 시전은 이전 예고를 대신한다. 표현도 같은 보스의 새 시전이 오면 이전 표현을 끝낸다
        public void OnTelegraphStarted(in BossTelegraphStart start)
        {
            _cast = new BossTelegraphSnapshot
            {
                Start               = start,
                Direction           = start.Direction,
                Length              = start.Length,
                IsDirectionLocked   = false,
                Phase                 = EBossTelegraphPhase.Telegraph,
                DashStoppedDistance   = NOT_STOPPED,
                DashStoppedServerTime = double.NaN
            };
            _hasCast = true;
        }

        public void OnAimChanged(int castId, Vector3 direction, float length, double serverTime)
        {
            if ((!IsCurrentCast(castId)) || (_cast.IsDirectionLocked) || (_cast.Phase != EBossTelegraphPhase.Telegraph))
            {
                return;
            }

            _cast.Direction = direction;
            _cast.Length    = length;
        }

        public void OnDirectionLocked(int castId, Vector3 direction, float length)
        {
            if (!IsCurrentCast(castId))
            {
                return;
            }

            _cast.Direction         = direction;
            _cast.Length            = length;
            _cast.IsDirectionLocked = true;
        }

        public void OnStruck(int castId, double serverTime)
        {
            if ((!IsCurrentCast(castId)) || (_cast.Phase != EBossTelegraphPhase.Telegraph))
            {
                return;
            }

            _cast.Phase             = EBossTelegraphPhase.Execute;
            _cast.ExecuteServerTime = serverTime;
            _cast.IsDirectionLocked = true;
        }

        public void OnDashStarted(int castId, float distance, float seconds, float speed, double startServerTime)
        {
            if ((!IsCurrentCast(castId)) || (_cast.Phase != EBossTelegraphPhase.Telegraph))
            {
                return;
            }

            _cast.Phase               = EBossTelegraphPhase.Execute;
            _cast.ExecuteServerTime   = startServerTime;
            _cast.IsDirectionLocked   = true;
            _cast.DashDistance        = distance;
            _cast.DashSeconds         = seconds;
            _cast.DashSpeed           = speed;
            _cast.DashStoppedDistance   = NOT_STOPPED;
            _cast.DashStoppedByWall     = false;
            _cast.DashStoppedServerTime = double.NaN;
        }

        // 정지 거리는 예고 기준점에서 고정 방향으로 잰 거리다. 표현의 돌진 정지 처리와 같은 정의다
        public void OnDashStopped(int castId, Vector3 position, bool stoppedByWall, double stopServerTime)
        {
            if ((!IsCurrentCast(castId)) || (_cast.Phase != EBossTelegraphPhase.Execute))
            {
                return;
            }

            Vector3 offset    = position - _cast.Start.Origin;
            Vector3 direction = _cast.Direction;
            float   along     = (offset.x * direction.x) + (offset.z * direction.z);

            _cast.DashStoppedDistance   = Mathf.Clamp(along, 0f, _cast.DashDistance);
            _cast.DashStoppedByWall     = stoppedByWall;
            _cast.DashStoppedServerTime = stopServerTime;
        }

        public void OnFinished(int castId)
        {
            EndCast(castId);
        }

        public void OnCancelled(int castId)
        {
            EndCast(castId);
        }

        // 발사하면 같은 번호의 예고는 끝나고 투사체가 따로 남는다
        public void OnProjectileLaunched(in BossProjectileStart start)
        {
            EndCast(start.ProjectileId);
            RemoveProjectile(start.ProjectileId);

            if (_projectiles.Count >= _projectiles.Capacity)
            {
                Debug.LogError($"[Boss] 복원 기록의 투사체가 가득 차 늦은 접속에 넘기지 못한다, projectileId={start.ProjectileId}");
                return;
            }

            _projectiles.Add(new BossProjectileSnapshot
            {
                Start = start,
                Fate  = EBossProjectileFate.Flying
            });
        }

        public void OnProjectileEnded(int projectileId, EBossProjectileFate fate, Vector3 position)
        {
            RemoveProjectile(projectileId);
        }

        // 활성하면 같은 번호의 예고는 끝나고 장판이 따로 남는다
        public void OnAreaActivated(in BossAreaStart start)
        {
            EndCast(start.AreaId);
            RemoveArea(start.AreaId);

            if (_areas.Count >= _areas.Capacity)
            {
                Debug.LogError($"[Boss] 복원 기록의 장판이 가득 차 늦은 접속에 넘기지 못한다, areaId={start.AreaId}");
                return;
            }

            _areas.Add(new BossAreaSnapshot
            {
                Start         = start,
                Fate          = EBossAreaFate.Active,
                LastTickIndex = -1
            });
        }

        public void OnAreaTicked(int areaId, int hitIndex, double roundServerTime)
        {
            int index = IndexOfArea(areaId);
            if (index < 0)
            {
                return;
            }

            BossAreaSnapshot area = _areas[index];
            if (hitIndex <= area.LastTickIndex)
            {
                return;
            }

            area.LastTickIndex      = hitIndex;
            area.LastTickServerTime = roundServerTime;
            _areas[index]           = area;
        }

        public void OnAreaEnded(int areaId, EBossAreaFate fate)
        {
            RemoveArea(areaId);
        }

        // 반월 연쇄는 여우불 이동과 구역 예고 넷이 함께 가는 짧은 시전이라 복원에 넣지 않는다. 늦게 들어온 관찰자는 다음 시전부터 본다
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

        // 빔은 예고부터 기록한다. 예고 중인 빔은 활성 여부만 거짓으로 남긴다
        public void OnBeamTelegraphStarted(in BossBeamStart start)
        {
            RemoveBeam(start.BeamId);

            if (_beams.Count >= _beams.Capacity)
            {
                Debug.LogError($"[Boss] 복원 기록의 빔이 가득 차 늦은 접속에 넘기지 못한다, beamId={start.BeamId}");
                return;
            }

            _beams.Add(new BossBeamSnapshot
            {
                Start         = start,
                IsActivated   = false,
                Fate          = EBossAreaFate.Active,
                LastTickIndex = -1
            });
        }

        public void OnBeamCancelled(int castId, int beamId)
        {
            RemoveBeam(beamId);
        }

        public void OnBeamActivated(int beamId, double activateServerTime)
        {
            int index = IndexOfBeam(beamId);
            if (index < 0)
            {
                return;
            }

            BossBeamSnapshot beam = _beams[index];
            if (beam.IsActivated)
            {
                return;
            }

            beam.IsActivated        = true;
            beam.ActivateServerTime = activateServerTime;
            _beams[index]           = beam;
        }

        public void OnBeamTicked(int beamId, int hitIndex, double roundServerTime)
        {
            int index = IndexOfBeam(beamId);
            if (index < 0)
            {
                return;
            }

            BossBeamSnapshot beam = _beams[index];
            if (hitIndex <= beam.LastTickIndex)
            {
                return;
            }

            beam.LastTickIndex      = hitIndex;
            beam.LastTickServerTime = roundServerTime;
            _beams[index]           = beam;
        }

        public void OnBeamEnded(int beamId, EBossAreaFate fate)
        {
            RemoveBeam(beamId);
        }

        public void OnStarTelegraphStarted(in BossStarStart start)
        {
            RemoveStar(start.StarId);

            if (_stars.Count >= _stars.Capacity)
            {
                Debug.LogError($"[Boss] 복원 기록의 별형 빔이 가득 차 늦은 접속에 넘기지 못한다, starId={start.StarId}");
                return;
            }

            _stars.Add(new BossStarSnapshot
            {
                Start         = start,
                IsActivated   = false,
                Fate          = EBossAreaFate.Active,
                LastTickIndex = -1
            });
        }

        public void OnStarCancelled(int castId, int starId)
        {
            RemoveStar(starId);
        }

        public void OnStarActivated(int starId, double activateServerTime)
        {
            int index = IndexOfStar(starId);
            if (index < 0)
            {
                return;
            }

            BossStarSnapshot star = _stars[index];
            if (star.IsActivated)
            {
                return;
            }

            star.IsActivated        = true;
            star.ActivateServerTime = activateServerTime;
            _stars[index]           = star;
        }

        public void OnStarTicked(int starId, int hitIndex, double roundServerTime)
        {
            int index = IndexOfStar(starId);
            if (index < 0)
            {
                return;
            }

            BossStarSnapshot star = _stars[index];
            if (hitIndex <= star.LastTickIndex)
            {
                return;
            }

            star.LastTickIndex      = hitIndex;
            star.LastTickServerTime = roundServerTime;
            _stars[index]           = star;
        }

        public void OnStarEnded(int starId, EBossAreaFate fate)
        {
            RemoveStar(starId);
        }

        // 리셋 뒤 다시 시작하면 다른 번호의 새 결계가 이전 기록을 대신한다
        public void OnBarrierStarted(in BossBarrierStart start)
        {
            _barrier    = start;
            _hasBarrier = true;
        }

        // 활성과 완료는 시작 값의 시각으로 재현되므로 따로 남기지 않는다
        public void OnBarrierActivated(int barrierId, double activateServerTime)
        {
        }

        public void OnBarrierCompleted(int barrierId, double completeServerTime)
        {
        }

        public void OnBarrierCleared(int barrierId)
        {
            if ((!_hasBarrier) || (_barrier.BarrierId != barrierId))
            {
                return;
            }

            _hasBarrier = false;
            _barrier    = default;
        }

        /// <summary> 진행 중인 것을 모두 복원으로 넘긴다 </summary>
        public void Replay(IBossStateListener listener)
        {
            if (listener == null)
            {
                return;
            }

            if (_hasCast)
            {
                listener.OnTelegraphRestored(in _cast);
            }

            for (int i = 0; i < _projectiles.Count; ++i)
            {
                BossProjectileSnapshot projectile = _projectiles[i];
                listener.OnProjectileRestored(in projectile);
            }

            for (int i = 0; i < _areas.Count; ++i)
            {
                BossAreaSnapshot area = _areas[i];
                listener.OnAreaRestored(in area);
            }

            for (int i = 0; i < _beams.Count; ++i)
            {
                BossBeamSnapshot beam = _beams[i];
                listener.OnBeamRestored(in beam);
            }

            for (int i = 0; i < _stars.Count; ++i)
            {
                BossStarSnapshot star = _stars[i];
                listener.OnStarRestored(in star);
            }

            if (_hasBarrier)
            {
                listener.OnBarrierRestored(in _barrier);
            }
        }

        // 순서를 바꾸면 Read도 함께 바꾼다
        public void Write(NetworkWriter writer)
        {
            writer.WriteBool(_hasCast);
            if (_hasCast)
            {
                writer.WriteBossTelegraphSnapshot(_cast);
            }

            writer.WriteVarUInt((uint)_projectiles.Count);
            for (int i = 0; i < _projectiles.Count; ++i)
            {
                writer.WriteBossProjectileSnapshot(_projectiles[i]);
            }

            writer.WriteVarUInt((uint)_areas.Count);
            for (int i = 0; i < _areas.Count; ++i)
            {
                writer.WriteBossAreaSnapshot(_areas[i]);
            }

            writer.WriteVarUInt((uint)_beams.Count);
            for (int i = 0; i < _beams.Count; ++i)
            {
                writer.WriteBossBeamSnapshot(_beams[i]);
            }

            writer.WriteVarUInt((uint)_stars.Count);
            for (int i = 0; i < _stars.Count; ++i)
            {
                writer.WriteBossStarSnapshot(_stars[i]);
            }

            writer.WriteBool(_hasBarrier);
            if (_hasBarrier)
            {
                writer.WriteBossBarrierStart(_barrier);
            }
        }

        // 기존 기록을 버리고 받은 값으로 바꾼다. 상한을 넘는 개수는 서버가 쓰지 않으므로 프로토콜 불일치로 보고 멈춘다
        public void Read(NetworkReader reader)
        {
            Clear();

            _hasCast = reader.ReadBool();
            if (_hasCast)
            {
                _cast = reader.ReadBossTelegraphSnapshot();
            }

            uint projectileCount = reader.ReadVarUInt();
            if (projectileCount > (uint)_projectiles.Capacity)
            {
                throw new InvalidDataException($"보스 복원 투사체 수가 상한을 넘는다, count={projectileCount}");
            }

            for (uint i = 0; i < projectileCount; ++i)
            {
                _projectiles.Add(reader.ReadBossProjectileSnapshot());
            }

            uint areaCount = reader.ReadVarUInt();
            if (areaCount > (uint)_areas.Capacity)
            {
                throw new InvalidDataException($"보스 복원 장판 수가 상한을 넘는다, count={areaCount}");
            }

            for (uint i = 0; i < areaCount; ++i)
            {
                _areas.Add(reader.ReadBossAreaSnapshot());
            }

            uint beamCount = reader.ReadVarUInt();
            if (beamCount > (uint)_beams.Capacity)
            {
                throw new InvalidDataException($"보스 복원 빔 수가 상한을 넘는다, count={beamCount}");
            }

            for (uint i = 0; i < beamCount; ++i)
            {
                _beams.Add(reader.ReadBossBeamSnapshot());
            }

            uint starCount = reader.ReadVarUInt();
            if (starCount > (uint)_stars.Capacity)
            {
                throw new InvalidDataException($"보스 복원 별형 빔 수가 상한을 넘는다, count={starCount}");
            }

            for (uint i = 0; i < starCount; ++i)
            {
                _stars.Add(reader.ReadBossStarSnapshot());
            }

            _hasBarrier = reader.ReadBool();
            if (_hasBarrier)
            {
                _barrier = reader.ReadBossBarrierStart();
            }
        }

        private bool IsCurrentCast(int castId)
        {
            return (_hasCast) && (_cast.Start.CastId == castId);
        }

        private void EndCast(int castId)
        {
            if (!IsCurrentCast(castId))
            {
                return;
            }

            _hasCast = false;
            _cast    = default;
        }

        private void RemoveProjectile(int projectileId)
        {
            for (int i = _projectiles.Count - 1; i >= 0; --i)
            {
                if (_projectiles[i].Start.ProjectileId == projectileId)
                {
                    _projectiles.RemoveAt(i);
                }
            }
        }

        private void RemoveArea(int areaId)
        {
            int index = IndexOfArea(areaId);
            if (index >= 0)
            {
                _areas.RemoveAt(index);
            }
        }

        private int IndexOfArea(int areaId)
        {
            for (int i = 0; i < _areas.Count; ++i)
            {
                if (_areas[i].Start.AreaId == areaId)
                {
                    return i;
                }
            }

            return -1;
        }

        private void RemoveBeam(int beamId)
        {
            int index = IndexOfBeam(beamId);
            if (index >= 0)
            {
                _beams.RemoveAt(index);
            }
        }

        private int IndexOfBeam(int beamId)
        {
            for (int i = 0; i < _beams.Count; ++i)
            {
                if (_beams[i].Start.BeamId == beamId)
                {
                    return i;
                }
            }

            return -1;
        }

        private void RemoveStar(int starId)
        {
            int index = IndexOfStar(starId);
            if (index >= 0)
            {
                _stars.RemoveAt(index);
            }
        }

        private int IndexOfStar(int starId)
        {
            for (int i = 0; i < _stars.Count; ++i)
            {
                if (_stars[i].Start.StarId == starId)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
