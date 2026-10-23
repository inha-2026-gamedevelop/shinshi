// System
using System;
using System.Reflection;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;
using Shinshi.UI;

namespace Shinshi.Tests
{
    public sealed class GrowthBinderOwnershipTests
    {
        // 스킬창과 옵션창 바인더의 실제 OnEnable, OnDisable, Update 흐름으로 자기 요청의 소유권 상실을 검사한다.
        // 비활성 사이 내 요청이 끝나거나 연결이 바뀌고 다른 창이 새 잠금을 잡아도, 내 요청만 놓고 남의 잠금은 건드리지 않아야 한다

        /****************************************
        *                Fields
        ****************************************/

        private const BindingFlags INSTANCE = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private GameObject _viewObject;
        private GameObject _binderObject;
        private Component _binder;

        /****************************************
        *               Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            TownGrowthClient.ResetForTest();
            TownGrowthClient.TestSink = _ => { };
        }

        [TearDown]
        public void TearDown()
        {
            if (_binder != null)
            {
                Invoke(_binder, "OnDisable");
            }

            if (_binderObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_binderObject);
            }

            if (_viewObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_viewObject);
            }

            _binder       = null;
            _binderObject = null;
            _viewObject   = null;
            TownGrowthClient.ResetForTest();
        }

        /****************************************
        *                Tests
        ****************************************/

        [TestCase(typeof(EquipmentOptionBinder), typeof(EquipmentOptionView), EGrowthAction.EquipmentRerollOptions)]
        [TestCase(typeof(SkillWindowBinder), typeof(SkillWindowView), EGrowthAction.TalismanUpgrade)]
        public void Binder_OwnRequestFinishedWhileDisabled_ForeignLockTaken_DropsOnlyOwnRequest(Type binderType, Type viewType,
            EGrowthAction action)
        {
            Create(binderType, viewType);
            Invoke(_binder, "OnEnable");
            Guid own = BeginOwn(action);
            Invoke(_binder, "OnDisable");

            TownGrowthClient.OnResult(new GrowthResultMessage(own, action, ERejectReason.None, 0, null));
            Guid foreign = TownGrowthClient.EquipEquipment("other-window-item");
            TownGrowthClient.OnResult(new GrowthResultMessage(foreign, EGrowthAction.EquipmentEquip, ERejectReason.BackendUnavailable, 0, null));

            Invoke(_binder, "OnEnable");
            Invoke(_binder, "Update");

            Assert.That(Request().IsActive, Is.False, "다른 창의 잠금을 내 요청으로 착각해 붙잡지 않는다");
            Assert.That(TownGrowthClient.HasUnresolvedWrite, Is.True, "다른 창의 잠금은 건드리지 않는다");
            Assert.That(TownGrowthClient.UnresolvedOperationId, Is.EqualTo(foreign));
            Assert.That(TownGrowthClient.IsUnresolvedOutcomeUnknown, Is.True);
        }

        [TestCase(typeof(EquipmentOptionBinder), typeof(EquipmentOptionView), EGrowthAction.EquipmentRerollOptions)]
        [TestCase(typeof(SkillWindowBinder), typeof(SkillWindowView), EGrowthAction.TalismanUpgrade)]
        public void Binder_ConnectionReplacedWhileDisabled_ForeignLockTaken_DropsOnlyOwnRequest(Type binderType, Type viewType,
            EGrowthAction action)
        {
            Create(binderType, viewType);
            Invoke(_binder, "OnEnable");
            BeginOwn(action);
            Invoke(_binder, "OnDisable");

            TownGrowthClient.ResetConnectionState();
            Guid foreign = TownGrowthClient.EquipEquipment("other-window-item");

            Invoke(_binder, "OnEnable");
            Invoke(_binder, "Update");

            Assert.That(Request().IsActive, Is.False, "이전 연결의 요청은 새 연결에서 같은 번호로 확인할 수 없어 놓는다");
            Assert.That(TownGrowthClient.UnresolvedOperationId, Is.EqualTo(foreign));
        }

        [TestCase(typeof(EquipmentOptionBinder), typeof(EquipmentOptionView), EGrowthAction.EquipmentRerollOptions)]
        [TestCase(typeof(SkillWindowBinder), typeof(SkillWindowView), EGrowthAction.TalismanUpgrade)]
        public void Binder_OwnLockStillHeld_KeepsRequestAcrossDisableEnable(Type binderType, Type viewType, EGrowthAction action)
        {
            Create(binderType, viewType);
            Invoke(_binder, "OnEnable");
            Guid own = BeginOwn(action);
            Invoke(_binder, "OnDisable");
            TownGrowthClient.OnResult(new GrowthResultMessage(own, action, ERejectReason.BackendUnavailable, 0, null));

            Invoke(_binder, "OnEnable");
            Invoke(_binder, "Update");

            Assert.That(Request().IsActive, Is.True, "내 잠금이 남아 있으면 같은 번호 재확인을 위해 유지한다");
            Assert.That(Request().OperationId, Is.EqualTo(own));
            Assert.That(TownGrowthClient.UnresolvedOperationId, Is.EqualTo(own));
        }

        /****************************************
        *                Helpers
        ****************************************/

        private void Create(Type binderType, Type viewType)
        {
            _viewObject = new GameObject("GrowthBinderOwnershipView");
            _viewObject.SetActive(false);
            Component view = _viewObject.AddComponent(viewType);

            _binderObject = new GameObject("GrowthBinderOwnershipBinder");
            _binderObject.SetActive(false);
            _binder = _binderObject.AddComponent(binderType);
            FieldInfo viewField = binderType.GetField("_view", INSTANCE);
            Assert.That(viewField, Is.Not.Null, binderType.Name + "._view");
            viewField.SetValue(_binder, view);
        }

        private Guid BeginOwn(EGrowthAction action)
        {
            Guid id = (action == EGrowthAction.EquipmentRerollOptions) ? TownGrowthClient.RerollEquipmentOptions("own-item")
                : TownGrowthClient.UpgradeTalisman("own-effect");
            Assert.That(id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(Request().Begin(id, action, Time.unscaledTime, Constants.Network.API_TIMEOUT_SECONDS), Is.True);
            return id;
        }

        private GrowthRequestLifetime Request()
        {
            FieldInfo field = _binder.GetType().GetField("_request", INSTANCE);
            Assert.That(field, Is.Not.Null, _binder.GetType().Name + "._request");
            return (GrowthRequestLifetime)field.GetValue(_binder);
        }

        private static void Invoke(object target, string method)
        {
            MethodInfo info = target.GetType().GetMethod(method, INSTANCE);
            Assert.That(info, Is.Not.Null, target.GetType().Name + "." + method);
            info.Invoke(target, null);
        }
    }
}
