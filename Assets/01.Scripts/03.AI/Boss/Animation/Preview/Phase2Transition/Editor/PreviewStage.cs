// Unity
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Shinshi.Editor
{
    // 미리보기 씬(NewPreviewScene/OpenPreviewScene)은 열린 편집 씬과 분리되어 있어 카메라의 scene을 지정해야 그 씬만 그린다
    /// <summary> 월백 전환 시안 조사와 캡처에서 쓰는 미리보기 씬 무대, 카메라, 렌더링 도우미 </summary>
    public static class PreviewStage
    {
        /****************************************
        *              Constants
        ****************************************/

        private const float NEAR_CLIP = 0.05f;
        private const float FAR_CLIP  = 200f;

        private const string URP_SUFFIX = "_URP.mat";

        /****************************************
        *                Methods
        ****************************************/

        public static Camera CreateStage(Scene scene, float lightIntensity, float groundScale, Color background, Vector3 lightEuler)
        {
            GameObject lightObject = new GameObject("Survey Light");
            SceneManager.MoveGameObjectToScene(lightObject, scene);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = lightIntensity;
            light.shadows = LightShadows.Soft;
            lightObject.transform.rotation = Quaternion.Euler(lightEuler);

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            SceneManager.MoveGameObjectToScene(ground, scene);
            ground.name = "Survey Ground";
            ground.transform.localScale = Vector3.one * groundScale;

            GameObject cameraObject = new GameObject("Survey Camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = background;
            camera.nearClipPlane = NEAR_CLIP;
            camera.farClipPlane = FAR_CLIP;
            camera.scene = scene;
            camera.enabled = false;
            return camera;
        }

        public static void Aim(Camera camera, Vector3 position, Vector3 lookAt, float fov)
        {
            camera.transform.position = position;
            camera.transform.rotation = Quaternion.LookRotation(lookAt - position);
            camera.fieldOfView = fov;
        }

        /// <summary> 정식 프리팹이 아닌 모델 프리팹을 같은 아바타로 놓는다. 컨트롤러는 비워 Timeline이나 그래프가 직접 구동한다 </summary>
        public static Animator SpawnWolbaek(Scene scene, Vector3 position, Quaternion rotation)
        {
            GameObject prefab = WolbaekPhase2TransitionAssets.LoadByGuid<GameObject>(WolbaekPhase2TransitionAssets.MODEL_PREFAB_GUID);
            Avatar avatar = WolbaekPhase2TransitionAssets.LoadByGuid<Avatar>(WolbaekPhase2TransitionAssets.AVATAR_GUID);
            if ((prefab == null) || (avatar == null))
            {
                return null;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.transform.SetPositionAndRotation(position, rotation);
            Animator animator = instance.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                animator = instance.AddComponent<Animator>();
            }

            animator.avatar = avatar;
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            UseUrpMaterials(instance);
            DisableCloth(instance);
            return animator;
        }

        // 기모노와 소매의 Cloth는 편집 모드에서 시뮬레이션되지 않아 몸이 숙여도 처음 자리에 남는다
        // 시안은 편집 모드 Timeline 미리보기와 캡처가 같아야 하므로 이 인스턴스의 Cloth만 끄고 뼈 스키닝으로 따라가게 한다
        /// <summary> 인스턴스의 Cloth 컴포넌트를 끄고 끈 수를 돌려준다 </summary>
        public static int DisableCloth(GameObject instance)
        {
            Cloth[] cloths = instance.GetComponentsInChildren<Cloth>(true);
            for (int i = 0; i < cloths.Length; ++i)
            {
                cloths[i].enabled = false;
            }

            return cloths.Length;
        }

        // 원본 모델 재질은 내장 렌더러용 Standard Double Sided라 URP에서 분홍색으로 보인다
        // 등장 시험 씬과 같은 기웅 소유 URP 사본(MAT_YUNA_*_URP)으로 이 인스턴스의 재질만 바꾼다. 원본 프리팹과 재질은 그대로 둔다
        /// <summary> 인스턴스 렌더러의 재질을 같은 이름의 URP 사본으로 바꾸고 바꾼 수를 돌려준다 </summary>
        public static int UseUrpMaterials(GameObject instance)
        {
            int replaced = 0;
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; ++r)
            {
                Material[] materials = renderers[r].sharedMaterials;
                bool isChanged = false;
                for (int m = 0; m < materials.Length; ++m)
                {
                    if (materials[m] == null)
                    {
                        continue;
                    }

                    string path = WolbaekPhase2TransitionAssets.URP_MATERIAL_DIR + materials[m].name + URP_SUFFIX;
                    Material urp = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (urp != null)
                    {
                        materials[m] = urp;
                        isChanged = true;
                        ++replaced;
                    }
                }

                if (isChanged)
                {
                    renderers[r].sharedMaterials = materials;
                }
            }

            return replaced;
        }

        // 미리보기 씬에서 루트 뼈를 기준으로 하는 스킨드 메시(다리, 신발, 꼬리, 아래 기모노)가 첫 자세에 남는 현상이 있었다
        // 캡처 전용으로 매 렌더 행렬 재계산과 화면 밖 갱신을 켠다. 저장하는 씬에는 쓰지 않는다
        /// <summary> 캡처용 인스턴스의 스킨드 메시가 렌더마다 다시 스키닝되게 한다 </summary>
        public static void ForceSkinning(GameObject instance)
        {
            SkinnedMeshRenderer[] skins = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < skins.Length; ++i)
            {
                skins[i].updateWhenOffscreen = true;
                skins[i].forceMatrixRecalculationPerRender = true;
            }
        }

        public static void RenderInto(Camera camera, Texture2D target, int x, int y, int width, int height, int depthBits)
        {
            RenderTexture texture = RenderTexture.GetTemporary(width, height, depthBits, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.aspect = (float)width / height;
                camera.targetTexture = texture;
                camera.Render();
                RenderTexture.active = texture;
                target.ReadPixels(new Rect(0, 0, width, height), x, y);
                target.Apply(false);
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(texture);
            }
        }
    }
}
