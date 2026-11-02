// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Combat;
using Shinshi.Contracts;

namespace Shinshi.Art
{
    /// <summary> PlayerCombatState.Appearance를 읽어 P09 외형을 조립하고 PresentationView와 전투 애니메이션에 연결한다, 서버 빌드에서는 아무것도 하지 않는다 </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerFemaleCharacterAppearanceView : ClientView
    {
        // 클래스 이름과 GUID는 PF_NetPlayer 참조를 지키려고 그대로 둔다. 조립 대상은 Tripo 여성 모델에서 P09 남녀 모듈 캐릭터(외형 v3)로 바뀌었다

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private PlayerCombatState                            _combat;
        [SerializeField] private PlayerCharacterLookCatalogSO                 _catalog;
        [SerializeField] private PlayerCombatAnimationProfileSO               _combatProfile;
        [SerializeField] private Transform                                    _assembledParent;
        [SerializeField] private Transform                                    _movementRoot;
        [SerializeField] private GameObject                                   _capsule;
        [SerializeField] private PlayerFemaleCharacterPresentationView        _presentationView;
        [SerializeField] private PlayerCharacterCombatAnimationView           _combatAnimation;
        [SerializeField] private PlayerFemaleCharacterPredictionMotionSource  _predictionMotionSource;
        [SerializeField] private PlayerFemaleCharacterServerMotionSource      _serverMotionSource;
        [SerializeField] private PlayerClassWeaponView                        _weaponView;

        private GameObject       _instance;
        private Animator         _animator;
        private bool             _isAssembled;
        private CharacterP09Look _currentLook;
        private string           _lastError = string.Empty;

        // 조립이 끝난 시점의 소유권을 기억해 둔다. ChangeOwner처럼 같은 개체가 재사용되며 소유권만 바뀌는 경우를
        // 잡아내는 값이고, 최초 스폰은 조립이 끝난 뒤 처음 저장되므로 오탐이 없다
        private bool _cachedIsOwned;

        /****************************************
        *              Properties
        ****************************************/

        public bool             IsAssembled => _isAssembled;
        public CharacterP09Look CurrentLook => _currentLook;
        public string           LastError   => _lastError;
        public Animator         Animator    => _animator;

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

            if (!CharacterP09Look.TryFrom(appearance, out CharacterP09Look look))
            {
                FailAssembly("appearance가 정규 v3가 아니다");
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

            if (!PlayerCharacterLookAssembler.TryAssemble(_catalog, look, _assembledParent, out GameObject result, out string error))
            {
                FailAssembly(error);
                return;
            }

            _instance = result;
            _animator = _instance.GetComponent<Animator>();

            // 조립된 시각 인스턴스는 원본 PF의 서버 물리/전투 판정을 바꾸지 않는다
            Collider[] colliders = _instance.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; ++i)
            {
                colliders[i].enabled = false;
            }

            if (!ConfigurePresentation(_combat.isOwned))
            {
                ClearInstance();
                return;
            }

            if (_weaponView != null)
            {
                _weaponView.Attach(_instance.transform);
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

        private bool ConfigurePresentation(bool isOwned)
        {
            MonoBehaviour motionSource = isOwned ? (MonoBehaviour)_predictionMotionSource : (MonoBehaviour)_serverMotionSource;
            if ((_presentationView == null) || (!_presentationView.Configure(_animator, _movementRoot, _combat, motionSource)))
            {
                FailAssembly((_presentationView != null) ? _presentationView.ValidationError : "PresentationView 참조가 없다");
                return false;
            }

            if (_combatAnimation != null)
            {
                _combatAnimation.Configure(_animator, _combat, _combatProfile);
            }

            // OnAnimatorIK는 Animator와 같은 개체에서만 불리므로 조립된 인스턴스 루트에 붙인다
            if (!_animator.TryGetComponent(out PlayerCharacterFootIkView footIk))
            {
                footIk = _animator.gameObject.AddComponent<PlayerCharacterFootIkView>();
            }

            footIk.Configure(_movementRoot, _combat);
            return true;
        }

        // ChangeOwner로 같은 개체의 소유권만 바뀌면 외형은 그대로이므로 ApplyAppearance가 다시 불리지 않는다.
        // 역할이 실제로 바뀐 프레임에만 Prediction(소유자)/서버 공개값(원격) 소스를 다시 Configure한다
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
            if (!ConfigurePresentation(isOwned))
            {
                ClearInstance();
            }
        }

        private void ClearInstance()
        {
            if (_combatAnimation != null)
            {
                _combatAnimation.Clear();
            }

            if (_weaponView != null)
            {
                _weaponView.Detach();
            }

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
