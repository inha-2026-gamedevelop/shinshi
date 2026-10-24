// Unity
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Shinshi.Dev
{
    /// <summary> 치명타 순간에 화면 중심에서 바깥으로 번지는 방사형 블러를 잠깐 걸었다가 푸는 연출 </summary>
    public sealed class HitFeedbackMotionBlur : MonoBehaviour
    {
        // 이 클라이언트 화면에만 거는 표현이다. URP 모션블러는 카메라가 프레임마다 움직인 만큼만 번져서 짧은 줌 펀치로는 거의 보이지 않으므로,
        // 렌더러에 붙인 풀스크린 패스를 쓴다. 평소에는 패스를 꺼 두어 비용을 내지 않고, 켜는 동안만 셰이더의 세기를 올렸다 내린다

        /****************************************
        *                Fields
        ****************************************/

        private static readonly int _strengthId = Shader.PropertyToID("_Strength");

        private const float STRENGTH_AT_MAX = 0.5f; // 세기 1일 때 화면 가장자리가 중심 쪽으로 끌리는 비율
        private const float HOLD_REMAIN     = 0.6f; // 남은 시간이 이 비율 아래로 내려가야 풀리기 시작한다. 그 전에는 최대 세기를 유지한다

        private FullScreenPassRendererFeature _feature  = null;
        private Material                      _material = null;
        private float                         _peak     = 0f;
        private float                         _seconds  = 1f;
        private float                         _left     = 0f;

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
                Stop();
            }
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

        // 처음 얼마 동안은 최대 세기를 유지하다가 부드럽게 풀린다
        private float ReadLevel()
        {
            float remain = Mathf.Clamp01((_left / _seconds) / HOLD_REMAIN);
            return _peak * Mathf.SmoothStep(0f, 1f, remain);
        }

        private void Stop()
        {
            _left = 0f;
            if ((_feature == null) || (_material == null))
            {
                return;
            }

            _material.SetFloat(_strengthId, 0f);
            _feature.SetActive(false);
        }
    }
}
