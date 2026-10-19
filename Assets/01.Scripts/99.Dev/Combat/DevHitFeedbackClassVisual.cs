// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Art;
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Dev
{
    /// <summary> 타격감 시험 플레이어에 직업 모델을 붙이고 공격, 이동, 피격, 사망 모션을 서버 없이 재생한다 </summary>
    [RequireComponent(typeof(DevHitFeedbackPlayer))]
    public sealed class DevHitFeedbackClassVisual : MonoBehaviour
    {
        // 서버 전투 상태가 없는 오프라인 시험용이다. 실제 접속과 같은 외형 카탈로그, 애니메이터 컨트롤러, 직업별 공격 모션 배정을 쓴다.
        // 피해는 DevHitFeedbackPlayer가 판정하고 이 컴포넌트는 모션만 맡는다. 판정 모양은 직업별 값으로 플레이어에 덮어쓴다

        /****************************************
        *                Fields
        ****************************************/

        private static readonly int _speedHash       = Animator.StringToHash("Speed");
        private static readonly int _aliveHash       = Animator.StringToHash("Alive");
        private static readonly int _attackHash      = Animator.StringToHash("Attack");
        private static readonly int _hitHash         = Animator.StringToHash("Hit");
        private static readonly int _actionIndexHash = Animator.StringToHash("ActionIndex");
        private static readonly int _actionSpeedHash = Animator.StringToHash("ActionSpeed");

        private const string CAPSULE_MESH_NAME = "Capsule";
        private const float  SPEED_DAMP_TIME   = 0.08f; // 초
        private const float  FALLBACK_INTERVAL = 0.9f;  // 초, 프로필에 간격이 없을 때

        [Header("직업")]
        [SerializeField] private EPlayerClass _playerClass = EPlayerClass.Mageknight;

        [Header("참조")]
        [SerializeField] private PlayerCharacterLookCatalogSO    _lookCatalog;
        [SerializeField] private PlayerCombatAnimationProfileSO  _animationProfile;

        [Header("공격 판정 (직업별)")]
        [SerializeField] private float _attackRange     = 2.5f; // m
        [SerializeField] private float _attackHalfAngle = 60f;  // 도
        [SerializeField] private float _attackInterval  = 0.85f; // 초, 모션 한 번이 끝나는 시간이자 다음 공격까지의 간격
        [SerializeField] private float _heavyInterval   = 1.1f;  // 초, 3타
        [SerializeField] private bool  _showSlash       = true;

        private DevHitFeedbackPlayer _player = null;
        private Animator             _animator = null;
        private bool                 _hasSpeedParameter = false;
        private float                _smoothedSpeed = 0f;
        private float                _speedVelocity = 0f;

        public EPlayerClass PlayerClass => _playerClass;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            _player = GetComponent<DevHitFeedbackPlayer>();
            _player.ConfigureAttack(_attackRange, _attackHalfAngle, _attackInterval, _attackInterval, _heavyInterval, _showSlash);
        }

        private void Start()
        {
            if ((_lookCatalog == null) || (_animationProfile == null))
            {
                Debug.LogError("[DevClassVisual] 외형 카탈로그와 모션 프로필이 필요하다", this);
                enabled = false;
                return;
            }

            CharacterAppearance appearance = CharacterAppearanceCatalog.CreateDefault((int)_playerClass);
            if ((!CharacterP09Look.TryFrom(appearance, out CharacterP09Look look))
                || (!PlayerCharacterLookAssembler.TryAssemble(_lookCatalog, look, transform, out GameObject model, out string error)))
            {
                Debug.LogError($"[DevClassVisual] 모델 조립 실패: {_playerClass}", this);
                enabled = false;
                return;
            }

            HideCapsule(model.transform);

            _animator = model.GetComponent<Animator>();
            for (int i = 0; i < _animator.parameterCount; ++i)
            {
                if (_animator.GetParameter(i).nameHash == _speedHash)
                {
                    _hasSpeedParameter = true;
                    break;
                }
            }

            // 서버 전투 상태가 있으면 실제 접속과 같은 경로로 모션을 재생한다. 없으면 이 컴포넌트가 직접 트리거를 건다
            if (TryGetComponent(out PlayerCombatState combat))
            {
                PlayerCharacterCombatAnimationView view = gameObject.AddComponent<PlayerCharacterCombatAnimationView>();
                view.Configure(_animator, combat, _animationProfile);
                return;
            }

            _player.Attacked += OnAttacked;
            _player.Damaged  += OnDamaged;
            _player.Died     += OnDied;
        }

        private void Update()
        {
            if ((_animator == null) || (!_hasSpeedParameter))
            {
                return;
            }

            Vector3 velocity = _player.Velocity;
            velocity.y = 0f;
            _smoothedSpeed = Mathf.SmoothDamp(_smoothedSpeed, velocity.magnitude, ref _speedVelocity, SPEED_DAMP_TIME);
            _animator.SetFloat(_speedHash, _smoothedSpeed);
        }

        private void OnDestroy()
        {
            if (_player == null)
            {
                return;
            }

            _player.Attacked -= OnAttacked;
            _player.Damaged  -= OnDamaged;
            _player.Died     -= OnDied;
        }

        /****************************************
        *                Methods
        ****************************************/

        // 기존 캡슐 표시만 끈다. 정면 화살표와 휘두르는 띠는 남긴다
        private void HideCapsule(Transform model)
        {
            foreach (MeshRenderer meshRenderer in GetComponentsInChildren<MeshRenderer>(true))
            {
                if (meshRenderer.transform.IsChildOf(model))
                {
                    continue;
                }

                MeshFilter filter = meshRenderer.GetComponent<MeshFilter>();
                if ((filter != null) && (filter.sharedMesh != null) && (filter.sharedMesh.name == CAPSULE_MESH_NAME))
                {
                    meshRenderer.enabled = false;
                }
            }

            MeshRenderer own = GetComponent<MeshRenderer>();
            if (own != null)
            {
                own.enabled = false;
            }
        }

        private void OnAttacked(int step, bool isHeavy)
        {
            if (!_animationProfile.TryGetAttackIndex(_playerClass, step, out int actionIndex))
            {
                return;
            }

            float clipLength = _animationProfile.GetAttackClipLength(actionIndex);
            float target     = isHeavy ? _heavyInterval : _attackInterval;
            if (target <= 0f)
            {
                target = FALLBACK_INTERVAL;
            }

            float speed = CombatAnimationTriggerRules.ComputeSpeed(clipLength, target, _animationProfile.MinSpeed, _animationProfile.MaxSpeed);
            Fire(_attackHash, actionIndex, speed);
        }

        private void OnDamaged(int hitsLeft)
        {
            Fire(_hitHash, hitsLeft % PlayerCombatAnimationProfileSO.HIT_COUNT, 1f);
        }

        private void OnDied()
        {
            _animator.SetBool(_aliveHash, false);
        }

        private void Fire(int trigger, int index, float speed)
        {
            _animator.SetInteger(_actionIndexHash, index);
            _animator.SetFloat(_actionSpeedHash, speed);
            _animator.SetTrigger(trigger);
        }
    }
}
