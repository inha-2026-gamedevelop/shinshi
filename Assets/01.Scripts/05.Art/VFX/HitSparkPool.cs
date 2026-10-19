// Unity
using UnityEngine;

namespace Shinshi.Art
{
    /// <summary> 맞은 자리에서 튀는 불똥 파티클을 돌려 쓰는 풀, 이 클라이언트 화면에만 보이는 표현이다 </summary>
    public sealed class HitSparkPool : MonoBehaviour
    {
        // 파티클 시스템 몇 개를 코드로 만들어 돌아가며 쓴다. 셰이더 이름 검색은 빌드에서 빠질 수 있어 머티리얼은 밖에서 받는다

        /****************************************
        *                Fields
        ****************************************/

        private const int   POOL_SIZE         = 12;
        private const int   MAX_PARTICLES     = 64;
        private const float LIFETIME_MIN      = 0.2f;
        private const float LIFETIME_MAX      = 0.4f;
        private const float SPEED_MIN         = 3f;     // m/s
        private const float SPEED_MAX         = 9f;     // m/s
        private const float SIZE_MIN          = 0.06f;  // m
        private const float SIZE_MAX          = 0.16f;  // m
        private const float GRAVITY_MODIFIER  = 1.5f;
        private const float CONE_ANGLE        = 35f;    // 도
        private const float CONE_RADIUS       = 0.05f;  // m
        private const float UP_BIAS           = 0.4f;   // 튀는 방향에 더하는 위쪽 비율
        private const float MIN_DIRECTION_SQR = 0.0001f;
        private const float CRIT_SIZE_RATIO   = 1.8f;   // 치명타 불똥의 크기 배율

        private static readonly Color s_critColor = new Color(1f, 0.8f, 0.15f, 1f);
        private static readonly Color s_hotColor  = new Color(1f, 0.95f, 0.6f, 1f);
        private static readonly Color s_warmColor = new Color(1f, 0.55f, 0.15f, 1f);

        private readonly ParticleSystem[] _pool = new ParticleSystem[POOL_SIZE];

        private int  _next    = 0;
        private bool _isReady = false;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 한 번 터뜨릴 불똥 수. 치명타면 배율을 곱하고 풀 한 칸의 상한을 넘지 않는다 </summary>
        public static int ResolveCount(int baseCount, bool isCritical, float critMultiplier)
        {
            int count = isCritical ? Mathf.RoundToInt(baseCount * critMultiplier) : baseCount;
            return Mathf.Clamp(count, 0, MAX_PARTICLES);
        }

        /// <summary> 불똥 풀을 만든다. 머티리얼이 없으면 아무것도 그리지 않는다 </summary>
        public void Initialize(Material material)
        {
            if ((_isReady) || (material == null))
            {
                return;
            }

            for (int i = 0; i < POOL_SIZE; ++i)
            {
                _pool[i] = CreateSystem(i, material);
            }

            _isReady = true;
        }

        /// <summary> point에서 direction 쪽으로 불똥을 count개 터뜨린다. 치명타면 금색의 큰 불똥이다 </summary>
        public void Burst(Vector3 point, Vector3 direction, int count, bool isCritical)
        {
            if ((!_isReady) || (count <= 0))
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

            if (!isCritical)
            {
                system.Emit(count);
                return;
            }

            var emitParams = new ParticleSystem.EmitParams
            {
                startColor           = s_critColor,
                startSize            = SIZE_MAX * CRIT_SIZE_RATIO,
                applyShapeToPosition = true
            };
            system.Emit(emitParams, count);
        }

        private ParticleSystem CreateSystem(int index, Material material)
        {
            var go = new GameObject($"HitSparks{index}");
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
            particleRenderer.sharedMaterial = material;
            particleRenderer.renderMode     = ParticleSystemRenderMode.Billboard;

            return system;
        }
    }
}
