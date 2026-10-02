// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 서버 알림을 받아 투사체 표현 한 발의 상태를 정하는 순수 모델이다. 서버가 보낸 속도와 경로 길이로만 위치를 세고 충돌과 벽을 계산하지 않는다
    // 서버가 소멸을 알리기 전에도 경로 길이에서 멈춘다. 그래서 알림이 늦거나 유실돼도 벽을 지나 날아가 보이지 않는다
    /// <summary> 투사체 한 발의 위치와 상태 </summary>
    public sealed class BossProjectileTrack
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly BossProjectileStart _start;
        private readonly Vector3             _direction;
        private readonly float               _pathLength;

        private EBossProjectileFate _fate;
        private float               _endDistance;

        /****************************************
        *              Properties
        ****************************************/

        public BossProjectileStart Start => _start;

        public int     BossId       => _start.BossId;
        public int     ProjectileId => _start.ProjectileId;
        public Vector3 Origin       => _start.Origin;
        public Vector3 Direction    => _direction;
        public float   Speed        => _start.Speed;
        public float   Width        => _start.Width;
        public float   PathLength   => _pathLength;
        public double  LaunchServerTime => _start.LaunchServerTime;

        public EBossProjectileFate Fate => _fate;

        /// <summary> 서버가 소멸을 알렸다, 날아 있는 중이 아니다 </summary>
        public bool IsEnded => _fate != EBossProjectileFate.Flying;

        /// <summary> 경로 길이에 닿는 데 걸리는 비행 시간(초) </summary>
        public float FlightSeconds => (_start.Speed > 0f) ? (_pathLength / _start.Speed) : 0f;

        /****************************************
        *                Methods
        ****************************************/

        public BossProjectileTrack(in BossProjectileStart start)
        {
            _start      = start;
            _direction  = BossTelegraphRules.Flatten(start.Direction, Vector3.forward);
            _pathLength = Mathf.Max(0f, start.PathLength);
            _fate       = EBossProjectileFate.Flying;
        }

        /// <summary> 스냅샷으로 복원한다 </summary>
        public static BossProjectileTrack FromSnapshot(in BossProjectileSnapshot snapshot)
        {
            BossProjectileTrack track = new BossProjectileTrack(snapshot.Start);
            track._fate        = snapshot.Fate;
            track._endDistance = Mathf.Clamp(snapshot.EndDistance, 0f, track._pathLength);
            return track;
        }

        public BossProjectileSnapshot ToSnapshot()
        {
            return new BossProjectileSnapshot { Start = _start, Fate = _fate, EndDistance = _endDistance };
        }

        /// <summary> 발사 위치에서 지금까지 날아간 거리, 서버가 소멸을 알렸으면 그 거리이고 아니면 일정한 속도로 세되 경로 길이를 넘지 않는다 </summary>
        public float GetDistance(double serverNow)
        {
            if (IsEnded)
            {
                return _endDistance;
            }

            double age = serverNow - _start.LaunchServerTime;
            if ((age <= 0.0) || (_start.Speed <= 0f))
            {
                return 0f;
            }

            return Mathf.Min(_pathLength, (float)(_start.Speed * age));
        }

        public Vector3 GetPosition(double serverNow)
        {
            return _start.Origin + (_direction * GetDistance(serverNow));
        }

        /// <summary> 날아서 경로 길이에 닿았다, 서버 알림이 없어도 닿은 것으로 보여 준다 </summary>
        public bool HasReachedEnd(double serverNow)
        {
            return IsEnded || (GetDistance(serverNow) >= (_pathLength - 0.0001f));
        }

        /// <summary> 서버가 알린 소멸, 날고 있을 때만 받아들인다. 위치는 발사 위치에서 방향으로 잰 거리로 바꿔 둔다 </summary>
        public bool TryEnd(EBossProjectileFate fate, Vector3 position)
        {
            if ((fate == EBossProjectileFate.Flying) || IsEnded)
            {
                return false;
            }

            Vector3 offset = position - _start.Origin;
            offset.y = 0f;

            _endDistance = Mathf.Clamp((offset.x * _direction.x) + (offset.z * _direction.z), 0f, _pathLength);
            _fate        = fate;
            return true;
        }

        /// <summary> 복원 값이 끝난 뒤라 지운다 </summary>
        public bool TryCancel()
        {
            if (IsEnded)
            {
                return false;
            }

            _endDistance = 0f;
            _fate        = EBossProjectileFate.Cancelled;
            return true;
        }
    }
}
