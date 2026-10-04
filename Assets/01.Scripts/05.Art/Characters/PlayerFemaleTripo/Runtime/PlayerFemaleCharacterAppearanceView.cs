// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Combat;
using Shinshi.Contracts;

namespace Shinshi.Art
{
    /// <summary> PlayerCombatState.Appearance를 읽어 실제 외형을 조립하고 PresentationView에 연결한다, 서버 빌드에서는 아무것도 하지 않는다 </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerFemaleCharacterAppearanceView : ClientView
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private PlayerCombatState                            _combat;
        [SerializeField] private PlayerFemaleLookCatalogSO                    _catalog;
        [SerializeField] private RuntimeAnimatorController                    _controller;
        [SerializeField] private Transform                                    _assembledParent;
        [SerializeField] private Transform                                    _movementRoot;
        [SerializeField] private GameObject                                   _capsule;
        [SerializeField] private PlayerFemaleCharacterPresentationView        _presentationView;
        [SerializeField] private PlayerFemaleCharacterPredictionMotionSource  _predictionMotionSource;
        [SerializeField] private PlayerFemaleCharacterServerMotionSource      _serverMotionSource;

        private GameObject    _instance;
        private Animator      _animator;
        private bool          _isAssembled;
        private CharacterLook _currentLook;
        private string        _lastError = string.Empty;

        // 조립이 끝난 시점의 소유권을 기억해 둔다. ChangeOwner처럼 같은 개체가 재사용되며 소유권만 바뀌는 경우를
        // 잡아내는 값이고, 최초 스폰은 조립이 끝난 뒤 처음 저장되므로 오탐이 없다
        private bool _cachedIsOwned;

        /****************************************
        *              Properties
        ****************************************/

        public bool          IsAssembled => _isAssembled;
        public CharacterLook CurrentLook => _currentLook;
        public string        LastError   => _lastError;
        public Animator       Animator    => _animator;

        /****************************************
        *              Unity Event
        ****************************************/

        protected override void OnViewAwake()
        {
            BindCombat();
        }

        private void OnEnable() { CheckOwnershipRole(); }

        // 소유권 변경(ChangeOwner)은 같은 개체의 isOwned만 바뀌고 새 Awake가 돌지 않는다.
        // SyncVar hook도 없으므로 매 프레임 가벼운 bool 비교로 역할이 바뀐 프레임만 다시 Configure한다
        private void Update() { CheckOwnershipRole(); }

