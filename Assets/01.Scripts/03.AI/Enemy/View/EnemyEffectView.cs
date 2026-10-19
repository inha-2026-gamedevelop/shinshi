// Unity
using UnityEngine;

// 서드파티
using Mirror;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.AI
{
    /// <summary> 일반 적 효과 개체의 클라이언트 표현, 출현 예고 고리와 투사체 한 발을 서버 시각으로 그린다 </summary>
    [RequireComponent(typeof(EnemyEffectState))]
    public sealed class EnemyEffectView : ClientView
    {
        // 투사체는 BossProjectileTrack으로 속도와 경로 길이에서 위치를 센다. 충돌과 벽은 서버가 정하고 여기서는 그리기만 한다.
        // 풀에서 다시 켜질 때 트랙과 표시를 지운다

        /****************************************
        *                Fields
        ****************************************/

        private const float RING_WIDTH = 0.08f;
        private const float BOLT_LIFT  = 1f;

        [SerializeField] private LineRenderer _ring;
        [SerializeField] private Transform    _bolt;

        private readonly Vector3[] _ringPoints = new Vector3[EnemyTelegraphGeometry.MAX_POINTS];

        private EnemyEffectState    _state;
        private BossProjectileTrack _track;
        private int                 _trackEffectId;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            _state = GetComponent<EnemyEffectState>();
            HideAll();
        }

        private void OnEnable()
        {
            _track         = null;
            _trackEffectId = 0;
            HideAll();
        }

        private void OnDisable()
        {
            _track = null;
            HideAll();
        }

        private void Update()
        {
            if (_state == null)
            {
                return;
            }

            switch (_state.Kind)
            {
                case EEnemyEffectKind.SpawnPreview:
                    DrawPreview();
                    break;
                case EEnemyEffectKind.Projectile:
                    DrawProjectile();
                    break;
                default:
                    HideAll();
                    break;
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void DrawPreview()
        {
            if (_bolt != null)
            {
                _bolt.gameObject.SetActive(false);
            }

            if (_ring == null)
            {
                return;
            }

            EnemyTelegraphCue cue = new EnemyTelegraphCue(0, EEnemyAttackShape.Circle, _state.Origin, Vector3.forward, _state.Size, 0f,
                _state.StartServerTime, _state.EndServerTime);
            int count = EnemyTelegraphGeometry.Fill(_ringPoints, cue);
            if (count <= 0)
            {
                _ring.enabled = false;
                return;
            }

            float progress = cue.Progress(NetworkTime.time);
            Color color    = Color.Lerp(EnemyRoleColors.Get(_state.Role), Color.white, progress);
            float width    = RING_WIDTH * (1f + progress);

            _ring.positionCount = count;
            _ring.SetPositions(_ringPoints);
            _ring.startColor = color;
            _ring.endColor   = color;
            _ring.startWidth = width;
            _ring.endWidth   = width;
            _ring.enabled    = true;
        }

        private void DrawProjectile()
        {
            if (_ring != null)
            {
                _ring.enabled = false;
            }

            if (_bolt == null)
            {
                return;
            }

            if ((_track == null) || (_trackEffectId != _state.EffectId))
            {
                BossProjectileStart start = new BossProjectileStart(0, _state.EffectId, _state.Origin, _state.Direction, _state.Speed,
                    _state.Size, _state.PathLength, _state.StartServerTime);
                _track         = new BossProjectileTrack(start);
                _trackEffectId = _state.EffectId;
            }

            double now = NetworkTime.time;
            if ((now < _track.LaunchServerTime) || _track.HasReachedEnd(now))
            {
                _bolt.gameObject.SetActive(false);
                return;
            }

            _bolt.position = _track.GetPosition(now) + (Vector3.up * BOLT_LIFT);
            _bolt.gameObject.SetActive(true);
        }

        private void HideAll()
        {
            if (_ring != null)
            {
                _ring.enabled = false;
            }

            if (_bolt != null)
            {
                _bolt.gameObject.SetActive(false);
            }
        }
    }
}
