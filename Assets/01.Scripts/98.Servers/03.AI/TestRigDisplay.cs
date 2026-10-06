// Unity
using UnityEngine;

namespace Shinshi.AI
{
    // 보스, 적 시험 장치가 함께 쓰는 화면 설정이다.
    // URP 빌드에서 CreatePrimitive는 내장 Standard 재질을 붙여 분홍색으로 그려지므로 빌드에 이미 들어가는 URP Lit 셰이더로 바꾼다.
    // 시험 장치 GUI는 왼쪽 위에 그리는데 그 자리에 FPS 표시(NetworkOverlayView)가 있어 그만큼 내려 그린다
    /// <summary> 시험 장치의 기본 도형 생성과 GUI 시작 위치 </summary>
    public static class TestRigDisplay
    {
        /****************************************
        *                Fields
        ****************************************/

        public const float GUI_TOP = 40f;

        private const string LIT_SHADER_NAME = "Universal Render Pipeline/Lit";

        private static Material G_LitMaterial = null;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> URP 재질을 붙인 기본 도형을 만든다, 셰이더를 못 찾으면 Unity 기본 재질을 그대로 둔다 </summary>
        public static GameObject CreatePrimitive(PrimitiveType type)
        {
            GameObject primitive = GameObject.CreatePrimitive(type);

            if (G_LitMaterial == null)
            {
                Shader lit = Shader.Find(LIT_SHADER_NAME);
                if (lit != null)
                {
                    G_LitMaterial = new Material(lit);
                }
            }

            if (G_LitMaterial != null)
            {
                primitive.GetComponent<Renderer>().sharedMaterial = G_LitMaterial;
            }

            return primitive;
        }
    }
}
