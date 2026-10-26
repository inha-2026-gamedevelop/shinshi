// Unity
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Shinshi.Dev
{
    /// <summary> 치명타 순간에 화면 중심에서 바깥으로 번지는 방사형 블러를 잠깐 걸었다가 푸는 연출. 같은 패스로 1~2프레임 임팩트 프레임도 낸다 </summary>
    public sealed class HitFeedbackMotionBlur : MonoBehaviour
    {
        // 이 클라이언트 화면에만 거는 표현이다. URP 모션블러는 카메라가 프레임마다 움직인 만큼만 번져서 짧은 줌 펀치로는 거의 보이지 않으므로,
        // 렌더러에 붙인 풀스크린 패스를 쓴다. 평소에는 패스를 꺼 두어 비용을 내지 않고, 켜는 동안만 셰이더의 세기를 올렸다 내린다

        /****************************************
        *                Fields
        ****************************************/

        private static readonly int _strengthId        = Shader.PropertyToID("_Strength");
        private static readonly int _impactId          = Shader.PropertyToID("_Impact");
        private static readonly int _impactThresholdId = Shader.PropertyToID("_ImpactThreshold");
        private static readonly int _impactHotId       = Shader.PropertyToID("_ImpactHotThreshold");
        private static readonly int _impactInvertId    = Shader.PropertyToID("_ImpactInvert");
        private static readonly int _impactSoftId      = Shader.PropertyToID("_ImpactSoft");
        private static readonly int _impactDimId       = Shader.PropertyToID("_ImpactDim");

        private const int NO_IMPACT = -1;

        private const float STRENGTH_AT_MAX = 0.5f; // 세기 1일 때 화면 가장자리가 중심 쪽으로 끌리는 비율
        private const float HOLD_REMAIN     = 0.6f; // 남은 시간이 이 비율 아래로 내려가야 풀리기 시작한다. 그 전에는 최대 세기를 유지한다

        private FullScreenPassRendererFeature _feature        = null;
        private Material                      _material       = null;
        private float                         _peak           = 0f;
        private float                         _seconds        = 1f;
        private float                         _left           = 0f;
        private int                           _impactEndFrame = NO_IMPACT; // 이 프레임부터 임팩트 프레임을 끈다
        private int                           _impactFrames   = 1;
        private bool                          _isImpactFading = false;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Update()
        {
            if (_left <= 0f)
            {
                return;
            }

            _left = Mathf.Max(0f, _left - Time.unscaledDeltaTime);
            _material.SetFloat(_strengthId, ReadLevel() * STRENGTH_AT_MAX);

            if (_left <= 0f)
            {
                _material.SetFloat(_strengthId, 0f);
                ApplyActive();
            }
        }

        // 타격은 Update 중에 들어오고 렌더는 모든 LateUpdate 뒤에 하므로, 끄는 판단을 여기서 해야 정확히 frames 장이 그려진다.
        // 부드러운 방식은 남은 장 수에 비례해 세기를 줄여 한 번에 꺼지는 깜빡임을 없앤다
        private void LateUpdate()
        {
            if (_impactEndFrame == NO_IMPACT)
            {
                return;
            }

            int remaining = _impactEndFrame - Time.frameCount;
            if (remaining > 0)
            {
                _material.SetFloat(_impactId, _isImpactFading ? remaining / (float)_impactFrames : 1f);
                return;
            }

            _impactEndFrame = NO_IMPACT;
            _material.SetFloat(_impactId, 0f);
            ApplyActive();
        }

        private void OnDestroy()
        {
            Stop();
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 렌더러의 풀스크린 패스와 그 재질을 연결한다. 연결하지 않으면 Pulse는 아무것도 하지 않는다 </summary>
        public void Init(FullScreenPassRendererFeature feature, Material material)
        {
            _feature  = feature;
            _material = material;
            Stop();
        }

        /// <summary> 블러가 intensity 세기로 걸렸다가 seconds 동안 풀린다. 진행 중인 것보다 약하면 무시한다 </summary>
        public void Pulse(float intensity, float seconds)
        {
            if ((_feature == null) || (_material == null) || (intensity <= 0f))
            {
                return;
            }

            if ((_left > 0f) && (intensity < ReadLevel()))
            {
                return;
            }

            _peak    = Mathf.Clamp01(intensity);
            _seconds = Mathf.Max(seconds, 0.01f);
            _left    = _seconds;

            _material.SetFloat(_strengthId, _peak * STRENGTH_AT_MAX);
            _feature.SetActive(true);
        }

        /// <summary> 이번 프레임부터 frames 장을 임팩트 프레임으로 그린다. 부드러운 방식은 배경을 dim만큼 어두운 회색으로 하고 hotThreshold보다 밝은 곳만 색을 남기며 서서히 풀린다.
        /// 강한 방식은 threshold로 배경을 흑백으로 가르고(invert면 밝은 쪽이 검게) hotThreshold보다 밝은 곳을 강조색으로 칠한다 </summary>
        public void ImpactFrame(int frames, bool isSoft, float dim, float threshold, float hotThreshold, bool invert)
        {
            if ((_feature == null) || (_material == null) || (frames <= 0))
            {
                return;
            }

            _impactEndFrame = Time.frameCount + frames;
            _impactFrames   = frames;
            _isImpactFading = isSoft;
            _material.SetFloat(_impactId, 1f);
            _material.SetFloat(_impactSoftId, isSoft ? 1f : 0f);
            _material.SetFloat(_impactDimId, dim);
            _material.SetFloat(_impactThresholdId, threshold);
            _material.SetFloat(_impactHotId, hotThreshold);
            _material.SetFloat(_impactInvertId, invert ? 1f : 0f);
            _feature.SetActive(true);
        }

        // 처음 얼마 동안은 최대 세기를 유지하다가 부드럽게 풀린다
        private float ReadLevel()
        {
            float remain = Mathf.Clamp01((_left / _seconds) / HOLD_REMAIN);
            return _peak * Mathf.SmoothStep(0f, 1f, remain);
        }

        // 블러와 임팩트 프레임이 같은 패스를 쓰므로 둘 다 끝났을 때만 끈다. 평소에는 꺼 두어 비용을 내지 않는다
        private void ApplyActive()
        {
            _feature.SetActive((_left > 0f) || (_impactEndFrame != NO_IMPACT));
        }

        private void Stop()
        {
            _left           = 0f;
            _impactEndFrame = NO_IMPACT;
            if ((_feature == null) || (_material == null))
            {
                return;
            }

            _material.SetFloat(_strengthId, 0f);
            _material.SetFloat(_impactId, 0f);
            _feature.SetActive(false);
        }
    }
}
