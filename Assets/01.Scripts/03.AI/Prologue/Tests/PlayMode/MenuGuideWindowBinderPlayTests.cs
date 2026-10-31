// System
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEngine;
using UnityEngine.TestTools;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Network;
using Shinshi.UI;

using Object = UnityEngine.Object;

namespace Shinshi.Tests
{
    public class MenuGuideWindowBinderPlayTests
    {
        private const BindingFlags PRIVATE_INSTANCE = BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags PRIVATE_STATIC = BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags ALL_STATIC = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private FieldInfo _connectionState;
        private object _previousConnectionState;
        private PropertyInfo _clientConnection;
        private object _previousConnection;
        private object _testConnection;
        private FieldInfo _activeTransport;
        private object _previousTransport;
        private FieldInfo _snapshots;
        private object _previousSnapshots;
        private readonly Dictionary<FieldInfo, object> _networkValues = new Dictionary<FieldInfo, object>();
        private bool _previousLoaded;
        private string[] _previousCompleted;
        private object _previousLauncherState;
        private object _previousLauncherAccount;
        private object _previousSessionChanged;
        private object _previousRecordAccount;
        private object _previousGeneration;
        private GameObject _window;

        [SetUp]
        public void SetUp()
        {
            Type client = Type.GetType("Mirror.NetworkClient, Mirror", true);
            Type networkTime = Type.GetType("Mirror.NetworkTime, Mirror", true);
            SaveNetworkValues(client);
            SaveNetworkValues(networkTime);

            // UI 조건만 연결 상태로 만들되 Mirror 프레임 루프가 요구하는 실제 연결 개체도 둔다.
            // 준비 전 연결은 Broadcast를 하지 않고, Ping과 Transport를 막아 이 시험이 실제 패킷을 보내지 않게 한다.
            _clientConnection = client.GetProperty("connection", ALL_STATIC);
            _previousConnection = _clientConnection.GetValue(null);
            _testConnection = Activator.CreateInstance(Type.GetType("Mirror.NetworkConnectionToServer, Mirror", true));
            _clientConnection.GetSetMethod(true).Invoke(null, new[] { _testConnection });
            client.GetField("ready", ALL_STATIC).SetValue(null, false);
            _activeTransport = Type.GetType("Mirror.Transport, Mirror", true).GetField("active", ALL_STATIC);
            _previousTransport = _activeTransport.GetValue(null);
            _activeTransport.SetValue(null, null);
            networkTime.GetField("PingInterval", ALL_STATIC).SetValue(null, float.MaxValue);
            networkTime.GetField("lastPingTime", ALL_STATIC).SetValue(null, Time.unscaledTimeAsDouble);
            FieldInfo qualityInterval = client.GetField("connectionQualityInterval", ALL_STATIC);
            qualityInterval.SetValue(null, Convert.ChangeType(0, qualityInterval.FieldType));
            _snapshots = client.GetField("snapshots", ALL_STATIC);
            _previousSnapshots = _snapshots.GetValue(null);
            _snapshots.SetValue(null, Activator.CreateInstance(_snapshots.FieldType));
            _connectionState = client.GetField("connectState", PRIVATE_STATIC);
            _previousConnectionState = _connectionState.GetValue(null);
            _connectionState.SetValue(null, Enum.Parse(_connectionState.FieldType, "Connected"));

            ServerMenuGuideRecord record = ServerMenuGuideRecord.Instance;
            _previousLoaded = (bool)Field(record, "_loaded");
            var completed = (HashSet<string>)Field(record, "_completed");
            _previousCompleted = new List<string>(completed).ToArray();
            _previousRecordAccount = Field(record, "_accountId");
            _previousGeneration = Field(record, "_generation");
            _previousLauncherState = typeof(LauncherSession).GetField("_state", PRIVATE_STATIC).GetValue(null);
            _previousLauncherAccount = typeof(LauncherSession).GetField("_accountId", PRIVATE_STATIC).GetValue(null);
            _previousSessionChanged = typeof(LauncherSession).GetField("StateChanged", PRIVATE_STATIC).GetValue(null);
            typeof(LauncherSession).GetField("_state", PRIVATE_STATIC).SetValue(null, ELauncherSessionState.Received);
            typeof(LauncherSession).GetField("_accountId", PRIVATE_STATIC).SetValue(null, "menu-guide-test-account");
            _ = record.IsLoaded;
            completed.Clear();
            typeof(ServerMenuGuideRecord).GetField("_loaded", PRIVATE_INSTANCE).SetValue(record, false);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_window != null)
            {
                Object.Destroy(_window);
            }
            yield return null;

