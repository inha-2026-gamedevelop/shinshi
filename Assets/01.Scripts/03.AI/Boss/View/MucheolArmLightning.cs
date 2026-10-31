// Unity
using UnityEngine;

namespace Shinshi.AI
{
    /// <summary> 무철 손에 두르는 전기, 평소에는 약하게 지직거리고 팔을 휘두르는 동안은 줄기가 늘고 길고 굵어진다 </summary>
    public sealed class MucheolArmLightning : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int   MIN_POINT_COUNT = 2;
        private const float MIN_INTERVAL    = 0.005f;
        private const float MIN_RANGE_SCALE = 0.01f;
        private const float MIN_LENGTH_RATE = 0.6f;   // 줄기마다 길이가 들쭉날쭉하도록 뽑는 하한
        private const float TIP_JITTER_FADE = 0.5f;   // 끝으로 갈수록 꺾임이 줄어드는 정도

        [SerializeField] private LineRenderer[] _bolts;                      // 줄기 하나당 LineRenderer 하나, 로컬 좌표로 그린다
        [SerializeField, Min(MIN_POINT_COUNT)] private int _pointCount = 7;  // 줄기를 이루는 꺾임점 수
        [SerializeField, Min(0f)] private float _refreshInterval = 0.04f;    // 초, 줄기 모양을 새로 뽑는 간격
        [SerializeField, Min(0f)] private float _idleLength      = 0.35f;    // m, 평소 줄기 길이
        [SerializeField, Min(0f)] private float _swingLength     = 0.9f;     // m, 휘두를 때 줄기 길이
        [SerializeField, Min(0f)] private float _idleWidth       = 0.012f;   // m, 평소 줄기 굵기
        [SerializeField, Min(0f)] private float _swingWidth      = 0.035f;   // m, 휘두를 때 줄기 굵기
        [SerializeField, Range(0f, 1f)] private float _jitter    = 0.28f;    // 꺾임이 줄기 길이에서 차지하는 비율
        [SerializeField, Range(0f, 1f)] private float _idleBoltRatio = 0.4f; // 평소에 켜 두는 줄기의 비율
        [SerializeField, Min(0f)] private float _chargeSpeed     = 14f;     // 1/초, 세기가 목표를 따라가는 속도

        private float _intensity;      // 0이면 평소, 1이면 휘두르는 중
        private float _targetIntensity;
        private float _nextRefresh;
        private float _rangeScale = 1f;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnEnable()
        {
            _intensity = 0f;
            _targetIntensity = 0f;
            _nextRefresh = 0f;
            Refresh();
        }

        private void Update()
        {
            _intensity = Mathf.MoveTowards(_intensity, _targetIntensity, _chargeSpeed * Time.deltaTime);

            _nextRefresh -= Time.deltaTime;
            if (_nextRefresh <= 0f)
            {
                _nextRefresh = Mathf.Max(_refreshInterval, MIN_INTERVAL);
                Refresh();
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 휘두르는 중이면 세게, 아니면 평소 세기로 </summary>
        public void SetSwinging(bool isSwinging)
        {
            _targetIntensity = isSwinging ? 1f : 0f;
        }

        /// <summary> 공격 범위 배율, 줄기 길이가 범위만큼 늘어난다 </summary>
        public void SetRangeScale(float scale)
        {
            _rangeScale = Mathf.Max(scale, MIN_RANGE_SCALE);
        }

        private void Refresh()
        {
            if (_bolts == null)
            {
                return;
            }

            float length = Mathf.Lerp(_idleLength, _swingLength, _intensity) * _rangeScale;
            float width  = Mathf.Lerp(_idleWidth, _swingWidth, _intensity) * _rangeScale;
            float ratio  = Mathf.Lerp(_idleBoltRatio, 1f, _intensity);
            int   active = Mathf.CeilToInt(_bolts.Length * ratio);

            for (int i = 0; i < _bolts.Length; i++)
            {
                LineRenderer bolt = _bolts[i];
                if (bolt == null)
                {
                    continue;
                }

                bool isOn = i < active;
                bolt.enabled = isOn;
                if (!isOn)
                {
                    continue;
                }

                bolt.widthMultiplier = width;
                Shape(bolt, length * Random.Range(MIN_LENGTH_RATE, 1f));
            }
        }

        // 손에서 무작위 방향으로 뻗는 한 줄기, 중간 점마다 가로로 흔들어 번개처럼 꺾는다
        private void Shape(LineRenderer bolt, float length)
        {
            int count = Mathf.Max(_pointCount, MIN_POINT_COUNT);
            if (bolt.positionCount != count)
            {
                bolt.positionCount = count;
            }

            Vector3 direction = Random.onUnitSphere;
            Vector3 side = Vector3.Cross(direction, Random.onUnitSphere).normalized;
            Vector3 up = Vector3.Cross(direction, side);

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                Vector3 point = direction * (length * t);
                if (i > 0)
                {
                    float spread = length * _jitter * (1f - t * TIP_JITTER_FADE);
                    point += side * (spread * Random.Range(-1f, 1f)) + up * (spread * Random.Range(-1f, 1f));
                }

                bolt.SetPosition(i, point);
            }
        }
    }
}
