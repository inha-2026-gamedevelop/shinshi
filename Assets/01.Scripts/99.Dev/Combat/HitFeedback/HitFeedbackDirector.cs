// Unity
using UnityEngine;

namespace Shinshi.Dev
{
    /// <summary> 한 번의 타격이 일으킬 모든 타격감(렉프레임, 카메라, 불똥, 충격파)을 프로필대로 재생하는 중앙 재생기 </summary>
    public sealed class HitFeedbackDirector : MonoBehaviour
    {
        // 서버가 확정한 타격 결과를 받은 쪽이 PlayHit을 부른다. 시험 씬에서는 스포너가 서버 대신 부른다.
        // 어떤 연출을 얼마나 낼지는 전부 프로필이 정하고, 이 클래스는 순서와 대상만 맞춘다.
        // 카메라 흔들림과 줌은 이 클라이언트 화면에서만 쓰므로 자기 캐릭터의 타격일 때만 부른다는 전제다

        /****************************************
        *                Fields
        ****************************************/

        [Header("프로필")]
        [SerializeField] private HitFeedbackProfile _lightProfile;
        [SerializeField] private HitFeedbackProfile _heavyProfile;

        [Header("켜기")]
        [SerializeField] private bool _useHitLag      = true;
        [SerializeField] private bool _useCameraShake = true;
        [SerializeField] private bool _useSparks      = true;
        [SerializeField] private bool _useShockwave   = true;
        [SerializeField] private bool _useScreenFx    = true;

        private HitFeedbackScreenFx     _screenFx  = null;
        private HitFeedbackCamera       _camera    = null;
        private DevHitFeedbackSparks    _sparks    = null;
        private DevHitFeedbackShockwave _shockwave = null;

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

            _shockwave = new GameObject("DevShockwave").AddComponent<DevHitFeedbackShockwave>();
            _shockwave.transform.SetParent(transform, false);

            _screenFx = new GameObject("DevScreenFx").AddComponent<HitFeedbackScreenFx>();
            _screenFx.transform.SetParent(transform, false);
        }

        /****************************************
        *                Methods
        ****************************************/

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

        /// <summary> 맞은 지점에서 프로필대로 타격감을 재생하고 렉프레임 수를 돌려준다. direction은 공격이 향한 수평 방향이고, 치명타 여부는 서버가 정해 준 값이다 </summary>
        public int PlayHit(HitFeedbackProfile profile, Vector3 point, Vector3 direction, bool isKill, bool isCritical,
            IHitLagTarget attacker, IHitLagTarget target)
        {
            int lagFrames = 0;
            if (!enabled)
            {
                return lagFrames;
            }

            if (_useHitLag)
            {
                lagFrames = profile.HitLagFrames + (isKill ? profile.KillExtraLagFrames : 0)
                    + (isCritical ? profile.CritExtraLagFrames : 0);
                attacker?.Freeze(lagFrames);
                target?.Freeze(lagFrames + profile.TargetExtraLagFrames);
            }

            if ((_useCameraShake) && (_camera != null))
            {
                float strength = isKill ? profile.KillShakeAmount : profile.ShakeAmount;
                _camera.Kick(direction, strength, profile.ShakeSeconds, lagFrames);
            }

            if (_useSparks)
            {
                int sparkCount = isKill ? profile.KillSparkCount : profile.SparkCount;
                sparkCount     = isCritical ? Mathf.RoundToInt(sparkCount * profile.CritSparkMultiplier) : sparkCount;
                _sparks.Burst(point, direction, sparkCount, isCritical);
            }

            if ((_useScreenFx) && (isCritical))
            {
                _screenFx.Flash(profile.CritFlashAlpha, profile.CritFlashSeconds);
            }

            if (isCritical)
            {
                PlayCritical(profile, point);
            }

            return lagFrames;
        }

        // 치명타만 더하는 연출: 맞은 자리의 충격 링
        private void PlayCritical(HitFeedbackProfile profile, Vector3 point)
        {
            if ((_useShockwave) && (profile.CritRingRadius > 0f))
            {
                _shockwave.Play(point, profile.CritRingRadius, false);
            }
        }

        /// <summary> 프로필에 충격파가 있으면 center에서 링과 먼지를 터뜨리고 화면을 당긴다. 몬스터를 미는 일은 호출한 쪽이 한다 </summary>
        public void PlaySlam(HitFeedbackProfile profile, Vector3 center)
        {
            if ((!enabled) || (!_useShockwave) || (profile.ShockwaveRadius <= 0f))
            {
                return;
            }

            _shockwave.Play(center, profile.ShockwaveRadius, true);

            if (_camera != null)
            {
                _camera.ZoomPunch(profile.ZoomPunch, profile.ZoomSeconds);
            }
        }
    }
}
