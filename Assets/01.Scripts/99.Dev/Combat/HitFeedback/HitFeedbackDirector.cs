// Unity
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Shinshi.Dev
{
    /// <summary> 한 번의 타격이 일으킬 모든 타격감(렉프레임, 카메라, 불똥, 충격파)을 프로필대로 재생하는 중앙 재생기 </summary>
    public sealed class HitFeedbackDirector : MonoBehaviour
    {
        // 서버가 확정한 타격 결과를 받은 쪽이 PlayHit을 부른다. 시험 씬에서는 스포너가 서버 대신 부른다.
        // 어떤 연출을 얼마나 낼지는 전부 프로필이 정하고, 이 클래스는 순서와 대상만 맞춘다.
        // 카메라 흔들림, 줌, 화면 효과와 공격자 멈춤은 이 클라이언트의 내 공격(isOwnAttack)에만 쓰고, 남의 공격은 몬스터 반응과 입자만 줄여 낸다

        /****************************************
        *                Fields
        ****************************************/

        private const string BLOOD_RESOURCE = "DevHitBlood/PF_DevHitBlood";
        private const string LANDING_RESOURCE = "DevHitReactions/PF_DevCorpseLandingFx";

        // 같은 프레임 다중 타격과 공격자 렉프레임 예산의 시작값. 조절 패널이 실행 중에 바꾼다
        private const float MULTI_HIT_SHAKE_BONUS = 0.25f; // 한 마리 더 맞힐 때마다 흔들림에 더하는 비율
        private const float MULTI_HIT_SHAKE_MAX   = 2f;    // 흔들림 배율 상한
        private const int   LAG_BUDGET_FRAMES     = 12;    // 연타로 모아 쓸 수 있는 공격자 렉프레임
        private const float LAG_REFILL_PER_SECOND = 24f;   // 프레임/초
        private const int   MIN_ATTACKER_LAG      = 1;     // 예산이 바닥나도 맞힌 순간은 1프레임 멈춘다
        private const float MIN_DIRECTION_SQR     = 0.0001f;
        private const float REMOTE_EFFECT_SCALE   = 0.4f;  // 남의 공격일 때 몬스터 떨림과 불똥 개수 배율
        private const int   MIN_REMOTE_SPARKS     = 1;     // 줄여도 맞았다는 표시는 남긴다

        // 임팩트 프레임. 이 씬은 몬스터가 대부분 한 대에 죽으므로 처치마다 내면 화면이 계속 깜빡인다.
        // 치명타와 한 프레임 다중 처치에만 내고 재사용 간격을 둔다
        // 흑백 반전은 화면 밝기가 한 번에 뒤집혀 눈이 아프다는 피드백으로 기본을 부드러운 방식(회색으로 어둡게, 서서히 풀림)으로 둔다
        private const int   IMPACT_FRAMES         = 6;
        private const float IMPACT_DIM            = 0.6f;  // 부드러운 방식에서 배경 회색의 밝기 배율
        private const float IMPACT_THRESHOLD      = 0.12f; // 강한 방식에서 배경을 흑백으로 가르는 밝기. 숲 고원은 어두워 낮게 둔다
        private const float IMPACT_HOT_THRESHOLD  = 0.5f;  // 이보다 밝은 곳(번쩍인 몸, 불똥)은 색을 남기거나 강조색
        private const int   IMPACT_MULTI_KILLS    = 3;     // 한 프레임에 이만큼 처치하면 낸다
        private const float IMPACT_COOLDOWN       = 0.4f;  // 초

        [Header("프로필")]
        [SerializeField] private HitFeedbackProfile _lightProfile;
        [SerializeField] private HitFeedbackProfile _heavyProfile;

        [Header("혈흔 (비우면 개발용 Resources 프리팹)")]
        [SerializeField] private DevHitFeedbackBlood _bloodPrefab;

        [Header("켜기")]
        [SerializeField] private bool _useHitLag      = true;
        [SerializeField] private bool _useCameraShake = true;
        [SerializeField] private bool _useSparks      = true;
        [SerializeField] private bool _useBlood       = true;
        [SerializeField] private bool _useShockwave   = true;
        [SerializeField] private bool _useScreenFx    = true;
        [SerializeField] private bool _useMotionBlur  = true;

        [Header("치명타 블러 (렌더러의 풀스크린 패스와 그 재질)")]
        [SerializeField] private FullScreenPassRendererFeature _blurFeature;
        [SerializeField] private Material _blurMaterial;

        private HitFeedbackScreenFx     _screenFx  = null;
        private HitFeedbackMotionBlur   _motionBlur = null;
        private HitFeedbackCamera       _camera    = null;
        private DevHitFeedbackSparks    _sparks    = null;
        private DevHitFeedbackShockwave _shockwave = null;
        private DevHitFeedbackBlood     _blood     = null;
        private DevCorpseLandingFx _landing = null;

        private Vector3 _shakeDirectionSum = Vector3.zero;
        private float   _shakeBase         = 0f;  // 이번 프레임에 맞힌 타격 중 가장 센 흔들림
        private float   _lagBudget         = LAG_BUDGET_FRAMES;
        private float   _lagRefillTime     = 0f;  // Time.time 기준, 예산을 마지막으로 채운 시각
        private int     _shakeFrame        = -1;
        private int     _shakeHits         = 0;
        private int     _lagFrame          = -1;
        private int     _lagGranted        = 0;   // 이번 프레임에 공격자에게 준 렉프레임
        private int     _killFrame         = -1;
        private int     _killsThisFrame    = 0;
        private float   _nextImpactTime    = 0f;  // Time.time 기준

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            if ((_lightProfile == null) || (_heavyProfile == null))
            {
                Debug.LogError("[HitFeedback] 타격 프로필이 연결되지 않았다", this);
                enabled = false;
                return;
            }

            _sparks = new GameObject("DevSparks").AddComponent<DevHitFeedbackSparks>();
            _sparks.transform.SetParent(transform, false);

            DevHitFeedbackBlood bloodPrefab = _bloodPrefab;
            if (bloodPrefab == null)
            {
                GameObject bloodAsset = Resources.Load<GameObject>(BLOOD_RESOURCE);
                if (bloodAsset != null)
                {
                    bloodAsset.TryGetComponent(out bloodPrefab);
                }
            }
            if (bloodPrefab != null)
            {
                _blood = Instantiate(bloodPrefab, transform, false);
                _blood.name = "DevBlood";
            }
            else
            {
                Debug.LogWarning("[HitFeedback] 혈흔 프리팹이 없어 코드 기본값을 사용한다", this);
                _blood = new GameObject("DevBlood").AddComponent<DevHitFeedbackBlood>();
                _blood.transform.SetParent(transform, false);
            }

            GameObject landingPrefab = Resources.Load<GameObject>(LANDING_RESOURCE);
            if (landingPrefab != null)
            {
                _landing = Instantiate(landingPrefab, transform, false).GetComponent<DevCorpseLandingFx>();
                _landing.Init(Blood);
            }

            _shockwave = new GameObject("DevShockwave").AddComponent<DevHitFeedbackShockwave>();
            _shockwave.transform.SetParent(transform, false);

            _screenFx = new GameObject("DevScreenFx").AddComponent<HitFeedbackScreenFx>();
            _screenFx.transform.SetParent(transform, false);

            _motionBlur = new GameObject("DevMotionBlur").AddComponent<HitFeedbackMotionBlur>();
            _motionBlur.transform.SetParent(transform, false);
            _motionBlur.Init(_blurFeature, _blurMaterial);

            // 치명타 블러와 줌 펀치는 프로필 값으로 시작해 조절 패널이 실행 중에 바꾼다. 프로필 에셋은 건드리지 않는다
            CritBlurIntensity = _lightProfile.CritMotionBlurIntensity;
            CritBlurSeconds   = _lightProfile.CritMotionBlurSeconds;
            CritZoomPunch     = _lightProfile.CritZoomPunch;
            CritZoomSeconds   = _lightProfile.CritZoomSeconds;
        }

        /****************************************
        *               Properties
        ****************************************/

        /// <summary> 치명타 블러 세기(0에서 1), 0이면 없다 </summary>
        public float CritBlurIntensity { get; set; }

        public float CritBlurSeconds { get; set; }

        /// <summary> 치명타 줌 펀치로 화면이 당겨지는 비율, 0이면 없다 </summary>
        public float CritZoomPunch { get; set; }

        public float CritZoomSeconds { get; set; }

        /// <summary> 맞은 몬스터의 렉프레임 떨림 크기에 곱하는 배율. 1이면 프로필 그대로, 0이면 떨지 않는다 </summary>
        public float VibrateScale { get; set; } = 1f;

        /// <summary> 같은 프레임에 한 마리 더 맞힐 때마다 카메라 흔들림에 더하는 비율. 0이면 합산하지 않는다 </summary>
        public float MultiHitShakeBonus { get; set; } = MULTI_HIT_SHAKE_BONUS;

        /// <summary> 다중 타격으로 커지는 카메라 흔들림 배율의 상한 </summary>
        public float MultiHitShakeMax { get; set; } = MULTI_HIT_SHAKE_MAX;

        /// <summary> 공격자가 연타로 모아 쓸 수 있는 렉프레임 수. 다 쓰면 맞힌 순간만 1프레임 멈춘다 </summary>
        public int HitLagBudgetFrames { get; set; } = LAG_BUDGET_FRAMES;

        /// <summary> 공격자 렉프레임 예산이 초마다 다시 차는 프레임 수 </summary>
        public float HitLagRefillPerSecond { get; set; } = LAG_REFILL_PER_SECOND;

        /// <summary> 남의 공격일 때 맞은 몬스터의 떨림과 불똥 개수에 곱하는 배율 </summary>
        public float RemoteEffectScale { get; set; } = REMOTE_EFFECT_SCALE;

        /// <summary> 내 치명타와 한 프레임 다중 처치에 임팩트 프레임을 낼지 </summary>
        public bool UseImpactFrame { get; set; } = true;

        /// <summary> 임팩트 프레임으로 그리는 프레임 수. 부드러운 방식은 이 동안 서서히 풀린다 </summary>
        public int ImpactFrames { get; set; } = IMPACT_FRAMES;

        /// <summary> 참이면 부드러운 방식(배경을 어두운 회색으로, 밝은 곳만 색을 남김), 거짓이면 흑백과 강조색의 강한 방식 </summary>
        public bool ImpactSoft { get; set; } = true;

        /// <summary> 부드러운 방식에서 배경 회색의 밝기 배율(0에서 1) </summary>
        public float ImpactDim { get; set; } = IMPACT_DIM;

        /// <summary> 강한 방식에서 배경을 흑백으로 나누는 밝기 기준(0에서 1) </summary>
        public float ImpactThreshold { get; set; } = IMPACT_THRESHOLD;

        /// <summary> 이보다 밝은 곳은 부드러운 방식에서 원래 색을 남기고 강한 방식에서 강조색으로 칠한다 </summary>
        public float ImpactHotThreshold { get; set; } = IMPACT_HOT_THRESHOLD;

        /// <summary> 강한 방식에서 참이면 밝은 곳이 검게, 어두운 곳이 희게 된다 </summary>
        public bool ImpactInvert { get; set; } = true;

        /// <summary> 한 프레임에 이만큼 처치하면 치명타가 아니어도 임팩트 프레임을 낸다 </summary>
        public int ImpactMultiKills { get; set; } = IMPACT_MULTI_KILLS;

        /// <summary> 임팩트 프레임 사이의 최소 간격(초) </summary>
        public float ImpactCooldown { get; set; } = IMPACT_COOLDOWN;

        public DevHitFeedbackBlood Blood => _useBlood ? _blood : null;
        public DevCorpseLandingFx Landing => _landing;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 맞히지 않고 치명타의 화면 연출(임팩트 프레임, 블러, 줌 펀치)만 한 번 재생한다. 값을 조절하며 눈으로 볼 때 쓴다 </summary>
        public void PreviewCritical()
        {
            if (UseImpactFrame)
            {
                _motionBlur.ImpactFrame(ImpactFrames, ImpactSoft, ImpactDim, ImpactThreshold, ImpactHotThreshold, ImpactInvert);
            }
            PlayCriticalScreen();
        }

        private void PlayCriticalScreen()
        {
            if (_useMotionBlur)
            {
                _motionBlur.Pulse(CritBlurIntensity, CritBlurSeconds);
            }

            if (_camera != null)
            {
                _camera.ZoomPunch(CritZoomPunch, CritZoomSeconds);
            }
        }

        /// <summary> 카메라 흔들림과 줌을 맡길 카메라 도우미를 연결한다 </summary>
        public void Init(HitFeedbackCamera feedbackCamera)
        {
            _camera = feedbackCamera;
        }

        /// <summary> 강공격이면 강공격 프로필, 아니면 약공격 프로필을 돌려준다 </summary>
        public HitFeedbackProfile GetProfile(bool isHeavy)
        {
            return isHeavy ? _heavyProfile : _lightProfile;
        }

        /// <summary> 맞은 지점에서 프로필대로 타격감을 재생하고 공격자에게 준 렉프레임 수를 돌려준다. direction은 공격이 향한 수평 방향이고, 치명타 여부는 서버가 정해 준 값이다. isOwnAttack이 거짓이면 남의 공격 등급으로 줄여 재생한다 </summary>
        public int PlayHit(HitFeedbackProfile profile, Vector3 point, Vector3 direction, bool isKill, bool isCritical,
            IHitLagTarget attacker, IHitLagTarget target, bool isOwnAttack = true)
        {
            int attackerLag = 0;
            if (!enabled)
            {
                return attackerLag;
            }

            // 남의 공격은 내 화면을 흔들거나 멈추지 않는다. 4명이 함께 치면 화면 연출이 쌓여 내 조작과 예고가 묻히므로
            // 맞은 몬스터의 반응과 월드 입자만 줄여 남긴다. 처치의 혈흔은 전장 상황이라 그대로 둔다
            float effectScale = isOwnAttack ? 1f : RemoteEffectScale;

            if (_useHitLag)
            {
                int lagFrames = profile.HitLagFrames + (isKill ? profile.KillExtraLagFrames : 0)
                    + (isCritical ? profile.CritExtraLagFrames : 0);
                if (isOwnAttack)
                {
                    attackerLag = (attacker != null) ? GrantAttackerLag(lagFrames) : lagFrames;
                    attacker?.Freeze(attackerLag);
                    target?.Freeze(lagFrames + profile.TargetExtraLagFrames);
                }
                else
                {
                    target?.Freeze(profile.HitLagFrames);
                }

                // 쿼터뷰는 카메라가 멀어 고정 미터 떨림이 1px 안팎으로 묻힌다. 보이는 화면 높이 비율로 키운다.
                // 플레이어는 떨지 않으므로 인터페이스를 넓히지 않고 맞은 몸에만 넘긴다
                if ((target is HitReactor reactor) && (_camera != null))
                {
                    float screenAmount = _camera.VisibleHeightAt(point) * profile.VibrateScreenRatio;
                    reactor.SetVibrateAmount(Mathf.Max(profile.VibrateAmount, screenAmount) * VibrateScale * effectScale);
                }
            }

            if ((isOwnAttack) && (_useCameraShake) && (_camera != null))
            {
                float strength = isKill ? profile.KillShakeAmount : profile.ShakeAmount;
                KickAccumulated(direction, strength, profile.ShakeSeconds, attackerLag);
            }

            if (_useSparks)
            {
                int sparkCount = isKill ? profile.KillSparkCount : profile.SparkCount;
                sparkCount     = isCritical ? Mathf.RoundToInt(sparkCount * profile.CritSparkMultiplier) : sparkCount;
                if ((!isOwnAttack) && (sparkCount > 0))
                {
                    sparkCount = Mathf.Max(MIN_REMOTE_SPARKS, Mathf.RoundToInt(sparkCount * effectScale));
                }
                _sparks.Burst(point, direction, sparkCount, isCritical);
            }

            if ((_useBlood) && (_blood != null) && ((isOwnAttack) || (isKill)))
            {
                _blood.Burst(point, direction, isKill, isCritical);
            }

            if (!isOwnAttack)
            {
                return attackerLag;
            }

            TryImpactFrame(isKill, isCritical);

            if ((_useScreenFx) && (isCritical))
            {
                _screenFx.Flash(profile.CritFlashAlpha, profile.CritFlashSeconds);
            }

            if (isCritical)
            {
                PlayCriticalScreen();
                PlayCritical(profile, point);
            }

            return attackerLag;
        }

        // 연타마다 공격자가 멈추면 조작이 끊긴다. 렉프레임을 예산에서 꺼내 쓰고 시간이 지나면 다시 채운다.
        // 한 번 휘둘러 여럿을 맞혀도 공격자는 가장 긴 멈춤 하나만 쓰므로, 같은 프레임에서는 더 긴 만큼만 예산을 쓴다.
        // 맞은 몬스터의 멈춤은 몬스터마다 따로라 예산과 무관하게 프로필대로 준다
        private int GrantAttackerLag(int requested)
        {
            float now = Time.time;
            _lagBudget     = Mathf.Min(HitLagBudgetFrames, _lagBudget + ((now - _lagRefillTime) * HitLagRefillPerSecond));
            _lagRefillTime = now;

            if (Time.frameCount != _lagFrame)
            {
                _lagFrame   = Time.frameCount;
                _lagGranted = 0;
            }

            int extra = Mathf.Min(Mathf.Max(0, requested - _lagGranted), Mathf.FloorToInt(_lagBudget));
            _lagBudget  -= extra;
            _lagGranted += extra;

            return (requested > 0) ? Mathf.Max(MIN_ATTACKER_LAG, _lagGranted) : _lagGranted;
        }

        // 같은 프레임에 맞힌 수만큼 흔들림을 키운다. 방향은 맞은 방향들의 평균이고, 서로 상쇄되면 이번 타격 방향을 쓴다.
        // Kick은 진행 중인 흔들림보다 약하면 무시하므로, 커진 값으로 다시 부르면 이번 프레임의 흔들림이 갱신된다
        private void KickAccumulated(Vector3 direction, float strength, float seconds, int delayFrames)
        {
            if (Time.frameCount != _shakeFrame)
            {
                _shakeFrame        = Time.frameCount;
                _shakeHits         = 0;
                _shakeBase         = 0f;
                _shakeDirectionSum = Vector3.zero;
            }

            ++_shakeHits;
            _shakeBase          = Mathf.Max(_shakeBase, strength);
            _shakeDirectionSum += direction;

            float   scale = Mathf.Min(MultiHitShakeMax, 1f + (MultiHitShakeBonus * (_shakeHits - 1)));
            Vector3 aim   = (_shakeDirectionSum.sqrMagnitude > MIN_DIRECTION_SQR) ? _shakeDirectionSum.normalized : direction;
            _camera.Kick(aim, _shakeBase * scale, seconds, delayFrames);
        }

        // 다중 처치는 그 프레임의 처치 수가 기준에 막 닿은 타격에서 한 번만 낸다
        private void TryImpactFrame(bool isKill, bool isCritical)
        {
            if (Time.frameCount != _killFrame)
            {
                _killFrame      = Time.frameCount;
                _killsThisFrame = 0;
            }

            if (isKill)
            {
                ++_killsThisFrame;
            }

            bool isMultiKill = (isKill) && (_killsThisFrame == ImpactMultiKills);
            if ((!UseImpactFrame) || ((!isCritical) && (!isMultiKill)) || (Time.time < _nextImpactTime))
            {
                return;
            }

            _nextImpactTime = Time.time + ImpactCooldown;
            _motionBlur.ImpactFrame(ImpactFrames, ImpactSoft, ImpactDim, ImpactThreshold, ImpactHotThreshold, ImpactInvert);
        }

        // 치명타만 더하는 연출: 맞은 자리의 충격 링
        private void PlayCritical(HitFeedbackProfile profile, Vector3 point)
        {
            if ((_useShockwave) && (profile.CritRingRadius > 0f))
            {
                _shockwave.Play(point, profile.CritRingRadius, false);
            }
        }

        /// <summary> 프로필에 충격파가 있으면 center에서 링과 먼지를 터뜨리고 내 공격이면 화면을 당긴다. 몬스터를 미는 일은 호출한 쪽이 한다 </summary>
        public void PlaySlam(HitFeedbackProfile profile, Vector3 center, bool isOwnAttack = true)
        {
            if ((!enabled) || (!_useShockwave) || (profile.ShockwaveRadius <= 0f))
            {
                return;
            }

            _shockwave.Play(center, profile.ShockwaveRadius, true);

            if ((isOwnAttack) && (_camera != null))
            {
                _camera.ZoomPunch(profile.ZoomPunch, profile.ZoomSeconds);
            }
        }
    }
}
