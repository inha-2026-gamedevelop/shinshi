// Unity
using UnityEngine;

// 서드파티
using DG.Tweening;
using Unity.Cinemachine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Art
{
    /// <summary> 캐릭터에 붙어 넓게 베기 연출을 재생하는 진입점, 애니메이터 상태와 타격 확정 View가 부른다 </summary>
    public sealed class SlashVfxEmitter : ClientView
    {
        // 히트스톱과 카메라 임펄스는 이 클라이언트의 화면에서만 쓰는 표현이다. 서버 판정 시각과
        // 이동 예측은 애니메이터 속도를 읽지 않으므로 잠깐 늦춰도 판정이 어긋나지 않는다.
        // 2~4인 협동에서 남의 공격으로 화면이 흔들리지 않게 자기 캐릭터의 타격에만 쓰고,
        // 한 번 휘두른 기술이 여러 대상을 맞혀도 경직과 임펄스는 첫 타격에 한 번만 낸다
        // TODO(민성): 공용 로컬 풀이 생기면 고정 인스턴스 대신 그 풀에서 빌리고 돌려준다

        /****************************************
        *                Fields
        ****************************************/

        private const float NORMAL_SPEED    = 1f;
        private const float FULL_BRIGHTNESS = 1f;

        [Header("연출")]
        [SerializeField]         private WideSlashVfx _wideSlashPrefab;
        [SerializeField, Min(1)] private int          _poolSize = 2;

        // 칼끝 소켓. 무기 모델이 없으면 오른손 뼈를 넣는다
        [SerializeField] private Transform _bladeAnchor;

        [Header("타격감")]
        [SerializeField]                private Animator                 _animator;
        [SerializeField]                private CinemachineImpulseSource _impulseSource;
        [SerializeField, Min(0f)]       private float                    _hitStopDuration = 0.05f;
        [SerializeField, Range(0f, 1f)] private float                    _hitStopSpeed    = 0.05f;
        [SerializeField, Min(0f)]       private float                    _impulseForce    = 0.08f;

        // 남의 캐릭터 연출, 효과 밝기 낮추기 설정이 이 값을 낮춘다
        [SerializeField, Range(0f, 1f)] private float _brightnessScale = FULL_BRIGHTNESS;

        private WideSlashVfx[] _pool;
        private WideSlashVfx   _current;
        private Sequence       _hitStop;
        private int            _next         = 0;
        private float          _savedSpeed   = NORMAL_SPEED;
        private bool           _inHitStop    = false;
        private bool           _feedbackUsed = false;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            if (_wideSlashPrefab == null)
            {
                Debug.LogError("[VFX] 넓게 베기 프리팹이 연결되지 않았다", this);
                enabled = false;
                return;
            }

            // 연출은 휘두른 자리에 남아야 하므로 캐릭터 밑에 두지 않는다
            _pool = new WideSlashVfx[_poolSize];
            for (int i = 0; i < _poolSize; ++i)
            {
                _pool[i]      = Instantiate(_wideSlashPrefab);
                _pool[i].name = $"{_wideSlashPrefab.name} ({name})";
            }

            _hitStop = DOTween.Sequence()
                .AppendCallback(BeginHitStop)
                .AppendInterval(_hitStopDuration)
                .AppendCallback(EndHitStop)
                .SetUpdate(true)
                .SetAutoKill(false)
                .Pause()
                .SetLink(gameObject);
        }

        private void OnDisable()
        {
            if (_inHitStop)
            {
                EndHitStop();
            }

            if (_hitStop != null)
            {
                _hitStop.Pause();
            }
        }

        private void OnDestroy()
        {
            if (_pool == null)
            {
                return;
            }

            for (int i = 0; i < _pool.Length; ++i)
            {
                if (_pool[i] != null)
                {
                    Destroy(_pool[i].gameObject);
                }
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 넓게 베기 시작 시점에 부른다, 입력 즉시 모든 클라이언트가 로컬로 재생한다 </summary>
        public void PlayWideSlash()
        {
            if ((!enabled) || (_pool == null))
            {
                return;
            }

            // 경직 중에 다음 휘두름이 오면 이전 연출이 느린 속도에 남지 않게 먼저 푼다
            if (_inHitStop)
            {
                _hitStop.Pause();
                EndHitStop();
            }

            _current      = _pool[_next];
            _next         = (_next + 1) % _pool.Length;
            _feedbackUsed = false;
            _current.Play(transform.position, Quaternion.Euler(0f, transform.eulerAngles.y, 0f), _bladeAnchor, _brightnessScale);
        }

        /// <summary> 준비 중 회피나 서버 거절로 넓게 베기가 취소되면 부른다 </summary>
        public void CancelWideSlash()
        {
            if (_current == null)
            {
                return;
            }

            _current.Cancel();
        }

        /// <summary> 연출 밝기 배율을 바꾼다, 다음 재생부터 적용한다 </summary>
        public void SetBrightnessScale(float scale)
        {
            _brightnessScale = Mathf.Clamp01(scale);
        }

        // 피해 결과 수신 View가 맞은 대상마다 부른다. 같은 결과가 두 번 오지 않게 거르는 일은 그 View가 한다
        /// <summary> 서버가 확정한 타격 지점에 타격 연출을 붙인다 </summary>
        public void PlayHit(Vector3 point, bool isLocalAttacker)
        {
            if ((!enabled) || (_current == null))
            {
                return;
            }

            _current.PlayHit(point);

            if ((!isLocalAttacker) || (_feedbackUsed))
            {
                return;
            }

            _feedbackUsed = true;
            _hitStop.Restart();

            if (_impulseSource != null)
            {
                _impulseSource.GenerateImpulseWithForce(_impulseForce);
            }
        }

        private void BeginHitStop()
        {
            // 경직 중에 다시 맞으면 이미 늦춘 속도를 원래 값으로 저장하지 않는다
            if (!_inHitStop)
            {
                _savedSpeed = (_animator != null) ? _animator.speed : NORMAL_SPEED;
                _inHitStop  = true;
            }

            if (_animator != null)
            {
                _animator.speed = _hitStopSpeed;
            }

            if (_current != null)
            {
                _current.SetTimeScale(_hitStopSpeed);
            }
        }

        private void EndHitStop()
        {
            _inHitStop = false;

            if (_animator != null)
            {
                _animator.speed = _savedSpeed;
            }

            if (_current != null)
            {
                _current.SetTimeScale(NORMAL_SPEED);
            }
        }
    }
}
