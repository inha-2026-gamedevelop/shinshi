// System
using System;
using System.Reflection;

// Unity
using UnityEditor;
using UnityEngine;

// Third Party
using NUnit.Framework;

// Project
using Shinshi.Combat;
using Shinshi.Common;
using Shinshi.UI;

namespace Shinshi.Tests
{
    public sealed class CombatMeterHudLifecycleTests
    {
        private const BindingFlags PRIVATE_INSTANCE = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PRIVATE_STATIC = BindingFlags.Static | BindingFlags.NonPublic;

        [Test]
        public void ClearRealtime_AfterDamageAndResult_HidesHudAndPreservesResult()
        {
            CombatMeterClient.Clear();
            GameObject meter = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/02.Prefab/UI/HUD/PF_HudCombatMeter.prefab"));
            GameObject result = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/02.Prefab/UI/HUD/PF_HudCombatMeterResult.prefab"));
            CombatMeterHudBinder binder = meter.GetComponent<CombatMeterHudBinder>();
            try
            {
                Invoke(binder, "OnDisable");
                typeof(CombatMeterHudBinder).GetField("_resultView", PRIVATE_INSTANCE).SetValue(binder,
                    result.GetComponent<HudCombatMeterResultView>());
                Invoke(binder, "Awake");
                Invoke(binder, "OnEnable");
                Assert.That(result.activeSelf, Is.False);
                typeof(CombatMeterClient).GetMethod("OnSelf", PRIVATE_STATIC).Invoke(null, new object[]
                {
                    new CombatMeterSelfMessage(1, 10, 10, 10, 100, 100, 0, 0, Array.Empty<CombatMeterSlotStat>())
                });
                HudCombatMeterView view = meter.GetComponent<HudCombatMeterView>();
                GameObject collapsed = (GameObject)typeof(HudCombatMeterView).GetField("_collapsedRoot", PRIVATE_INSTANCE).GetValue(view);
                GameObject expanded = (GameObject)typeof(HudCombatMeterView).GetField("_expandedRoot", PRIVATE_INSTANCE).GetValue(view);
                Assert.That(collapsed.activeSelf || expanded.activeSelf, Is.True);
                typeof(CombatMeterClient).GetMethod("OnResult", PRIVATE_STATIC).Invoke(null, new object[]
                {
                    new CombatMeterResultMessage(1, default(ESessionEndReason), 10,
                        Array.Empty<CombatMeterResultEntry>(), Array.Empty<CombatMeterPhaseStat>())
                });
                Assert.That(result.activeSelf, Is.True);
                CombatMeterClient.ClearRealtime();
                Assert.That(collapsed.activeSelf, Is.False);
                Assert.That(expanded.activeSelf, Is.False);
                Assert.That(result.activeSelf, Is.True);
                CombatMeterClient.Clear();
                Assert.That(result.activeSelf, Is.False);
            }
            finally
            {
                Invoke(binder, "OnDisable");
                UnityEngine.Object.DestroyImmediate(meter);
                UnityEngine.Object.DestroyImmediate(result);
                CombatMeterClient.Clear();
            }
        }

        private static void Invoke(CombatMeterHudBinder binder, string method)
        {
            typeof(CombatMeterHudBinder).GetMethod(method, PRIVATE_INSTANCE).Invoke(binder, null);
        }
    }
}
