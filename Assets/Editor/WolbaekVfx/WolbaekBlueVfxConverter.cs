// System
using System.Collections.Generic;
using System.IO;
using System.Text;

// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Shinshi.Editor
{
    // 변환 규칙(월백 등장 마법진 때 정한 규칙): 채도가 최소 채도 미만인 색(흰색, 검정)은 그대로 두고, 나머지 색은 색상 H를 기준 색상 + 비율 x 원래 H로 바꾼다
    // 채도, 밝기, 알파는 유지한다. 대상은 파티클 시작 색, 시간별 색, 속도별 색, 궤적 색, 렌더러 머티리얼의 모든 Color 속성, 조명 색이다
    // 원본 프리팹과 머티리얼은 고치지 않고 파란색 사본을 따로 만든다. 텍스처 안에 구워진 색은 바꾸지 않는다
    /// <summary> Spirit Bloom 시리즈 프리팹의 색을 월백 파란색 계열로 바꾼 사본을 만드는 도구 </summary>
    public static class WolbaekBlueVfxConverter
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string SOURCE_FOLDER   = "Assets/99.ThirdParty/VFX/Piloto Studio/Super Realistic FXs/Super Realistic FX Bundle 02/ARPG Spirit Bloom Spells";
        private const string OUTPUT_ROOT     = "Assets/99.ThirdParty/VFX/Wolbaek";
        private const string PREFAB_FOLDER   = "Blue";
        private const string MATERIAL_FOLDER = "Materials";
        private const string BLUE_SUFFIX     = "_Blue";

        private const float MIN_SATURATION = 0.08f;
        private const float BASE_HUE       = 0.58f;
        private const float HUE_RATIO      = 0.06f;

        /****************************************
        *                Methods
        ****************************************/

        [MenuItem("Shinshi/VFX/Wolbaek/Convert Spirit Bloom Prefabs To Blue")]
        public static void ConvertMenu()
        {
            Debug.Log(ConvertAll());
        }

        /// <summary> 시리즈 전체를 변환하고 결과 요약을 돌려준다 </summary>
        public static string ConvertAll()
        {
            EnsureFolder(OUTPUT_ROOT, PREFAB_FOLDER);
            EnsureFolder(OUTPUT_ROOT, MATERIAL_FOLDER);

            Dictionary<string, Material> materialCache = new Dictionary<string, Material>();
            StringBuilder report = new StringBuilder();
            int converted = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { SOURCE_FOLDER }))
            {
                string sourcePath = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(sourcePath);

                if (name.EndsWith(BLUE_SUFFIX))
                {
                    continue;
                }

                string outputPath = OUTPUT_ROOT + "/" + PREFAB_FOLDER + "/" + name + BLUE_SUFFIX + ".prefab";
                ConvertPrefab(sourcePath, outputPath, materialCache);
                report.AppendLine(outputPath);
                ++converted;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return "파란색 프리팹 " + converted + "개, 머티리얼 사본 " + materialCache.Count + "개\n" + report;
        }

        private static void ConvertPrefab(string sourcePath, string outputPath, Dictionary<string, Material> materialCache)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(sourcePath);

            foreach (ParticleSystem system in contents.GetComponentsInChildren<ParticleSystem>(true))
            {
                ConvertParticleColors(system);
            }

            foreach (Renderer renderer in contents.GetComponentsInChildren<Renderer>(true))
            {
                ConvertMaterials(renderer, materialCache);
            }

            foreach (Light light in contents.GetComponentsInChildren<Light>(true))
            {
                light.color = ToBlue(light.color);
            }

            PrefabUtility.SaveAsPrefabAsset(contents, outputPath);
            PrefabUtility.UnloadPrefabContents(contents);
        }

        private static void ConvertParticleColors(ParticleSystem system)
        {
            ParticleSystem.MainModule main = system.main;
            main.startColor = ToBlue(main.startColor);

            ParticleSystem.ColorOverLifetimeModule lifetime = system.colorOverLifetime;

            if (lifetime.enabled)
            {
                lifetime.color = ToBlue(lifetime.color);
            }

            ParticleSystem.ColorBySpeedModule speed = system.colorBySpeed;

            if (speed.enabled)
            {
                speed.color = ToBlue(speed.color);
            }

            ParticleSystem.TrailModule trails = system.trails;

            if (trails.enabled)
            {
                trails.colorOverLifetime = ToBlue(trails.colorOverLifetime);
                trails.colorOverTrail = ToBlue(trails.colorOverTrail);
            }
        }

        private static void ConvertMaterials(Renderer renderer, Dictionary<string, Material> materialCache)
        {
            Material[] materials = renderer.sharedMaterials;

            for (int i = 0; i < materials.Length; ++i)
            {
                materials[i] = GetBlueMaterial(materials[i], materialCache);
            }

            renderer.sharedMaterials = materials;

            ParticleSystemRenderer particleRenderer = renderer as ParticleSystemRenderer;

            if (particleRenderer != null)
            {
                particleRenderer.trailMaterial = GetBlueMaterial(particleRenderer.trailMaterial, materialCache);
            }
        }

        private static Material GetBlueMaterial(Material source, Dictionary<string, Material> materialCache)
        {
            if (source == null)
            {
                return null;
            }

            string sourcePath = AssetDatabase.GetAssetPath(source);

            if (string.IsNullOrEmpty(sourcePath) || source.name.EndsWith(BLUE_SUFFIX))
            {
                return source;
            }

            if (materialCache.TryGetValue(sourcePath, out Material cached))
            {
                return cached;
            }

            string outputPath = OUTPUT_ROOT + "/" + MATERIAL_FOLDER + "/" + source.name + BLUE_SUFFIX + ".mat";
            Material blue = AssetDatabase.LoadAssetAtPath<Material>(outputPath);

            if (blue == null)
            {
                blue = new Material(source);
                blue.name = source.name + BLUE_SUFFIX;
                ConvertMaterialColors(blue);
                AssetDatabase.CreateAsset(blue, outputPath);
            }

            materialCache[sourcePath] = blue;
            return blue;
        }

        private static void ConvertMaterialColors(Material material)
        {
            Shader shader = material.shader;

            for (int i = 0; i < shader.GetPropertyCount(); ++i)
            {
                if (shader.GetPropertyType(i) == ShaderPropertyType.Color)
                {
                    string property = shader.GetPropertyName(i);
                    material.SetColor(property, ToBlue(material.GetColor(property)));
                }
            }
        }

        private static ParticleSystem.MinMaxGradient ToBlue(ParticleSystem.MinMaxGradient source)
        {
            switch (source.mode)
            {
                case ParticleSystemGradientMode.Color:
                    return new ParticleSystem.MinMaxGradient(ToBlue(source.color));

                case ParticleSystemGradientMode.TwoColors:
                    return new ParticleSystem.MinMaxGradient(ToBlue(source.colorMin), ToBlue(source.colorMax));

                case ParticleSystemGradientMode.Gradient:
                    return new ParticleSystem.MinMaxGradient(ToBlue(source.gradient));

                case ParticleSystemGradientMode.TwoGradients:
                    return new ParticleSystem.MinMaxGradient(ToBlue(source.gradientMin), ToBlue(source.gradientMax));

                case ParticleSystemGradientMode.RandomColor:
                    ParticleSystem.MinMaxGradient random = new ParticleSystem.MinMaxGradient(ToBlue(source.gradient));
                    random.mode = ParticleSystemGradientMode.RandomColor;
                    return random;

                default:
                    return source;
            }
        }

        private static Gradient ToBlue(Gradient source)
        {
            GradientColorKey[] keys = source.colorKeys;

            for (int i = 0; i < keys.Length; ++i)
            {
                keys[i].color = ToBlue(keys[i].color);
            }

            Gradient result = new Gradient();
            result.mode = source.mode;
            result.SetKeys(keys, source.alphaKeys);
            return result;
        }

        private static Color ToBlue(Color source)
        {
            Color.RGBToHSV(source, out float hue, out float saturation, out float value);

            if (saturation < MIN_SATURATION)
            {
                return source;
            }

            Color result = Color.HSVToRGB(BASE_HUE + (HUE_RATIO * hue), saturation, value, true);
            result.a = source.a;
            return result;
        }

        private static void EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + child))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }
    }
}
