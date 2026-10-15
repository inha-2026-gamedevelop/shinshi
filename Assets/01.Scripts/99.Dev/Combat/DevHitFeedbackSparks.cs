// Unity
using UnityEngine;

namespace Shinshi.Dev
{
    /// <summary> 맞은 자리에서 튀는 불똥 파티클을 돌려 쓰는 시험용 풀 </summary>
    public sealed class DevHitFeedbackSparks : MonoBehaviour
    {
        // 파티클 시스템 몇 개를 코드로 만들어 돌아가며 쓴다. 에셋 없이 Sprites/Default 셰이더 하나만 쓴다

        /****************************************
        *                Fields
        ****************************************/

        private const string SHADER_NAME = "Sprites/Default";

        private const int   POOL_SIZE        = 12;
        private const int   MAX_PARTICLES    = 64;
        private const int   HIT_COUNT        = 10;
        private const int   KILL_COUNT       = 28;
        private const float LIFETIME_MIN     = 0.2f;
        private const float LIFETIME_MAX     = 0.4f;
        private const float SPEED_MIN        = 3f;  // m/s
        private const float SPEED_MAX        = 9f;  // m/s
        private const float SIZE_MIN         = 0.06f; // m
        private const float SIZE_MAX         = 0.16f; // m
        private const float GRAVITY_MODIFIER = 1.5f;
        private const float CONE_ANGLE       = 35f;  // 도
        private const float CONE_RADIUS      = 0.05f; // m
        private const float UP_BIAS          = 0.4f; // 튀는 방향에 더하는 위쪽 비율
        private const float MIN_DIRECTION_SQR = 0.0001f;

        private static readonly Color s_hotColor  = new Color(1f, 0.95f, 0.6f, 1f);
        private static readonly Color s_warmColor = new Color(1f, 0.55f, 0.15f, 1f);

        private readonly ParticleSystem[] _pool = new ParticleSystem[POOL_SIZE];

        private Material _material = null;
        private int       _next     = 0;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Awake()
        {
            // 시험용 씬이라 머티리얼 에셋을 따로 두지 않고 Awake에서 한 번만 이름으로 찾는다
            Shader shader = Shader.Find(SHADER_NAME);
            if (shader == null)
            {
                Debug.LogError($"[DevSparks] 셰이더를 찾지 못했다: {SHADER_NAME}", this);
                enabled = false;
                return;
            }

            _material = new Material(shader);
            for (int i = 0; i < POOL_SIZE; ++i)
            {
                _pool[i] = CreateSystem(i);
            }
        }

        private void OnDestroy()
        {
            if (_material != null)
            {
                Destroy(_material);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> point에서 direction 쪽으로 불똥을 터뜨린다. 처치 때는 더 많이 튄다 </summary>
        public void Burst(Vector3 point, Vector3 direction, bool isKill)
        {
            if (!enabled)
            {
                return;
            }

            Vector3 aim = direction + (Vector3.up * UP_BIAS);
            if (aim.sqrMagnitude <= MIN_DIRECTION_SQR)
            {
                aim = Vector3.up;
            }

            ParticleSystem system = _pool[_next];
            _next = (_next + 1) % POOL_SIZE;

            system.transform.SetPositionAndRotation(point, Quaternion.LookRotation(aim.normalized, Vector3.up));
            system.Play();
            system.Emit(isKill ? KILL_COUNT : HIT_COUNT);
        }

        private ParticleSystem CreateSystem(int index)
        {
            var go = new GameObject($"DevSparks{index}");
            go.transform.SetParent(transform, false);

            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = system.main;
            main.loop            = false;
            main.playOnAwake     = false;
            main.maxParticles    = MAX_PARTICLES;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = GRAVITY_MODIFIER;
            main.startLifetime   = new ParticleSystem.MinMaxCurve(LIFETIME_MIN, LIFETIME_MAX);
            main.startSpeed      = new ParticleSystem.MinMaxCurve(SPEED_MIN, SPEED_MAX);
            main.startSize       = new ParticleSystem.MinMaxCurve(SIZE_MIN, SIZE_MAX);
            main.startColor      = new ParticleSystem.MinMaxGradient(s_hotColor, s_warmColor);

            // 개수는 Emit으로 직접 정한다
            var emission = system.emission;
            emission.enabled = false;

            var shape = system.shape;
            shape.enabled   = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle     = CONE_ANGLE;
            shape.radius    = CONE_RADIUS;

            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size    = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

            var particleRenderer = go.GetComponent<ParticleSystemRenderer>();
            particleRenderer.sharedMaterial = _material;
            particleRenderer.renderMode     = ParticleSystemRenderMode.Billboard;

            return system;
        }
    }
}
