// System
using System.Collections;
using System.Collections.Generic;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Art;

namespace Shinshi.Tests
{
    // 프리워밍 어댑터가 실제 재생 경로를 예외 없이 지나고 끝나면 화면을 지우는지 본다. 화면에 나온 모양은 확인하지 않는다
    public class BossPrewarmTargetPlayTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const string VFX_DIR       = "Assets/01.Scripts/03.AI/Boss/VFX/Generated/";
        private const string SECTOR_PREFAB = VFX_DIR + "PF_SectorGroundVFX.prefab";
        private const string BEAM_PREFAB   = VFX_DIR + "PF_BeamGroundVFX.prefab";
        private const string RECT_PREFAB   = VFX_DIR + "PF_RectTelegraphVFX.prefab";
        private const string AREA_PREFAB   = VFX_DIR + "PF_AreaGroundVFX.prefab";
        private const string BOSS_PREFAB   = "Assets/02.Prefab/Boss/PF_WolbaekBoss.prefab";
        private const string URP_SUFFIX    = "_URP";
        private const float  TICK_STEP     = 0.1f;
        private const int    TICK_COUNT    = 9;

        /****************************************
        *               Helpers
        ****************************************/

        private static GameObject LoadPrefab(string path)
        {
#if UNITY_EDITOR
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
#else
            GameObject prefab = null;
#endif
            Assert.IsNotNull(prefab, path);
            return prefab;
        }

        private static void RunTicksAndEnd(IPrewarmTarget target)
        {
            for (int i = 1; i <= TICK_COUNT; ++i)
            {
                target.TickPrewarm(i * TICK_STEP);
            }

            target.EndPrewarm();
        }

        private static void RunCycle(IPrewarmTarget target)
        {
            target.BeginPrewarm();
            RunTicksAndEnd(target);
        }

        private static int CountEnabledRenderers(GameObject root)
        {
            int count = 0;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
            {
                count += renderer.enabled ? 1 : 0;
            }

            return count;
        }

        /****************************************
        *                Tests
        ****************************************/

        [UnityTest]
        public IEnumerator SectorController_PrewarmCycle_HidesAfterEnd()
        {
            GameObject root = Object.Instantiate(LoadPrefab(SECTOR_PREFAB));
            SectorVFXController controller = root.GetComponent<SectorVFXController>();
            yield return null;

            controller.BeginPrewarm();
            Assert.IsTrue(controller.IsPlaying);
            Assert.IsTrue(root.GetComponent<MeshRenderer>().enabled);
            yield return new WaitForSeconds(0.5f);
            controller.TickPrewarm(0.5f);
            controller.EndPrewarm();

            Assert.IsFalse(controller.IsPlaying);
            Assert.IsFalse(root.GetComponent<MeshRenderer>().enabled);
            Object.Destroy(root);
        }

        [UnityTest]
        public IEnumerator BeamController_PrewarmCycle_HidesAfterEnd()
        {
            GameObject root = Object.Instantiate(LoadPrefab(BEAM_PREFAB));
            BeamGroundVFXController controller = root.GetComponent<BeamGroundVFXController>();
            yield return null;

            controller.BeginPrewarm();
            Assert.IsTrue(controller.IsShown);
            Assert.IsTrue(controller.HasArea);
            RunTicksAndEnd(controller);
            Assert.IsFalse(controller.IsShown);
            Assert.IsFalse(root.GetComponent<MeshRenderer>().enabled);
            Object.Destroy(root);
        }

        [UnityTest]
        public IEnumerator RectController_PrewarmCycle_HidesAfterEnd()
        {
            GameObject root = Object.Instantiate(LoadPrefab(RECT_PREFAB));
            RectTelegraphVFXController controller = root.GetComponent<RectTelegraphVFXController>();
            yield return null;

            controller.BeginPrewarm();
            Assert.IsTrue(controller.IsShown);
            RunTicksAndEnd(controller);
            Assert.IsFalse(controller.IsShown);
            Assert.IsFalse(root.GetComponent<MeshRenderer>().enabled);
            Object.Destroy(root);
        }

        [UnityTest]
        public IEnumerator AreaController_PrewarmCycle_HidesAfterEnd()
        {
            GameObject root = Object.Instantiate(LoadPrefab(AREA_PREFAB));
            AreaGroundVFXController controller = root.GetComponent<AreaGroundVFXController>();
            yield return null;

            controller.BeginPrewarm();
            Assert.IsTrue(controller.IsShown);
            RunTicksAndEnd(controller);
            Assert.IsFalse(controller.IsShown);
            Assert.IsFalse(root.GetComponent<MeshRenderer>().enabled);
            Object.Destroy(root);
        }

        [UnityTest]
        public IEnumerator GroundShapeAdapter_PrewarmCycle_LeavesNothingVisible()
        {
            GameObject host = new GameObject("ShapeHost", typeof(BossGroundShapePrewarmTarget));
            BossGroundShapePrewarmTarget target = host.GetComponent<BossGroundShapePrewarmTarget>();
            yield return null;

            target.BeginPrewarm();
            GameObject poolRoot = GameObject.Find("Boss Ground Shapes");
            Assert.IsNotNull(poolRoot);
            Assert.Greater(CountEnabledRenderers(poolRoot), 0);
            target.TickPrewarm(0.2f);
            target.EndPrewarm();

            yield return null;
            Assert.IsTrue(poolRoot == null);
            Object.Destroy(host);
        }

        [UnityTest]
        public IEnumerator StarAdapter_PrewarmCycle_DestroysController()
        {
            GameObject host = new GameObject("StarHost", typeof(BossStarPrewarmTarget));
            BossStarPrewarmTarget target = host.GetComponent<BossStarPrewarmTarget>();
            yield return null;

            RunCycle(target);
            yield return null;
            Assert.AreEqual(0, host.transform.childCount);
            Object.Destroy(host);
        }

        [UnityTest]
        public IEnumerator OrbAdapter_WithoutMaterials_DoesNotThrow()
        {
            GameObject host = new GameObject("OrbHost", typeof(BossOrbPrewarmTarget));
            BossOrbPrewarmTarget target = host.GetComponent<BossOrbPrewarmTarget>();
            yield return null;

            LogAssert.Expect(LogType.Warning, "[Prewarm] 여우불 재질이 비어 있다");
            RunCycle(target);
            Object.Destroy(host);
        }

        [UnityTest]
        public IEnumerator BossView_Destroy_DestroysRuntimeMaterials()
        {
            GameObject root = Object.Instantiate(LoadPrefab(BOSS_PREFAB));
            yield return null;

            var created = new List<Material>();
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if ((material != null) && material.name.EndsWith(URP_SUFFIX) && (!created.Contains(material)))
                    {
                        created.Add(material);
                    }
                }
            }

            Assert.Greater(created.Count, 0, "BossView가 만든 URP 재질이 없다");

            Object.Destroy(root);
            yield return null;

            for (int i = 0; i < created.Count; ++i)
            {
                Assert.IsTrue(created[i] == null, "BossView가 만든 재질이 파괴되지 않았다");
            }
        }
    }
}
