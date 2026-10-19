// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.AI
{
    /// <summary> 일반 적의 출현 예고 표시와 투사체 한 발을 클라이언트에 보이게 하는 가벼운 네트워크 개체, 판정은 서버가 한다 </summary>
    public sealed class EnemyEffectState : NetworkBehaviour
    {
        // 서버 스포너(EnemyNetworkSpawner)가 효과 하나마다 풀에서 꺼내 Setup 뒤 스폰하고, 끝나면 UnSpawn해 풀로 돌려보낸다.
        // 클라이언트가 보는 것은 스폰 초기 상태의 SyncVar뿐이라 늦은 접속과 재접속도 지금 떠 있는 효과를 그대로 받는다.
        // 시각은 서버 시각이고 투사체 위치는 클라이언트가 BossProjectileTrack으로 속도와 경로 길이에서 센다. 충돌과 벽은 다시 계산하지 않는다.
        // 위치를 계속 보내지 않으므로 이 개체에는 NetworkTransform을 붙이지 않는다

        /****************************************
        *                Fields
        ****************************************/

        [SyncVar] private EEnemyEffectKind _kind;
        [SyncVar] private int              _effectId;
        [SyncVar] private EEnemyRole       _role;
        [SyncVar] private Vector3          _origin;
        [SyncVar] private Vector3          _direction;
        [SyncVar] private float            _speed;
        [SyncVar] private float            _size;
        [SyncVar] private float            _pathLength;
        [SyncVar] private double           _startServerTime;
        [SyncVar] private double           _endServerTime;

        /****************************************
        *              Properties
        ****************************************/

        public EEnemyEffectKind Kind => _kind;

        /// <summary> 출현 예고는 서버가 준 예고 번호, 투사체는 투사체 번호 </summary>
        public int EffectId => _effectId;

        /// <summary> 출현 예고에서 나타날 적의 종류, 표시 색에 쓴다 </summary>
        public EEnemyRole Role => _role;

        public Vector3 Origin    => _origin;
        public Vector3 Direction => _direction;
        public float   Speed     => _speed;

        /// <summary> 출현 예고는 표시 반경, 투사체는 폭(m) </summary>
        public float Size => _size;

        public float  PathLength      => _pathLength;
        public double StartServerTime => _startServerTime;

        /// <summary> 출현 예고는 적이 나타나는 시각, 투사체는 NaN이다 </summary>
        public double EndServerTime => _endServerTime;

        /****************************************
        *                Server
        ****************************************/

        // 스폰 전에 불러야 첫 관찰자부터 값을 받는다
        [Server]
        public void SetupSpawnPreview(int previewId, EEnemyRole role, Vector3 position, float radius, double startServerTime,
            double endServerTime)
        {
            _kind            = EEnemyEffectKind.SpawnPreview;
            _effectId        = previewId;
            _role            = role;
            _origin          = position;
            _direction       = Vector3.forward;
            _speed           = 0f;
            _size            = radius;
            _pathLength      = 0f;
            _startServerTime = startServerTime;
            _endServerTime   = endServerTime;
        }

        [Server]
        public void SetupProjectile(in BossProjectileStart start)
        {
            _kind            = EEnemyEffectKind.Projectile;
            _effectId        = start.ProjectileId;
            _role            = EEnemyRole.Ranged;
            _origin          = start.Origin;
            _direction       = start.Direction;
            _speed           = start.Speed;
            _size            = start.Width;
            _pathLength      = start.PathLength;
            _startServerTime = start.LaunchServerTime;
            _endServerTime   = double.NaN;
        }

        /// <summary> 풀로 돌려보낼 때 지운다 </summary>
        [Server]
        public void Clear()
        {
            _kind     = EEnemyEffectKind.None;
            _effectId = 0;
        }
    }
}
