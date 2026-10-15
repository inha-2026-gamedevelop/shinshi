// Unity
using UnityEngine;
using UnityEngine.Serialization;

namespace Shinshi.AI
{
    /// <summary> 월백 등장 마법진의 파티클을 지정한 시각에 더 만들지 않게 멈추고 완전히 사라진 시각을 로그로 남기는 확인용 도구 </summary>
    public sealed class AppearVfxTimer : MonoBehaviour
    {
        /****************************************
        *                Fields
        ****************************************/

        [FormerlySerializedAs("stopTime")]
        [SerializeField] private float _stopTime = 2.5f;

        private ParticleSystem[] _systems;
        private float            _startTime;
        private bool             _isStopped;
        private bool             _isReported;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Start()
        {
            _systems   = GetComponentsInChildren<ParticleSystem>(true);
            _startTime = Time.time;
        }

        private void Update()
        {
            if ((_systems == null) || (_systems.Length == 0))
            {
                return;
            }

            float elapsed = Time.time - _startTime;

            if ((!_isStopped) && (elapsed >= _stopTime))
            {
                StopEmitting(elapsed);
            }

            if ((_isStopped) && (!_isReported) && (!_systems[0].IsAlive(true)))
            {
                _isReported = true;
                Debug.Log($"[AppearVfx] 완전히 사라짐 {elapsed:F2}초, 중지 후 {(elapsed - _stopTime):F2}초");
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        private void StopEmitting(float elapsed)
        {
            _isStopped = true;

            for (int i = 0; i < _systems.Length; ++i)
            {
                _systems[i].Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }

            Debug.Log($"[AppearVfx] 생성 중지 {elapsed:F2}초");
        }
    }
}
