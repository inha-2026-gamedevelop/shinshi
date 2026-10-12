// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;
using TMPro;

// 프로젝트 내부
using Shinshi.Combat;

namespace Shinshi.Tests
{
    public sealed class DamageNumberPoolTests
    {
        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void Show_CriticalAndNormal_UsesServerAmountAndExistingHudFont()
        {
            var cameraRoot = new GameObject("DamageNumberCamera", typeof(Camera));
            var root = new GameObject("DamageNumberPool", typeof(RectTransform));
            try
            {
                Camera camera = cameraRoot.GetComponent<Camera>();
                camera.transform.position = new Vector3(0f, 1.5f, -10f);
                DamageNumberPool pool = root.AddComponent<DamageNumberPool>();
                pool.Initialize(camera);
                Canvas.ForceUpdateCanvases();
                var critical = new DamageFeedback(1, 1, 2, Vector3.zero, 12.6f, true, 1.0);
                var normal = new DamageFeedback(1, 2, 2, Vector3.zero, 7.2f, false, 1.0);
                Assert.IsTrue(pool.Show(in critical));
                Assert.IsTrue(pool.Show(in normal));
                TMP_Text[] labels = root.GetComponentsInChildren<TMP_Text>();
                Assert.AreEqual(2, labels.Length);
                Assert.AreEqual("13!", labels[0].text);
                Assert.AreEqual("7", labels[1].text);
                Assert.Greater(labels[0].fontSize, labels[1].fontSize);
                Assert.AreNotEqual(labels[0].color, labels[1].color);
                GameObject hud = Resources.Load<GameObject>("PF_PlayerStatusHud");
                Assert.AreSame(hud.GetComponentInChildren<TMP_Text>(true).font, labels[0].font);
                Assert.AreEqual(12.6f, critical.Amount, "rounding only affects presentation");
                var huge = new DamageFeedback(1, 3, 2, Vector3.zero, float.MaxValue, true, 1.0);
                Assert.IsTrue(pool.Show(in huge), "finite maximum cannot overflow TMP decimal formatting");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(cameraRoot);
            }
        }

        [TestCase(0.01f, false, "1")]
        [TestCase(0.49f, false, "1")]
        [TestCase(0.01f, true, "1!")]
        [TestCase(0.49f, true, "1!")]
        public void Show_PositiveFraction_DisplaysAtLeastOneWithoutChangingServerAmount(float amount, bool critical, string expected)
        {
            var cameraRoot = new GameObject("FractionDamageCamera", typeof(Camera));
            var root = new GameObject("FractionDamagePool", typeof(RectTransform));
            try
            {
                Camera camera = cameraRoot.GetComponent<Camera>();
                camera.transform.position = new Vector3(0f, 1.5f, -10f);
                DamageNumberPool pool = root.AddComponent<DamageNumberPool>();
                pool.Initialize(camera);
                Canvas.ForceUpdateCanvases();
                var feedback = new DamageFeedback(1, 1, 2, Vector3.zero, amount, critical, 1.0);
                Assert.IsTrue(pool.Show(in feedback));
                Assert.AreEqual(expected, root.GetComponentInChildren<TMP_Text>().text);
                Assert.AreEqual(amount, feedback.Amount);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(cameraRoot);
            }
        }
    }
}
