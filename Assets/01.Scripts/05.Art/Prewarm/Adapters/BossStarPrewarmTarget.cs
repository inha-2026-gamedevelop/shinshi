// Unity
using UnityEngine;

namespace Shinshi.Art
{
    // 별형 구조는 빔 메시와 여우불 구체 8개를 한 컨트롤러가 만든다. 구체만 따로 그리는 것보다 운영과 같은
    // 생성(MeshFilter, MeshRenderer, 컨트롤러 순서)과 Initialize, Show, Render를 그대로 타는 쪽이 정확해서 컨트롤러를 직접 쓴다
    /// <summary> 별형 회전 빔 구조를 운영과 같은 경로로 만들어 예고와 활성 상태를 그린다 </summary>
    [DisallowMultipleComponent]
    public sealed class BossStarPrewarmTarget : MonoBehaviour, IPrewarmTarget
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string CONTROLLER_NAME   = "StarGroundVFX Prewarm";
        private const float  RING_RADIUS       = 2.5f;
        private const int    VERTEX_COUNT      = 5;
        private const int    CONNECT_STEP      = 2;
        private const float  BEAM_WIDTH        = 0.8f;
        private const float  TELEGRAPH_SECONDS = 0.25f;
        private const float  ACTIVE_SECONDS    = 0.35f;
        private const float  SPIN_DEGREES      = 90f;
        private const float  FULL_VALUE        = 1f;
        private const float  NO_ROTATION       = 0f;
        private const float  NO_PULSE_AGE      = float.MaxValue;
        private const bool   ROTATES_RIGHT     = true;

        /****************************************
        *                Fields
        ****************************************/

        [SerializeField] private Material _beamMaterial;
        [SerializeField] private Material _orbMaterial;

        private StarGroundVFXController _visual;

        /****************************************
        *              Unity Event
        ****************************************/

        private void OnDestroy()
        {
            EndPrewarm();
        }

        /****************************************
        *                Methods
        ****************************************/

        public void BeginPrewarm()
        {
            EndPrewarm();

            GameObject go = new GameObject(CONTROLLER_NAME);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
            _visual = go.AddComponent<StarGroundVFXController>();
            _visual.Initialize(_beamMaterial, _orbMaterial);

            Vector3 forward = transform.forward;
            forward.y = 0f;
            _visual.Show(transform.position, forward, RING_RADIUS, VERTEX_COUNT, CONNECT_STEP, BEAM_WIDTH, ROTATES_RIGHT);
        }

        public void TickPrewarm(float elapsedSeconds)
        {
            if (_visual == null)
            {
                return;
            }

            if (elapsedSeconds < TELEGRAPH_SECONDS)
            {
                _visual.Render(false, elapsedSeconds / TELEGRAPH_SECONDS, NO_PULSE_AGE, FULL_VALUE, NO_ROTATION, elapsedSeconds);
                return;
            }

            float activeAge = elapsedSeconds - TELEGRAPH_SECONDS;
            _visual.Render(true, FULL_VALUE - Mathf.Clamp01(activeAge / ACTIVE_SECONDS), activeAge, FULL_VALUE,
                activeAge * SPIN_DEGREES, elapsedSeconds);
        }

        public void EndPrewarm()
        {
            if (_visual == null)
            {
                return;
            }

            _visual.Hide();
            Destroy(_visual.gameObject);
            _visual = null;
        }
    }
}
