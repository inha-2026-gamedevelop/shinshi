// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Shinshi.Editor
{
    /// <summary> 각 타격의 실제 범위 안에 고정 시드의 먼지와 스파크를 배치한다 </summary>
    public static class MucheolBatchParticleBuilder
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string PARTICLE_SHADER = "Shinshi/VFX/MucheolImpactParticle";
        private const int SPARK_COUNT = 40;
        private const int DUST_COUNT = 12;
        private const float PARTICLE_DURATION = 1.3f;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 기존 타격 재생기에 연결할 먼지와 스파크를 만든다 </summary>
        public static ParticleSystem[] Build(Transform parent, string assetFolder, int number, Vector3[] authoredPositions)
        {
            return new[]
            {
                Create(parent, assetFolder, number, false, authoredPositions),
                Create(parent, assetFolder, number, true, authoredPositions)
            };
        }

        private static ParticleSystem Create(Transform parent, string folder, int number, bool dust, Vector3[] authoredPositions)
        {
            string name = dust ? "Dust" : "Sparks";
            int count = dust ? DUST_COUNT : SPARK_COUNT;
            bool line = (number == 3) || (number == 6);
            float maxSize = dust ? (line ? 0.45f : 1f) : 0.085f;
            float growth = dust ? 2f : 1f;
            float boundRadius = maxSize * growth / Mathf.Sqrt(2f);
            string meshPath = folder + "/Meshes/SM_" + name + "Spawn.asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            bool createMesh = mesh == null;
            if (createMesh)
            {
                mesh = new Mesh();
            }

            Vector3[] positions = MucheolBatchVfxBuilder.BuildParticlePositions(number, count, boundRadius);
            if ((authoredPositions != null) && (authoredPositions.Length > 0))
            {
                int index = 0;
                for (int i = 0; (i < authoredPositions.Length) && (index < positions.Length); ++i)
                {
                    if (MucheolBatchVfxBuilder.ContainsParticleBounds(number, authoredPositions[i], boundRadius))
                    {
                        positions[index] = authoredPositions[i];
                        ++index;
                    }
                }
            }
            Vector3[] normals = new Vector3[count];
            for (int i = 0; i < normals.Length; ++i)
            {
                normals[i] = Vector3.up;
            }

            mesh.Clear();
            mesh.name = $"SM_Mucheol_{number:00}_{name}Spawn";
            mesh.vertices = positions;
            mesh.normals = normals;
            mesh.RecalculateBounds();
            if (createMesh)
            {
                AssetDatabase.CreateAsset(mesh, meshPath);
            }
            else
            {
                EditorUtility.SetDirty(mesh);
            }

            string materialPath = folder + "/Materials/M_" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                Shader shader = Shader.Find(PARTICLE_SHADER);
                if (shader == null)
                {
                    throw new System.InvalidOperationException("무철 파티클 셰이더가 없다");
                }

                material = new Material(shader) { name = $"M_Mucheol_{number:00}_{name}" };
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
            main.duration = PARTICLE_DURATION;
            main.startLifetime = new ParticleSystem.MinMaxCurve(dust ? 0.8f : 0.45f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(dust ? 0.15f : 1.5f, dust ? 0.45f : 3.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(dust ? maxSize * 0.55f : 0.035f, maxSize);
            main.startColor = dust ? new Color(0.32f, 0.25f, 0.16f, 0.5f) : new Color(3f, 2.1f, 0.8f, 1f);
            main.gravityModifier = dust ? 0f : 0.45f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = count;
            ParticleSystem.EmissionModule emission = particle.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            ParticleSystem.ShapeModule shape = particle.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Mesh;
            shape.mesh = mesh;
            shape.meshShapeType = ParticleSystemMeshShapeType.Vertex;
            shape.randomDirectionAmount = 0f;
            shape.sphericalDirectionAmount = 0f;
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
