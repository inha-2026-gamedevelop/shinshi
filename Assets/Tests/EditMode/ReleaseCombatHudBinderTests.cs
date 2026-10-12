// System
using System.Reflection;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;
using TMPro;

// 프로젝트 내부
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.UI;

namespace Shinshi.Tests
{
    public sealed class ReleaseCombatHudBinderTests
    {
        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void ApplyPotions_NoServerState_HidesPreviewCounts()
        {
            var root = new GameObject("test");
            try
            {
                ReleaseCombatHudBinder binder = root.AddComponent<ReleaseCombatHudBinder>();
                HudPotionView potion = Child(root, "potions").AddComponent<HudPotionView>();
                Set(binder, "_potionView", potion);
                Invoke(binder, "ApplyPotions", false, default(PotionStateMessage), 0d, 10f);
                Assert.That(potion.gameObject.activeSelf, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ApplyPotions_PendingServerState_UsesAvailableCountsWithoutLocalDecrement()
        {
            var root = new GameObject("test");
            try
            {
                ReleaseCombatHudBinder binder = root.AddComponent<ReleaseCombatHudBinder>();
                HudPotionView potion = Child(root, "potions").AddComponent<HudPotionView>();
                TMP_Text small = Label(potion.gameObject, "small");
                TMP_Text large = Label(potion.gameObject, "large");
                Set(potion, "_countLabels", new[] { small, large });
                Set(binder, "_potionView", potion);
                var state = new PotionStateMessage(1, ERejectReason.None, 4, 2, 1, 3, true, 15d);
                Invoke(binder, "ApplyPotions", true, state, 12d, 10f);
                Assert.That(potion.gameObject.activeSelf, Is.True);
                Assert.That(small.text, Is.EqualTo("4"));
                Assert.That(large.text, Is.EqualTo("2"));
                Invoke(binder, "ApplyPotions", true, state, 13d, 10f);
                Assert.That(small.text, Is.EqualTo("4"));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void HideAll_DisconnectOrDisable_HidesStaleAndUnsynchronizedViews()
        {
            var root = new GameObject("test");
            try
            {
                ReleaseCombatHudBinder binder = root.AddComponent<ReleaseCombatHudBinder>();
                HudBossView boss = Child(root, "boss").AddComponent<HudBossView>();
                HudWaveView wave = Child(root, "wave").AddComponent<HudWaveView>();
                HudReviveStatusView revive = Child(root, "revive").AddComponent<HudReviveStatusView>();
                GameObject budget = Child(root, "reviveBudget");
                GameObject costs = Child(root, "unavailableCosts");
                Set(binder, "_bossView", boss);
                Set(binder, "_waveView", wave);
                Set(binder, "_reviveView", revive);
                Set(binder, "_bossRevivesLabel", budget);
                Set(binder, "_unavailableRoots", new[] { costs });
                Invoke(binder, "HideAll");
                Assert.That(boss.gameObject.activeSelf, Is.False);
                Assert.That(wave.gameObject.activeSelf, Is.False);
                Assert.That(revive.gameObject.activeSelf, Is.False);
                Assert.That(budget.activeSelf, Is.False);
                Assert.That(costs.activeSelf, Is.False);
                Assert.That(root.activeSelf, Is.True, "바인더는 상태가 도착하면 뷰를 다시 켤 수 있어야 한다");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static GameObject Child(GameObject root, string name)
        {
            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(root.transform, false);
            return child;
        }

        private static TMP_Text Label(GameObject root, string name)
        {
            return Child(root, name).AddComponent<TextMeshProUGUI>();
        }

        private static void Set(object target, string name, object value)
        {
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        }

        private static void Invoke(object target, string name, params object[] values)
        {
            target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, values);
        }
    }
}