        private void OnDestroy()
        {
            if (_combat != null)
            {
                _combat.AppearanceChanged -= OnAppearanceChanged;
            }

            if (_instance != null)
            {
                Destroy(_instance);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 부모 씬 바인딩용. 최초 hook 순서와 무관하게, 연결 직후 현재 값을 한 번 적용한다 </summary>
        public void Configure(PlayerCombatState combat, PlayerFemaleLookCatalogSO catalog, RuntimeAnimatorController controller,
            Transform assembledParent, Transform movementRoot, GameObject capsule,
            PlayerFemaleCharacterPresentationView presentationView,
            PlayerFemaleCharacterPredictionMotionSource predictionMotionSource,
            PlayerFemaleCharacterServerMotionSource serverMotionSource)
        {
            if (_combat != null)
            {
                _combat.AppearanceChanged -= OnAppearanceChanged;
            }

            _combat                = combat;
            _catalog                = catalog;
            _controller             = controller;
            _assembledParent        = assembledParent;
            _movementRoot           = movementRoot;
            _capsule                = capsule;
            _presentationView       = presentationView;
            _predictionMotionSource = predictionMotionSource;
            _serverMotionSource     = serverMotionSource;
            BindCombat();
        }

        private void BindCombat()
        {
            if (_combat == null)
            {
                Debug.LogError("[AppearanceView] PlayerCombatState 참조가 필요하다", this);
                return;
            }

            _combat.AppearanceChanged += OnAppearanceChanged;
            ApplyAppearance(_combat.Appearance);
        }

        private void OnAppearanceChanged(CharacterAppearance appearance) { ApplyAppearance(appearance); }

        private void ApplyAppearance(CharacterAppearance appearance)
        {
            if (IsServerBuild)
            {
                return;
            }

            // 최초 Instantiate의 Awake는 SyncVar 초기 동기화보다 먼저 돈다. 아직 수신 전인 null은
            // 오류가 아니라 대기일 뿐이다 - 구독은 이미 걸려 있으므로 다음 값이 오면 다시 불린다
            if (appearance == null)
            {
                return;
            }

            if (!CharacterLook.TryFrom(appearance, out CharacterLook look))
            {
                FailAssembly("appearance가 정규 v2가 아니다");
                return;
            }

            if ((_isAssembled) && (_currentLook.Equals(look)))
            {
                return;
            }

            ClearInstance();
            if ((_catalog == null) || (_assembledParent == null))
            {
                FailAssembly("조립 참조가 연결되지 않았다");
                return;
            }

            bool success = PlayerFemaleLookAssembler.TryAssemble(_catalog, look, _assembledParent, _controller,
                out GameObject result, out string error);
            if (!success)
            {
                FailAssembly(error);
                return;
            }

            _instance = result;
            _animator = _instance.GetComponentInChildren<Animator>(true);
            if (_animator == null)
            {
                FailAssembly("조립 결과에 Animator가 없다");
                ClearInstance();
                return;
            }

            // 조립된 시각 인스턴스는 원본 PF의 서버 물리/전투 판정을 바꾸지 않는다
            _animator.applyRootMotion = false;
            Collider[] colliders = _instance.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; ++i)
            {
                colliders[i].enabled = false;
            }

            MonoBehaviour motionSource = (_combat.isOwned) ? (MonoBehaviour)_predictionMotionSource : (MonoBehaviour)_serverMotionSource;
            if ((_presentationView == null) || (!_presentationView.Configure(_animator, _movementRoot, _combat, motionSource)))
            {
                FailAssembly((_presentationView != null) ? _presentationView.ValidationError : "PresentationView 참조가 없다");
                ClearInstance();
                return;
            }

            _currentLook   = look;
            _isAssembled   = true;
            _cachedIsOwned = _combat.isOwned;
            _lastError     = string.Empty;
            if (_capsule != null)
            {
                _capsule.SetActive(false);
            }
        }

        // ChangeOwner로 같은 개체의 소유권만 바뀌면 look은 그대로이므로 ApplyAppearance가 다시 불리지 않는다.
        // 역할이 실제로 바뀐 프레임에만 Prediction(소유자)/서버 공개값(원격) 소스를 다시 Configure한다.
        // 최초 스폰은 조립 성공 시점에 이미 올바른 isOwned를 _cachedIsOwned로 저장하므로 추측이 필요 없다
        private void CheckOwnershipRole()
        {
            if ((!_isAssembled) || (_combat == null) || (!_combat.isClient))
            {
                return;
            }

            bool isOwned = _combat.isOwned;
            if (isOwned == _cachedIsOwned)
            {
                return;
            }

            _cachedIsOwned = isOwned;
            MonoBehaviour motionSource = (isOwned) ? (MonoBehaviour)_predictionMotionSource : (MonoBehaviour)_serverMotionSource;
            if ((_presentationView == null) || (!_presentationView.Configure(_animator, _movementRoot, _combat, motionSource)))
            {
                FailAssembly((_presentationView != null) ? _presentationView.ValidationError : "PresentationView 참조가 없다");
                ClearInstance();
            }
        }

        private void ClearInstance()
        {
            if (_instance != null)
            {
                Destroy(_instance);
                _instance = null;
            }

            _animator    = null;
            _isAssembled = false;
        }

        private void FailAssembly(string error)
        {
            _lastError = error;
            if (_capsule != null)
            {
                _capsule.SetActive(true);
            }

            Debug.LogError($"[AppearanceView] 외형 조립 실패: {error}", this);
        }
    }
}
