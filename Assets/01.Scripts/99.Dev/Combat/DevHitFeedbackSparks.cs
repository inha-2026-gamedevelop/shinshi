// Unity
using UnityEngine;

namespace Shinshi.Dev
{
    /// <summary> 맞은 자리에서 쇠를 갈 때처럼 길게 그으며 튀는 불똥 파티클을 돌려 쓰는 시험용 풀 </summary>
    public sealed class DevHitFeedbackSparks : MonoBehaviour
    {
        // 파티클 시스템 몇 개를 코드로 만들어 돌아가며 쓴다. 에셋 없이 내장 셰이더만 쓴다. 불똥은 속도 방향으로 늘어난 가는 줄이고,
        // 하얗게 달궈졌다가 주황, 붉은색으로 식으며 사라지고, 바닥에 닿으면 한두 번 튄다

        /****************************************
        *                Fields
        ****************************************/

        private const string SHADER_NAME = "Sprites/Default";

        private const int   POOL_SIZE        = 12;
        private const int   MAX_PARTICLES    = 64;
        private const float LIFETIME_MIN     = 0.25f;
        private const float LIFETIME_MAX     = 0.55f;
        private const float SPEED_MIN        = 5f;  // m/s
        private const float SPEED_MAX        = 16f; // m/s
        private const float SIZE_MIN         = 0.035f; // m, 줄의 굵기
        private const float SIZE_MAX         = 0.08f;  // m
        private const float GRAVITY_MODIFIER = 2.2f;
        private const float DRAG             = 0.8f;  // 1/s, 날아가며 조금씩 느려진다
        private const float CONE_ANGLE       = 50f;   // 도
        private const float CONE_RADIUS      = 0.08f; // m
        private const float UP_BIAS          = 0.25f; // 튀는 방향에 더하는 위쪽 비율
        private const float MIN_DIRECTION_SQR = 0.0001f;
        private const float STREAK_VELOCITY_SCALE = 0.07f; // 줄 길이에 속도가 더하는 비율
        private const float STREAK_LENGTH_SCALE   = 1.2f;
        private const float BOUNCE           = 0.35f;
        private const float BOUNCE_DAMPEN    = 0.35f;
        private const float BOUNCE_LIFETIME_LOSS = 0.25f;
        private const float COLLISION_RADIUS = 0.05f;

        private const float CRIT_SIZE_RATIO = 1.7f; // 치명타 불똥의 굵기 배율

        private static readonly Color s_critColor = new Color(1f, 0.8f, 0.15f, 1f);
        private static readonly Color s_hotColor  = new Color(1f, 0.97f, 0.75f, 1f);
        private static readonly Color s_warmColor = new Color(1f, 0.62f, 0.18f, 1f);
        private static readonly Color s_coolColor = new Color(0.9f, 0.18f, 0.05f, 1f); // 식은 불똥의 끝 색

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

        /// <summary> point에서 direction 쪽으로 불똥을 count개 터뜨린다. 치명타면 금색의 큰 불똥이다 </summary>
        public void Burst(Vector3 point, Vector3 direction, int count, bool isCritical)
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
            main.startRotation   = 0f;
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

            // 날아가며 굵기가 줄어 끝이 가늘게 꺼진다
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size    = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.15f));

            var limit = system.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen  = 0f;
            limit.drag    = DRAG;

            // 하얗게 달궈졌다가 주황, 붉은색으로 식으며 투명해진다
            var color = system.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(s_warmColor, 0.45f),
                    new GradientColorKey(s_coolColor, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 0.55f),
                    new GradientAlphaKey(0f, 1f)
                });
            color.color = new ParticleSystem.MinMaxGradient(gradient);

            // 바닥이나 바위에 닿으면 한두 번 튄다. 움직이는 충돌체(몬스터, 플레이어)와는 부딪치지 않는다
            var collision = system.collision;
            collision.enabled             = true;
            collision.type                = ParticleSystemCollisionType.World;
            collision.mode                = ParticleSystemCollisionMode.Collision3D;
            collision.quality             = ParticleSystemCollisionQuality.Low;
            collision.enableDynamicColliders = false;
            collision.bounce              = BOUNCE;
            collision.dampen              = BOUNCE_DAMPEN;
            collision.lifetimeLoss        = BOUNCE_LIFETIME_LOSS;
            collision.radiusScale         = COLLISION_RADIUS;

            var particleRenderer = go.GetComponent<ParticleSystemRenderer>();
            particleRenderer.sharedMaterial = _material;
            particleRenderer.renderMode     = ParticleSystemRenderMode.Stretch;
            particleRenderer.velocityScale  = STREAK_VELOCITY_SCALE;
            particleRenderer.lengthScale    = STREAK_LENGTH_SCALE;
            particleRenderer.cameraVelocityScale = 0f;

            return system;
        }
    }
}
