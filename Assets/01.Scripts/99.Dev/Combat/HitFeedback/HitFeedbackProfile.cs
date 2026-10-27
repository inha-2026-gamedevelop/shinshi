// Unity
using UnityEngine;

namespace Shinshi.Dev
{
    /// <summary> 타격 한 종류의 타격감 수치를 모아 둔 데이터. 약공격, 강공격처럼 종류마다 에셋을 하나씩 만든다 </summary>
    [CreateAssetMenu(fileName = "HitFeedbackProfile", menuName = "Shinshi/Dev/Hit Feedback Profile")]
    public sealed class HitFeedbackProfile : ScriptableObject
    {
        // 공격과 피격 쪽 코드는 수치를 직접 들고 있지 않고 이 프로필만 읽는다. 새 무기나 몬스터가 생기면 에셋만 만들면 된다.
        // 넉백과 충격파의 밀침은 실제로는 서버가 정하는 위치 변화다. 시험 씬에서는 서버 대신 이 값을 읽는다

        /****************************************
        *                Fields
        ****************************************/

        [Header("렉프레임 (프레임 수)")]
        [SerializeField, Min(0)] private int _hitLagFrames       = 3;
        [SerializeField, Min(0)] private int _killExtraLagFrames = 3; // 처치한 타격에 더한다
        [SerializeField, Min(0)] private int _targetExtraLagFrames = 0; // 맞은 몬스터만 더 오래 멈춘다. 공격자는 더하지 않는다

        [Header("카메라 흔들림 (m)")]
        [SerializeField, Min(0f)]    private float _shakeAmount     = 0.06f;
        [SerializeField, Min(0f)]    private float _killShakeAmount = 0.12f;
        [SerializeField, Min(0.01f)] private float _shakeSeconds    = 0.2f; // 렉프레임이 풀린 뒤부터 센다

        [Header("맞은 몸")]
        [SerializeField, Min(0.01f)]      private float _flashSeconds       = 0.12f;
        [SerializeField, Range(0f, 0.6f)] private float _punchAmount        = 0.25f;  // 높이가 눌리는 비율
        [SerializeField, Min(0f)]         private float _vibrateAmount      = 0.05f;  // m, 렉프레임 동안 떠는 최소 크기
        [SerializeField, Range(0f, 0.1f)] private float _vibrateScreenRatio = 0.008f; // 맞은 자리에서 보이는 화면 높이에 대한 떠는 크기 비율

        [Header("넉백")]
        [SerializeField, Min(0f)] private float _knockbackSpeed = 4f;  // m/s
        [SerializeField, Min(0f)] private float _knockbackDrag  = 12f; // 1/s, 작을수록 멀리 미끄러진다

        [Header("처치")]
        [SerializeField, Min(0f)] private float _flyMultiplier = 1f; // 처치되어 날아가는 세기 배율

        [Header("불똥 (개수)")]
        [SerializeField, Min(0)] private int _sparkCount     = 10;
        [SerializeField, Min(0)] private int _killSparkCount = 28;

        [Header("치명타 (서버가 치명타로 알렸을 때만)")]
        [SerializeField, Min(0)]          private int   _critExtraLagFrames   = 2;     // 렉프레임에 더한다
        [SerializeField, Range(0f, 1f)]   private float _critFlashAlpha       = 0.25f;  // 화면 번쩍임의 세기. 0이면 없다
        [SerializeField, Min(0.01f)]      private float _critFlashSeconds     = 0.08f;
        [SerializeField, Min(1f)]         private float _critSparkMultiplier  = 2.5f;   // 불똥 개수 배율
        [SerializeField, Min(1f)]         private float _critPunchMultiplier  = 1.6f;   // 맞은 몸 찌그러짐 배율
        [SerializeField, Min(1f)]         private float _critKnockbackMultiplier = 1.5f; // 넉백 속도 배율
        [SerializeField, Min(0f)]         private float _critRingRadius       = 1.4f;   // m, 맞은 자리의 충격 링. 0이면 없다
        [SerializeField, Range(0f, 1f)]   private float _critMotionBlurIntensity = 0.8f; // 화면 모션블러의 세기. 0이면 없다
        [SerializeField, Min(0.01f)]      private float _critMotionBlurSeconds   = 0.45f;
        [SerializeField, Range(0f, 0.5f)] private float _critZoomPunch    = 0.08f; // 화면이 당겨지는 비율. 0이면 없다
        [SerializeField, Min(0.01f)]      private float _critZoomSeconds  = 0.3f;

        [Header("충격파 (반지름이 0이면 없음)")]
        [SerializeField, Min(0f)]         private float _shockwaveRadius = 0f;  // m
        [SerializeField, Min(0f)]         private float _shockwaveSpeed  = 14f; // m/s, 중심에서 밀리는 속도
        [SerializeField, Range(0f, 0.5f)] private float _zoomPunch       = 0f;  // 화면이 당겨지는 비율
        [SerializeField, Min(0.01f)]      private float _zoomSeconds     = 0.3f;

        public int   HitLagFrames       => _hitLagFrames;
        public int   KillExtraLagFrames => _killExtraLagFrames;
        public int   TargetExtraLagFrames => _targetExtraLagFrames;
        public float ShakeAmount        => _shakeAmount;
        public float KillShakeAmount    => _killShakeAmount;
        public float ShakeSeconds       => _shakeSeconds;
        public float FlashSeconds       => _flashSeconds;
        public float PunchAmount        => _punchAmount;
        public float VibrateAmount      => _vibrateAmount;
        public float VibrateScreenRatio => _vibrateScreenRatio;
        public float KnockbackSpeed     => _knockbackSpeed;
        public float KnockbackDrag      => _knockbackDrag;
        public float FlyMultiplier      => _flyMultiplier;
        public int   SparkCount         => _sparkCount;
        public int   KillSparkCount     => _killSparkCount;
        public int   CritExtraLagFrames  => _critExtraLagFrames;
        public float CritFlashAlpha      => _critFlashAlpha;
        public float CritFlashSeconds    => _critFlashSeconds;
        public float CritSparkMultiplier => _critSparkMultiplier;
        public float CritPunchMultiplier => _critPunchMultiplier;
        public float CritKnockbackMultiplier => _critKnockbackMultiplier;
        public float CritRingRadius      => _critRingRadius;
        public float CritMotionBlurIntensity => _critMotionBlurIntensity;
        public float CritMotionBlurSeconds   => _critMotionBlurSeconds;
        public float CritZoomPunch       => _critZoomPunch;
        public float CritZoomSeconds     => _critZoomSeconds;
        public float ShockwaveRadius    => _shockwaveRadius;
        public float ShockwaveSpeed     => _shockwaveSpeed;
        public float ZoomPunch          => _zoomPunch;
        public float ZoomSeconds        => _zoomSeconds;
    }
}
