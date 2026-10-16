// Unity
using UnityEngine;

namespace Shinshi.Dev
{
    /// <summary> 사체가 재처럼 타들어 가며 사라지게 하는 연출. 재질을 타는 셰이더로 바꾸고, 몸 표면에서 재 입자를 날린다 </summary>
    public sealed class HitAshDissolve : MonoBehaviour
    {
        // 이 클라이언트 화면에만 보이는 표현이다. 시작하면 seconds 동안 진행되고, 끝나면 소유자를 지우지 않고 스스로만 정리한다.
        // 원래 재질의 _BaseMap을 그대로 이어받아 모델 색을 유지한다. 시험용이라 셰이더와 입자 재질은 이름으로 한 번만 찾는다

        /****************************************
        *                Fields
        ****************************************/

        private const string SHADER_NAME   = "Shinshi/Dev/AshDissolve";
        private const string PARTICLE_SHADER = "Sprites/Default";
        private const string BASE_MAP      = "_BaseMap";
        private const string DISSOLVE      = "_Dissolve";

        private const float DISSOLVE_START = -0.4f; // 아직 아무것도 타지 않았고 재 띠도 닿지 않은 값
        private const float DISSOLVE_END   = 1.05f; // 잡음의 최댓값까지 모두 사라진 값

        private const int   MAX_PARTICLES  = 300;
        private const float EMIT_RATE      = 140f;
        private const float LIFETIME_MIN   = 0.7f;
        private const float LIFETIME_MAX   = 1.5f;
        private const float SIZE_MIN       = 0.03f; // m
        private const float SIZE_MAX       = 0.09f; // m
        private const float RISE_SPEED     = 0.9f;  // m/s
        private const float SPREAD_SPEED   = 0.5f;  // m/s
        private const float NOISE_STRENGTH = 0.6f;
        private const float GRAVITY        = -0.05f;

        // 불씨의 주황빛은 쓰지 않는다. 사라지기 직전 몸 가장자리와 입자가 적갈색으로 물들어 보여, 재의 어두운 색만 남긴다
        private const string EMBER_COLOR = "_EmberColor";

        private static readonly Color s_ashColor      = new Color(0.12f, 0.11f, 0.1f, 1f);
        private static readonly Color s_ashLightColor = new Color(0.3f, 0.28f, 0.26f, 1f);

        private Material[] _materials = null;
        private float      _seconds   = 1f;
        private float      _elapsed   = 0f;

        /****************************************
        *              Unity Event
        ****************************************/

        private void Update()
        {
            _elapsed += Time.deltaTime;

            float progress = Mathf.Clamp01(_elapsed / _seconds);
            float value    = Mathf.Lerp(DISSOLVE_START, DISSOLVE_END, progress);
            foreach (Material material in _materials)
            {
                material.SetFloat(DISSOLVE, value);
            }
        }

        private void OnDestroy()
        {
            if (_materials == null)
            {
                return;
            }

            foreach (Material material in _materials)
            {
                Destroy(material);
            }
        }

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> owner의 모든 렌더러를 타는 재질로 바꾸고 seconds 동안 재가 되어 사라지게 한다 </summary>
        public static void Begin(GameObject owner, float seconds)
        {
            Shader shader = Shader.Find(SHADER_NAME);
            if (shader == null)
            {
                Debug.LogError($"[HitAshDissolve] 셰이더를 찾지 못했다: {SHADER_NAME}", owner);
                return;
            }

            var ash = owner.AddComponent<HitAshDissolve>();
            ash._seconds = seconds;

            var materials = new System.Collections.Generic.List<Material>();
            foreach (Renderer renderer in owner.GetComponentsInChildren<Renderer>())
            {
                if (renderer is ParticleSystemRenderer)
                {
                    continue;
                }

                Material[] originals = renderer.sharedMaterials;
                var        burning   = new Material[originals.Length];
                for (int i = 0; i < originals.Length; ++i)
                {
                    burning[i] = new Material(shader);
                    if ((originals[i] != null) && (originals[i].HasProperty(BASE_MAP)))
                    {
                        burning[i].SetTexture(BASE_MAP, originals[i].GetTexture(BASE_MAP));
                    }

                    burning[i].SetFloat(DISSOLVE, DISSOLVE_START);
                    burning[i].SetColor(EMBER_COLOR, Color.black);
                    materials.Add(burning[i]);
                }

                renderer.sharedMaterials = burning;

                if (renderer is SkinnedMeshRenderer skinned)
                {
                    ash.CreateAshParticles(owner.transform, skinned);
                }
            }

            ash._materials = materials.ToArray();
        }

        // 몸 표면(스킨 메시)에서 재를 위로 날린다. 입자는 월드에 남아 몸이 사라진 뒤에도 잠시 떠돈다
        private void CreateAshParticles(Transform parent, SkinnedMeshRenderer skinned)
        {
            var go = new GameObject("DevAshParticles");
            go.transform.SetParent(parent, false);

            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = system.main;
            main.loop            = false;
            main.duration        = _seconds;
            main.playOnAwake     = false;
            main.maxParticles    = MAX_PARTICLES;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = GRAVITY;
            main.startLifetime   = new ParticleSystem.MinMaxCurve(LIFETIME_MIN, LIFETIME_MAX);
            main.startSize       = new ParticleSystem.MinMaxCurve(SIZE_MIN, SIZE_MAX);
            main.startColor      = new ParticleSystem.MinMaxGradient(s_ashColor, s_ashLightColor);

            var emission = system.emission;
            emission.rateOverTime = EMIT_RATE;

            var shape = system.shape;
            shape.enabled              = true;
            shape.shapeType            = ParticleSystemShapeType.SkinnedMeshRenderer;
            shape.skinnedMeshRenderer  = skinned;

            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space   = ParticleSystemSimulationSpace.World;
            velocity.x       = new ParticleSystem.MinMaxCurve(-SPREAD_SPEED, SPREAD_SPEED);
            velocity.y       = new ParticleSystem.MinMaxCurve(RISE_SPEED * 0.5f, RISE_SPEED);
            velocity.z       = new ParticleSystem.MinMaxCurve(-SPREAD_SPEED, SPREAD_SPEED);

            var noise = system.noise;
            noise.enabled  = true;
            noise.strength = NOISE_STRENGTH;

            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size    = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

            var fade = system.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            var particleRenderer = go.GetComponent<ParticleSystemRenderer>();
            particleRenderer.sharedMaterial = new Material(Shader.Find(PARTICLE_SHADER));
            particleRenderer.renderMode     = ParticleSystemRenderMode.Billboard;

            system.Play();
        }
    }
}
