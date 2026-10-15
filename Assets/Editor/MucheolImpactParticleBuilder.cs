// System
using System;

// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Shinshi.Editor
{
    /// <summary> 승인된 무철 색감의 스파크와 먼지를 프리팹에 조립한다 </summary>
    public static class MucheolImpactParticleBuilder
    {
        private const string ROOT = "Assets/01.Scripts/05.Art/VFX/Mucheol/ImpactV4";
        private const int SPARK_COUNT = 40;
        private const int DUST_COUNT = 12;

        public static ParticleSystem[] Build(Transform parent)
        {
            return new[] { Create(parent, false), Create(parent, true) };
        }

        private static ParticleSystem Create(Transform parent, bool dust)
        {
            string name = dust ? "Dust" : "Sparks";
            string meshPath = ROOT + "/Meshes/SM_" + name + "Spawn.asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null)
            {
                int count = dust ? DUST_COUNT : SPARK_COUNT;
                Vector3[] points = new Vector3[count];
                Vector3[] normals = new Vector3[count];
                System.Random random = new System.Random(261014);
                for (int i = 0; i < count; ++i)
                {
                    float angle = Mathf.Lerp(-50f, 50f, (float)random.NextDouble()) * Mathf.Deg2Rad;
                    float radius = Mathf.Lerp(0.8f, 3.8f, (float)Math.Sqrt(random.NextDouble()));
                    points[i] = new Vector3(Mathf.Sin(angle) * radius, 0.08f, Mathf.Cos(angle) * radius);
                    normals[i] = Vector3.up;
                }
                mesh = new Mesh { name = name + "Spawn", vertices = points, normals = normals };
                mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, meshPath);
            }

            string materialPath = ROOT + "/Materials/M_" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Shinshi/VFX/MucheolImpactParticle"));
                material.SetFloat("_Softness", dust ? 1.5f : 2f);
                material.SetFloat("_Noise", dust ? 1f : 0f);
                AssetDatabase.CreateAsset(material, materialPath);
            }

            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            ParticleSystem particle = go.AddComponent<ParticleSystem>();
            particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particle.useAutoRandomSeed = false;
            particle.randomSeed = dust ? 57u : 41u;
            ParticleSystem.MainModule main = particle.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1.3f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(dust ? 0.8f : 0.45f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(dust ? 0.15f : 1.5f, dust ? 0.45f : 3.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(dust ? 0.55f : 0.035f, dust ? 1f : 0.085f);
            main.startColor = dust ? new Color(0.32f, 0.25f, 0.16f, 0.5f) : new Color(3f, 2.1f, 0.8f, 1f);
            main.gravityModifier = dust ? 0f : 0.45f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = dust ? DUST_COUNT : SPARK_COUNT;
            ParticleSystem.EmissionModule emission = particle.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(dust ? DUST_COUNT : SPARK_COUNT)) });
            ParticleSystem.ShapeModule shape = particle.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Mesh;
            shape.mesh = mesh;
            shape.meshShapeType = ParticleSystemMeshShapeType.Vertex;
            ParticleSystem.ColorOverLifetimeModule color = particle.colorOverLifetime;
            color.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
            ParticleSystem.SizeOverLifetimeModule size = particle.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, dust ? 0.4f : 1f, 1f, dust ? 2f : 0.2f));
            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return particle;
        }
    }
}
