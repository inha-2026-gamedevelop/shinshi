// Unity
using UnityEngine;
using UnityEngine.Rendering;

// 서드파티
using NUnit.Framework;

namespace Shinshi.Tests
{
    /// <summary> 공용 파티클 셰이더의 툰 선택 기능과 무철 바위 셰이더의 DepthNormals 패스가 있는지 검증한다 </summary>
    public class VfxToonShaderTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string PARTICLE_SHADER    = "Shinshi/VFX/Particle";
        private const string BATCH_ROCK_SHADER  = "Shinshi/VFX/MucheolBatchRock";
        private const string TOON_KEYWORD       = "_TOON_VFX";
        private const string TOON_TOGGLE        = "_ToonVfx";
        private const string LIGHT_MODE_TAG     = "LightMode";
        private const string DEPTH_NORMALS_MODE = "DepthNormals";

        private static readonly string[] G_ToonProperties =
        {
            TOON_TOGGLE,
            "_ToonAlphaSteps",
            "_ToonAlphaSoft",
            "_ToonCoreThreshold",
            "_ToonCoreColor",
            "_ToonEdgeWidth",
            "_ToonEdgeColor",
        };

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void ParticleShader_HasToonProperties()
        {
            Shader shader = FindRequiredShader(PARTICLE_SHADER);

            for (int i = 0; i < G_ToonProperties.Length; i++)
            {
                Assert.GreaterOrEqual(shader.FindPropertyIndex(G_ToonProperties[i]), 0, G_ToonProperties[i]);
            }
        }

        [Test]
        public void ParticleShader_DeclaresToonKeyword()
        {
            Shader shader = FindRequiredShader(PARTICLE_SHADER);

            LocalKeyword keyword = shader.keywordSpace.FindKeyword(TOON_KEYWORD);

            Assert.IsTrue(keyword.isValid);
        }

        // 기존 재질이 툰을 켜지 않은 상태 그대로 보이도록 새 재질의 기본값이 꺼짐이어야 한다
        [Test]
        public void ParticleShader_NewMaterial_ToonIsOff()
        {
            Shader   shader   = FindRequiredShader(PARTICLE_SHADER);
            Material material = new Material(shader);

            try
            {
                Assert.AreEqual(0f, material.GetFloat(TOON_TOGGLE));
                Assert.IsFalse(material.IsKeywordEnabled(TOON_KEYWORD));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void BatchRockShader_HasDepthNormalsPass()
        {
            Shader      shader    = FindRequiredShader(BATCH_ROCK_SHADER);
            ShaderTagId lightMode = new ShaderTagId(LIGHT_MODE_TAG);

            bool found = false;
            for (int i = 0; i < shader.passCount; i++)
            {
                if (shader.FindPassTagValue(i, lightMode).name == DEPTH_NORMALS_MODE)
                {
                    found = true;
                    break;
                }
            }

            Assert.IsTrue(found);
        }

        /****************************************
        *               Helpers
        ****************************************/

        private static Shader FindRequiredShader(string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            Assert.IsNotNull(shader, shaderName);
            return shader;
        }
    }
}
