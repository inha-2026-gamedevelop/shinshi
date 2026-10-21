#if UNITY_EDITOR
// Unity
using UnityEngine;

namespace Shinshi.AI
{
    /// <summary> 방향 봉인의 연출 비교용 시간표. 실제 타격이나 체력 판정을 수행하지 않는다. </summary>
    public sealed class MucheolSealPreview : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private Transform[] _anchors;
        [SerializeField] private Renderer[] _markers;
        [SerializeField] private Vector3[] _offsets;
        [SerializeField] private Vector3[] _localAngles;
        [SerializeField] private float _revealTime = 3.4f;
        [SerializeField] private float _directionSeconds = 4f;
        [SerializeField] private int _hitsPerDirection = 5;
        [SerializeField] private float _breakFade = 0.4f;
        [SerializeField] private float _size = 2f;

        private static readonly int G_Progress = Shader.PropertyToID("_Progress");
        private static readonly int G_Pulse = Shader.PropertyToID("_Pulse");
        private static readonly int G_Opacity = Shader.PropertyToID("_Opacity");
        private static readonly int G_Phase = Shader.PropertyToID("_Phase");
        private MaterialPropertyBlock _block;

        /****************************************
        *                Methods
        ****************************************/

        public string Describe(float time)
        {
            if (time < _revealTime)
            {
                return "봉인 발동 · 진행 중인 패턴 종료 대기";
            }
            int index = Mathf.FloorToInt((time - _revealTime) / _directionSeconds);
            if (index >= _markers.Length)
            {
                return "봉인 해제 · 체력 고정 해제";
            }
            int hits = Mathf.FloorToInt((time - _revealTime - index * _directionSeconds)
                / (_directionSeconds / _hitsPerDirection));
            string direction = index == 0 ? "오른쪽" : (index == 1 ? "왼쪽" : "후방");
            return direction + " 부적 · " + hits + " / " + _hitsPerDirection + "타 (자동 시연)";
        }

        public void Sample(float time, bool visible)
        {
            gameObject.SetActive(visible);
            if (!visible)
            {
                return;
            }
            if (_block == null)
            {
                _block = new MaterialPropertyBlock();
            }
            float interval = _directionSeconds / _hitsPerDirection;
            for (int i = 0; i < _markers.Length; ++i)
            {
                float age = time - _revealTime - i * _directionSeconds;
                bool active = age >= 0f && age < _directionSeconds + _breakFade;
                Renderer marker = _markers[i];
                marker.gameObject.SetActive(active);
                if (!active)
                {
                    continue;
                }
                float dissolve = Mathf.Clamp01((age - _directionSeconds) / _breakFade);
                int hits = Mathf.Clamp(Mathf.FloorToInt(age / interval), 0, _hitsPerDirection);
                float sinceHit = age - hits * interval;
                float pulse = hits > 0 ? Mathf.Clamp01(1f - sinceHit / 0.2f) : 0f;
                Transform anchor = _anchors[i];
                marker.transform.position = anchor.TransformPoint(_offsets[i]);
                marker.transform.rotation = anchor.rotation * Quaternion.Euler(_localAngles[i]);
                marker.transform.localScale = Vector3.one * _size * (1f + pulse * 0.12f + dissolve * 0.7f);
                _block.SetFloat(G_Progress, (float)hits / _hitsPerDirection);
                _block.SetFloat(G_Pulse, pulse);
                _block.SetFloat(G_Opacity, Mathf.Clamp01(age / 0.2f) * (1f - dissolve));
                _block.SetFloat(G_Phase, age);
                marker.SetPropertyBlock(_block);
            }
        }
    }
}
#endif
