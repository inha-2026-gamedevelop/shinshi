// System
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// Third Party
using NUnit.Framework;

// Project
using Shinshi.Contracts;
using Shinshi.Data;
using Shinshi.Network;
using Shinshi.UI;

namespace Shinshi.Tests
{
    /// <summary> 강화창을 실제로 끄고 켜서, 결과를 못 본 요청 뒤에 캐시 스냅샷으로 버튼이 다시 열리지 않는지 확인한다 </summary>
    public sealed class EquipmentEnhanceViewPlayModeTests
    {
        /****************************************
        *                Fields
        ****************************************/

        private const BindingFlags INSTANCE = BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly List<GrowthRequestMessage> _sent = new List<GrowthRequestMessage>();

        private GameObject           _root;
        private EquipmentEnhanceView _view;
        private EquipmentCatalogSO   _catalog;

        /****************************************
        *               Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _sent.Clear();
            TownGrowthClient.ResetForTest();
            TownGrowthClient.TestSink = _sent.Add;
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                UnityEngine.Object.DestroyImmediate(_root);
            }

            if (_catalog != null)
            {
                UnityEngine.Object.DestroyImmediate(_catalog);
            }

            TownGrowthClient.ResetForTest();
        }

        /****************************************
        *                Tests
        ****************************************/

        [UnityTest]
        public IEnumerator DisableWithRequestInFlight_ReenableRequestsFreshSnapshotAndBlocksEnhance()
        {
            yield return OpenWindow();
            TownGrowthClient.OnSnapshot(Snapshot(5));
            Assert.IsTrue(GetBool("_hasData"));
            Assert.IsTrue(GetBool("_hasServerSnapshot"));

            Guid operationId = TownGrowthClient.EnhanceEquipment("item-1");
            Assert.AreNotEqual(Guid.Empty, operationId);
            GrowthRefreshReleaseRequest request = (GrowthRefreshReleaseRequest)typeof(EquipmentEnhanceView)
                .GetField("_request", INSTANCE).GetValue(_view);
            Assert.IsTrue(request.Begin(operationId, EGrowthAction.EquipmentEnhance, Time.realtimeSinceStartup, 30f));
            Assert.IsTrue(TownGrowthClient.HasUnresolvedWrite);

            _root.SetActive(false);
            Assert.IsFalse(request.IsInFlight);
            Assert.IsFalse(TownGrowthClient.HasUnresolvedWrite);
            Assert.IsTrue(TownGrowthClient.HasSnapshot);
            Assert.IsTrue(GetBool("_awaitingFreshSnapshot"));
            Assert.IsFalse(GetBool("_hasData"));

            _sent.Clear();
            _root.SetActive(true);

            Assert.AreEqual(1, _sent.Count);
            Assert.AreEqual(EGrowthAction.Snapshot, _sent[0].Action);
            Assert.IsTrue(GetBool("_awaitingFreshSnapshot"));
            Assert.IsFalse(GetBool("_hasData"));
            Assert.IsFalse(GetBool("_hasServerSnapshot"));
            Assert.IsFalse(CanEnhance(out string reason));
            Assert.IsNotEmpty(reason);

            _sent.Clear();
            _view.Enhance();
            Assert.AreEqual(0, _sent.FindAll(m => m.Action == EGrowthAction.EquipmentEnhance).Count);

            TownGrowthClient.OnSnapshot(Snapshot(6));
            Assert.IsFalse(GetBool("_awaitingFreshSnapshot"));
            Assert.IsTrue(GetBool("_hasData"));
            Assert.IsTrue(GetBool("_hasServerSnapshot"));
        }

        [UnityTest]
        public IEnumerator TimeoutRefreshThenReenable_DoesNotApplyCachedSnapshot()
        {
            yield return OpenWindow();
            TownGrowthClient.OnSnapshot(Snapshot(5));

            Guid operationId = TownGrowthClient.EnhanceEquipment("item-1");
            GrowthRefreshReleaseRequest request = (GrowthRefreshReleaseRequest)typeof(EquipmentEnhanceView)
                .GetField("_request", INSTANCE).GetValue(_view);
            Assert.IsTrue(request.Begin(operationId, EGrowthAction.EquipmentEnhance, Time.realtimeSinceStartup, 30f));

            typeof(EquipmentEnhanceView).GetMethod("StopAndRefresh", INSTANCE).Invoke(_view, new object[] { "timeout" });
            Assert.IsTrue(GetBool("_awaitingFreshSnapshot"));
            Assert.IsFalse(GetBool("_hasData"));
            Assert.IsFalse(TownGrowthClient.HasUnresolvedWrite);

            _root.SetActive(false);
            _sent.Clear();
            _root.SetActive(true);

            Assert.AreEqual(1, _sent.Count);
            Assert.AreEqual(EGrowthAction.Snapshot, _sent[0].Action);
            Assert.IsFalse(GetBool("_hasData"));
            Assert.IsFalse(CanEnhance(out string _));
        }

        [UnityTest]
        public IEnumerator DisableWithoutRequest_ReenableStillUsesCachedSnapshot()
        {
            yield return OpenWindow();
            TownGrowthClient.OnSnapshot(Snapshot(5));
            Assert.IsTrue(GetBool("_hasData"));

            _root.SetActive(false);
            _sent.Clear();
            _root.SetActive(true);

            Assert.IsFalse(GetBool("_awaitingFreshSnapshot"));
            Assert.IsTrue(GetBool("_hasData"));
            Assert.AreEqual(0, _sent.Count);
        }

        /****************************************
        *               Helpers
        ****************************************/

        private IEnumerator OpenWindow()
        {
            _root = new GameObject("EnhanceViewPlayModeTest", typeof(RectTransform));
            _root.SetActive(false);
            _view = _root.AddComponent<EquipmentEnhanceView>();
            if (!GameDB.IsInitialized)
            {
                LogAssert.Expect(LogType.Error, new Regex("GameDB"));
            }

            _root.SetActive(true);
            yield return null;

            if (!GameDB.IsInitialized)
            {
                _catalog = ScriptableObject.CreateInstance<EquipmentCatalogSO>();
                typeof(EquipmentEnhanceView).GetField("_catalog", INSTANCE).SetValue(_view, _catalog);
            }

            Assert.IsTrue(GetBool("_runtimeStarted"));
        }

        private static GrowthSnapshotMessage Snapshot(long revision)
        {
            string version = GameDB.IsInitialized ? GameDB.ContentVersion : "test";
            return new GrowthSnapshotMessage(0, revision, 100, 100, new int[5], new TownEquipmentEntry[0], null, null, null,
                null, 0, 0, version);
        }

        private bool GetBool(string field)
        {
            return (bool)typeof(EquipmentEnhanceView).GetField(field, INSTANCE).GetValue(_view);
        }

        private bool CanEnhance(out string reason)
        {
            object[] args = { string.Empty };
            bool ok = (bool)typeof(EquipmentEnhanceView).GetMethod("TryValidate", INSTANCE).Invoke(_view, args);
            reason = (string)args[0];
            return ok;
        }
    }
}
