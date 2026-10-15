// System
using System;

// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Shinshi.Editor
{
    /// <summary> 누락된 외부 효과 대신 프로젝트 소유의 검 궤적과 화살 입자를 만든다 </summary>
    public static class ReleaseVfxBuilder
    {
        private const string ROOT = "Assets/02.Prefab/VFX/Archer/Materials/";
        private static readonly string[] G_Swords = { "Basic", "Wide", "Dash", "Focus", "ChainFirst", "ChainSecond" };
        private static readonly string[] G_Arrows = { "Arrow", "Muzzle", "Wind", "WindMuzzle", "Impact" };

        public static void Build()
        {
            Shader shader = Shader.Find("Shinshi/VFX/ReleaseParticle");
            if (shader == null)
            {
                throw new InvalidOperationException("출시 파티클 셰이더가 없습니다.");
            }
            Material material = AssetDatabase.LoadAssetAtPath<Material>(ROOT + "M_ArcherArrow.mat");
            material.shader = shader;
            material.shaderKeywords = Array.Empty<string>();
            material.SetColor("_Tint", Color.white);
            Texture2D white = null;
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(ROOT + "M_ArcherArrow.mat"))
            {
                if (asset is Texture2D texture && texture.name == "ReleaseWhite")
                {
                    white = texture;
                }
            }
            if (white == null)
            {
                white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                white.name = "ReleaseWhite";
                white.SetPixel(0, 0, Color.white);
                white.Apply();
                AssetDatabase.AddObjectToAsset(white, material);
            }
            material.SetTexture("_BaseMap", white);
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            Material glow = AssetDatabase.LoadAssetAtPath<Material>(ROOT + "M_ReleaseGlow.mat");
            if (glow == null)
            {
                glow = new Material(shader);
                AssetDatabase.CreateAsset(glow, ROOT + "M_ReleaseGlow.mat");
                var mask = new Texture2D(32, 32, TextureFormat.RGBA32, false);
                mask.name = "RadialMask";
                mask.wrapMode = TextureWrapMode.Clamp;
                for (int y = 0; y < 32; ++y)
                {
                    for (int x = 0; x < 32; ++x)
                    {
                        float radius = new Vector2((x - 15.5f) / 15.5f, (y - 15.5f) / 15.5f).magnitude;
                        mask.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - radius), 2f)));
                    }
                }
                mask.Apply();
                AssetDatabase.AddObjectToAsset(mask, glow);
                glow.SetTexture("_BaseMap", mask);
            }
            glow.SetColor("_Tint", Color.white);
            EditorUtility.SetDirty(glow);
            AssetDatabase.SaveAssetIfDirty(glow);

            Mesh arc = SaveMesh("M_ReleaseSlash", Arc());
            Mesh arrow = SaveMesh("M_ReleaseArrow", Arrow());
            for (int i = 0; i < G_Swords.Length; ++i)
            {
                string name = "PF_SwordsmanMagicSlash_" + G_Swords[i];
                BuildEffect("Assets/02.Prefab/VFX/SwordsmanMagicSlash/" + name + ".prefab", name,
                    material, arc, false, i == 1 ? 1.4f : 1f, new Color(0.55f, 0.8f, 1f, 0.65f));
            }
            for (int i = 0; i < G_Arrows.Length; ++i)
            {
                bool projectile = i == 0 || i == 2;
                string name = "PF_ArcherVfx_" + G_Arrows[i];
                BuildEffect("Assets/02.Prefab/VFX/Archer/" + name + ".prefab", name,
                    projectile ? material : glow, projectile ? arrow : null, projectile, 1f,
                    new Color(0.65f, 1f, 0.82f, 0.65f));
            }
            // 마검사 효과는 MageknightVfxBuilder가 기획에서 고른 외부 효과의 원본 재질로 만든다.
            // 여기서 공용 발광 재질로 바꾸거나 장판을 고리로 다시 만들면 고른 모양이 사라지므로 손대지 않는다
        }

        private static void BuildEffect(string path, string name, Material material, Mesh mesh, bool loop,
            float size, Color color, bool field = false)
        {
            var root = new GameObject(name);
            try
            {
                var child = new GameObject("Effect");
                child.transform.SetParent(root.transform, false);
                child.transform.localPosition = Vector3.up * (field ? 0.05f : 1f);
                ParticleSystem particle = child.AddComponent<ParticleSystem>();
                particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = particle.main;
                main.duration = 0.25f;
                main.loop = loop;
                main.playOnAwake = false;
                main.stopAction = ParticleSystemStopAction.None;
                main.startLifetime = loop ? 0.26f : 0.2f;
                main.startSpeed = mesh != null ? 0f : 1.5f;
                main.startSize = mesh != null ? size : 0.2f;
                main.startColor = color;
                main.maxParticles = mesh != null ? 1 : 12;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                var shape = particle.shape;
                shape.enabled = mesh == null;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.04f;
                var emission = particle.emission;
                emission.rateOverTime = field ? 12f : 0f;
                emission.SetBursts(field ? Array.Empty<ParticleSystem.Burst>()
                    : new[] { new ParticleSystem.Burst(0f, (short)(mesh == null ? 8 : 1)) });
                var fade = particle.colorOverLifetime;
                fade.enabled = !loop;
                var gradient = new Gradient();
                gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                fade.color = gradient;
                particle.useAutoRandomSeed = false;
                particle.randomSeed = 7;
                var renderer = child.GetComponent<ParticleSystemRenderer>();
                renderer.sharedMaterials = new[] { material };
                renderer.renderMode = mesh != null ? ParticleSystemRenderMode.Mesh : ParticleSystemRenderMode.Billboard;
                renderer.mesh = mesh;
                renderer.alignment = ParticleSystemRenderSpace.Local;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                if (loop && !field)
                {
                    TrailRenderer trail = child.AddComponent<TrailRenderer>();
                    trail.sharedMaterial = material;
                    trail.time = 0.12f;
                    trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
                    trail.widthMultiplier = 0.04f;
                    trail.startColor = color;
                    trail.endColor = new Color(color.r, color.g, color.b, 0f);
                    trail.emitting = false;
                    trail.shadowCastingMode = ShadowCastingMode.Off;
                    trail.receiveShadows = false;
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static Mesh SaveMesh(string name, Mesh value)
        {
            string path = ROOT + name + ".asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = value;
                AssetDatabase.CreateAsset(mesh, path);
            }
            else
            {
                EditorUtility.CopySerialized(value, mesh);
                UnityEngine.Object.DestroyImmediate(value);
            }
            mesh.name = name;
            EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssetIfDirty(mesh);
            return mesh;
        }

        private static Mesh Arc(float sweep = 140f)
        {
            const int segments = 24;
            var vertices = new Vector3[(segments + 1) * 2];
            var triangles = new int[segments * 6];
            for (int i = 0; i <= segments; ++i)
            {
                float angle = Mathf.Lerp(-sweep * 0.5f, sweep * 0.5f, (float)i / segments) * Mathf.Deg2Rad;
                Vector3 direction = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                vertices[i * 2] = direction * 1.1f;
                vertices[i * 2 + 1] = direction * 1.25f;
                if (i < segments)
                {
                    int t = i * 6;
                    triangles[t] = i * 2;
                    triangles[t + 1] = i * 2 + 1;
                    triangles[t + 2] = i * 2 + 2;
                    triangles[t + 3] = i * 2 + 1;
                    triangles[t + 4] = i * 2 + 3;
                    triangles[t + 5] = i * 2 + 2;
                }
            }
            var mesh = new Mesh { vertices = vertices, triangles = triangles };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh Arrow()
        {
            var mesh = new Mesh
            {
                vertices = new[] { new Vector3(-0.02f, 0f, -0.45f), new Vector3(0.02f, 0f, -0.45f),
                    new Vector3(-0.02f, 0f, 0.2f), new Vector3(0.02f, 0f, 0.2f), new Vector3(-0.13f, 0f, 0.1f),
                    new Vector3(0.13f, 0f, 0.1f), new Vector3(0f, 0f, 0.5f) },
                triangles = new[] { 0, 1, 2, 1, 3, 2, 4, 5, 6 }
            };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