            ServerMenuGuideRecord record = ServerMenuGuideRecord.Instance;
            var completed = (HashSet<string>)Field(record, "_completed");
            completed.Clear();
            foreach (string id in _previousCompleted)
            {
                completed.Add(id);
            }
            typeof(ServerMenuGuideRecord).GetField("_loaded", PRIVATE_INSTANCE).SetValue(record, _previousLoaded);
            typeof(ServerMenuGuideRecord).GetField("_accountId", PRIVATE_INSTANCE).SetValue(record, _previousRecordAccount);
            typeof(ServerMenuGuideRecord).GetField("_generation", PRIVATE_INSTANCE).SetValue(record, _previousGeneration);
            typeof(LauncherSession).GetField("_state", PRIVATE_STATIC).SetValue(null, _previousLauncherState);
            typeof(LauncherSession).GetField("_accountId", PRIVATE_STATIC).SetValue(null, _previousLauncherAccount);
            typeof(LauncherSession).GetField("StateChanged", PRIVATE_STATIC).SetValue(null, _previousSessionChanged);
            _testConnection.GetType().GetMethod("Cleanup").Invoke(_testConnection, null);
            _clientConnection.GetSetMethod(true).Invoke(null, new[] { _previousConnection });
            _snapshots.SetValue(null, _previousSnapshots);
            _activeTransport.SetValue(null, _previousTransport);
            foreach (KeyValuePair<FieldInfo, object> value in _networkValues)
            {
                value.Key.SetValue(null, value.Value);
            }
            _networkValues.Clear();
            _connectionState.SetValue(null, _previousConnectionState);
        }

        [UnityTest]
        public IEnumerator OpenBeforeServerList_ArrivingListShowsGuideAndClosingDoesNotRecord()
        {
            MenuGuideWindowBinder binder = CreateWindow(true);
            yield return null;
            yield return null;
            var view = (MenuGuideView)Field(binder, "_view");
            Assert.IsNotNull(view);
            Assert.IsFalse(view.IsShowing, "서버 목록을 받기 전에는 이미 마친 안내를 다시 띄우지 않는다");

            ServerMenuGuideRecord record = ServerMenuGuideRecord.Instance;
            typeof(ServerMenuGuideRecord).GetField("_loaded", PRIVATE_INSTANCE).SetValue(record, true);
            ((Action)Field(record, "Changed"))?.Invoke();
            Assert.IsTrue(view.IsShowing, "열린 창도 늦게 도착한 완료 목록으로 첫 안내를 시작한다");

            _window.SetActive(false);
            Assert.IsFalse(view.IsShowing);
            Assert.IsFalse(record.IsCompleted(MenuGuideWindowBinder.TRAINING_MENU_ID), "창을 닫아 중단하면 완료 요청을 만들지 않는다");
        }

        [UnityTest]
        public IEnumerator OtherPresentationMode_DoesNotShowUntilGuideModeIsSelected()
        {
            typeof(ServerMenuGuideRecord).GetField("_loaded", PRIVATE_INSTANCE).SetValue(ServerMenuGuideRecord.Instance, true);
            MenuGuideWindowBinder binder = CreateWindow(false);
            yield return null;
            yield return null;
            Assert.IsNull(Field(binder, "_view"));

            MenuGuideWindowBinder.Configure(_window.GetComponent<MenuGuideTestWindow>(),
                MenuGuideWindowBinder.TRAINING_MENU_ID, true);
            yield return null;
            yield return null;
            var view = (MenuGuideView)Field(binder, "_view");
            Assert.IsTrue(view.IsShowing);

            MenuGuideWindowBinder.Configure(_window.GetComponent<MenuGuideTestWindow>(),
                MenuGuideWindowBinder.TRAINING_MENU_ID, false);
            Assert.IsFalse(view.IsShowing);
            Assert.IsFalse(_window.transform.Find("MenuGuideHelp").gameObject.activeSelf);
        }

        private MenuGuideWindowBinder CreateWindow(bool available)
        {
            _window = new GameObject("MenuGuideTestWindow", typeof(RectTransform));
            var window = _window.AddComponent<MenuGuideTestWindow>();
            MenuGuideWindowBinder.Configure(window, MenuGuideWindowBinder.TRAINING_MENU_ID, available);
            return _window.GetComponent<MenuGuideWindowBinder>();
        }

        private static object Field(object target, string name)
        {
            return target.GetType().GetField(name, PRIVATE_INSTANCE).GetValue(target);
        }

        private void SaveNetworkValues(Type type)
        {
            foreach (FieldInfo field in type.GetFields(ALL_STATIC))
            {
                if ((!field.IsInitOnly) && (!field.IsLiteral) && (field.FieldType.IsValueType))
                {
                    _networkValues[field] = field.GetValue(null);
                }
            }
        }
    }

    public sealed class MenuGuideTestWindow : UiWindowView
    {
    }
}
