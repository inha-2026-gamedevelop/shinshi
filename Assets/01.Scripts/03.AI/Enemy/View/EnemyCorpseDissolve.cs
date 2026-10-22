// System
using System.Collections.Generic;

// Unity
using UnityEngine;

namespace Shinshi.AI
{
    /// <summary> 일반 적 시체가 재처럼 타들어 가며 사라지게 하는 연출. 재질을 타는 셰이더로 바꾸고 몸 표면에서 재와 불씨 입자를 날린다 </summary>
    public sealed class EnemyCorpseDissolve : MonoBehaviour
    {
        // 이 클라이언트 화면에만 보이는 표현이다. 시작하면 seconds 동안 진행되고 끝나도 소유자를 지우지 않는다.
        // 원래 재질의 _BaseMap을 이어받아 모델 색을 유지한다. 셰이더는 빌드에 확실히 들어가도록 Resources에서 한 번만 읽는다

        /****************************************
        *                Fields
        ****************************************/

        private const string SHADER_RESOURCE = "SH_EnemyCorpseDissolve";
        private const string PARTICLE_SHADER = "Sprites/Default";
        private const string BASE_MAP        = "_BaseMap";
        private const string BASE_COLOR      = "_BaseColor";
        private const string DISSOLVE        = "_Dissolve";

        private const float DISSOLVE_START = -0.4f; // 아직 아무것도 타지 않았고 재 띠도 닿지 않은 값
        private const float DISSOLVE_END   = 1.05f; // 잡음의 최댓값까지 모두 사라진 값

        private const int   MAX_PARTICLES  = 120;
        private const float EMIT_RATE      = 100f;
        private const float LIFETIME_MIN   = 0.7f;
        private const float LIFETIME_MAX   = 1.5f;
        private const float SIZE_MIN       = 0.03f; // m
        private const float SIZE_MAX       = 0.09f; // m
        private const float RISE_SPEED     = 0.9f;  // m/s
        private const float SPREAD_SPEED   = 0.5f;  // m/s
        private const float NOISE_STRENGTH = 0.6f;
        private const float GRAVITY        = -0.05f;
        private const float HALF           = 0.5f;
        private const float FADE_KEEP      = 0.6f;  // 수명 중 이 지점까지 불투명하게 둔다

        private static readonly Color s_ashColor   = new Color(0.12f, 0.11f, 0.1f, 1f);
        private static readonly Color s_emberColor = new Color(1f, 0.45f, 0.1f, 1f);

        private static Shader   s_shader;
        private static Material s_particleMaterial;

        private Material[] _materials;
        private float      _seconds = 1f;
        private float      _elapsed;

        /****************************************
        *              Unity Event
        ****************************************/

        // 도메인 리로드를 끈 플레이 모드 진입 때 지난 판의 참조가 남지 않게 한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_shader           = null;
            s_particleMaterial = null;
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;

            float value = Mathf.Lerp(DISSOLVE_START, DISSOLVE_END, Mathf.Clamp01(_elapsed / _seconds));
            for (int i = 0; i < _materials.Length; ++i)
            {
                _materials[i].SetFloat(DISSOLVE, value);
            }
        }

        private void OnDestroy()
        {
            if (_materials == null)
            {
                return;
            }

            for (int i = 0; i < _materials.Length; ++i)
            {
                Destroy(_materials[i]);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> owner의 렌더러를 타는 재질로 바꾸고 seconds 동안 재가 되어 사라지게 한다. 셰이더를 못 찾으면 false </summary>
        public static bool Begin(GameObject owner, float seconds)
        {
            if (s_shader == null)
            {
                s_shader = Resources.Load<Shader>(SHADER_RESOURCE);
            }

            if (s_shader == null)
            {
                return false;
            }

            EnemyCorpseDissolve dissolve = owner.AddComponent<EnemyCorpseDissolve>();
            dissolve._seconds = seconds;

            List<Material> materials = new List<Material>();
            foreach (Renderer renderer in owner.GetComponentsInChildren<Renderer>())
            {
                Material[] originals = renderer.sharedMaterials;
                Material[] burning   = new Material[originals.Length];
                for (int i = 0; i < originals.Length; ++i)
                {
                    burning[i] = new Material(s_shader);
                    if (originals[i] != null)
                    {
                        if (originals[i].HasProperty(BASE_MAP))
                        {
                            burning[i].SetTexture(BASE_MAP, originals[i].GetTexture(BASE_MAP));
                        }

                        if (originals[i].HasProperty(BASE_COLOR))
                        {
                            burning[i].SetColor(BASE_COLOR, originals[i].GetColor(BASE_COLOR));
                        }
                    }

                    burning[i].SetFloat(DISSOLVE, DISSOLVE_START);
                    materials.Add(burning[i]);
                }

                renderer.sharedMaterials = burning;

                if (renderer is SkinnedMeshRenderer skinned)
                {
                    dissolve.CreateAshParticles(owner.transform, skinned);
                }
            }

            dissolve._materials = materials.ToArray();
            return true;
        }

        // 몸 표면에서 재와 불씨를 위로 날린다. 입자는 월드에 남아 몸이 사라진 뒤에도 잠시 떠돈다
        private void CreateAshParticles(Transform parent, SkinnedMeshRenderer skinned)
        {
            GameObject go = new GameObject("CorpseAshParticles");
            go.transform.SetParent(parent, false);

            ParticleSystem system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = system.main;
            main.loop            = false;
            main.duration        = _seconds;
            main.playOnAwake     = false;
            main.maxParticles    = MAX_PARTICLES;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = GRAVITY;
            main.startLifetime   = new ParticleSystem.MinMaxCurve(LIFETIME_MIN, LIFETIME_MAX);
            main.startSize       = new ParticleSystem.MinMaxCurve(SIZE_MIN, SIZE_MAX);
            main.startColor      = new ParticleSystem.MinMaxGradient(s_ashColor, s_emberColor);

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = EMIT_RATE;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled             = true;
            shape.shapeType           = ParticleSystemShapeType.SkinnedMeshRenderer;
            shape.skinnedMeshRenderer = skinned;

            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space   = ParticleSystemSimulationSpace.World;
            velocity.x       = new ParticleSystem.MinMaxCurve(-SPREAD_SPEED, SPREAD_SPEED);
            velocity.y       = new ParticleSystem.MinMaxCurve(RISE_SPEED * HALF, RISE_SPEED);
            velocity.z       = new ParticleSystem.MinMaxCurve(-SPREAD_SPEED, SPREAD_SPEED);

            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled  = true;
            noise.strength = NOISE_STRENGTH;

            ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
            size.enabled = true;
            size.size    = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

            ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
            fade.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, FADE_KEEP), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            if (s_particleMaterial == null)
            {
                s_particleMaterial = new Material(Shader.Find(PARTICLE_SHADER));
            }

            ParticleSystemRenderer particleRenderer = go.GetComponent<ParticleSystemRenderer>();
            particleRenderer.sharedMaterial = s_particleMaterial;
            particleRenderer.renderMode     = ParticleSystemRenderMode.Billboard;

            system.Play();
        }
    }
}
