// System
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

// Unity
using UnityEngine;

// 서드파티
using Mirror;
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class AuthenticatedSendGuardTests
    {
        // 전송 계층만 연결되고 인증이 끝나지 않은 연결에는 소셜과 채팅 요청을 보내지 않는다. 서버는 인증 전 메시지에 연결을 끊는다.
        // 실제 NetworkClient.Send와 배치 전송을 거쳐 테스트 전송 계층에 도착한 메시지 ID로 송신 여부를 확인한다

        /****************************************
        *                Fields
        ****************************************/

        private const BindingFlags STATIC_ANY   = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags INSTANCE_ANY = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const double       LATE_NOW     = 1.0e9;
        private const long         ACK_ID       = 4242;
        private const string       TARGET_NAME  = "tester";
        private const string       CHAT_TEXT    = "hello";

        private static readonly Type[] G_StaticTypes =
        {
            typeof(SocialClient), typeof(ChatClient), typeof(ChatLog), typeof(DeliveryDedupe),
        };

        private GameObject           _host          = null;
        private RecordingTransport   _transport     = null;
        private object               _savedState    = null;
        private NetworkConnectionToServer _savedConnection = null;
        private NetworkManager       _savedSingleton = null;
        private Transport            _savedActive   = null;
        private List<StaticSnapshot> _savedStatics  = null;

        /****************************************
        *                Setup
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _savedState      = GetConnectState();
            _savedConnection = NetworkClient.connection;
            _savedSingleton  = NetworkManager.singleton;
            _savedActive     = Transport.active;
            _savedStatics    = SnapshotStatics();

            _host      = new GameObject("AuthenticatedSendGuardTests");
            _transport = _host.AddComponent<RecordingTransport>();
            ShinshiNetworkManager manager = _host.AddComponent<ShinshiNetworkManager>();
            manager.ConfigureServerRole(EServerRole.Town);

            // EditMode의 AddComponent는 Awake를 부르지 않아 싱글턴과 활성 전송 계층을 직접 맞춘다
            SetSingleton(manager);
            Transport.active = _transport;
            SetConnectState("Connected");
            SetConnection(new NetworkConnectionToServer());

            SocialClient.ResetConnection();
            ChatClient.ClearHistory();
        }

        [TearDown]
        public void TearDown()
        {
            RestoreStatics(_savedStatics);
            SetConnection(_savedConnection);
            SetConnectStateValue(_savedState);
            SetSingleton(_savedSingleton);
            Transport.active = _savedActive;

            if (_host != null)
            {
                UnityEngine.Object.DestroyImmediate(_host);
                _host = null;
            }
        }

        /****************************************
        *                Tests
        ****************************************/

        [Test]
        public void IsAuthenticatedConnection_ConnectedButNotAuthenticated_ReturnsFalse()
        {
            Assert.IsTrue(NetworkClient.isConnected);
            Assert.IsFalse(ShinshiClientAuthenticator.IsAuthenticatedConnection());
        }

        [Test]
        public void IsAuthenticatedConnection_ConnectedAndAuthenticated_ReturnsTrue()
        {
            Authenticate();

            Assert.IsTrue(ShinshiClientAuthenticator.IsAuthenticatedConnection());
        }

        [Test]
        public void IsAuthenticatedConnection_AuthenticatedButNoConnection_ReturnsFalse()
        {
            Authenticate();
            SetConnection(null);

            Assert.IsFalse(ShinshiClientAuthenticator.IsAuthenticatedConnection());
        }

        [Test]
        public void SocialClient_ConnectedButNotAuthenticated_SendsNothing()
        {
            Assert.IsFalse(SocialClient.IsTownConnection());
            Assert.IsFalse(SocialClient.RequestSnapshot(LATE_NOW));
            Assert.AreEqual(Guid.Empty, SocialClient.SendFriendRequest(TARGET_NAME));
            Assert.IsFalse(SocialClient.HasPendingWrite);

            Assert.AreEqual(0, FlushAndReadIds().Count);
        }

        [Test]
        public void SocialClient_Authenticated_SendsSnapshotAndWrite()
        {
            Authenticate();

            Assert.IsTrue(SocialClient.IsTownConnection());
            Assert.IsTrue(SocialClient.RequestSnapshot(LATE_NOW));
            Assert.AreNotEqual(Guid.Empty, SocialClient.SendFriendRequest(TARGET_NAME));
            Assert.IsTrue(SocialClient.HasPendingWrite);

            List<ushort> ids = FlushAndReadIds();
            Assert.AreEqual(2, ids.Count);
            Assert.AreEqual(NetworkMessageId<SocialRequestMessage>.Id, ids[0]);
            Assert.AreEqual(NetworkMessageId<SocialRequestMessage>.Id, ids[1]);
        }

        [Test]
        public void SocialClient_SnapshotRequestedBeforeAuth_ResumesAfterAuth()
        {
            Assert.IsFalse(SocialClient.RequestSnapshot(LATE_NOW));
            Assert.AreEqual(0, FlushAndReadIds().Count);

            Authenticate();

            Assert.IsTrue(SocialClient.RequestSnapshot(LATE_NOW));
            List<ushort> ids = FlushAndReadIds();
            Assert.AreEqual(1, ids.Count);
            Assert.AreEqual(NetworkMessageId<SocialRequestMessage>.Id, ids[0]);
        }

        [Test]
        public void SocialClient_PendingAckBeforeAuth_IsKeptAndFlushedAfterAuth()
        {
            Assert.IsTrue(DeliveryDedupe.Accept(ACK_ID));
            Assert.AreEqual(1, DeliveryDedupe.PendingAckCount);

            Assert.IsFalse(SocialClient.FlushAcks(LATE_NOW));
            SocialClient.Tick(LATE_NOW);
            Assert.AreEqual(1, DeliveryDedupe.PendingAckCount);
            Assert.AreEqual(0, FlushAndReadIds().Count);

            Authenticate();

            Assert.IsTrue(SocialClient.FlushAcks(LATE_NOW));
            Assert.AreEqual(0, DeliveryDedupe.PendingAckCount);
            List<ushort> ids = FlushAndReadIds();
            Assert.AreEqual(1, ids.Count);
            Assert.AreEqual(NetworkMessageId<SocialDeliveryAckMessage>.Id, ids[0]);
        }

        [Test]
        public void ChatClient_ConnectedButNotAuthenticated_RejectsAsNotConnectedAndSendsNothing()
        {
            Assert.AreEqual(EChatReject.NotConnected, ChatClient.Send(EChatChannel.All, CHAT_TEXT));
            Assert.AreEqual(EChatReject.NotConnected, ChatClient.SendWhisper(TARGET_NAME, CHAT_TEXT));

            Assert.AreEqual(0, FlushAndReadIds().Count);
        }

        [Test]
        public void ChatClient_Authenticated_SendsChat()
        {
            Authenticate();

            Assert.AreEqual(EChatReject.None, ChatClient.Send(EChatChannel.All, CHAT_TEXT));

            List<ushort> ids = FlushAndReadIds();
            Assert.AreEqual(1, ids.Count);
            Assert.AreEqual(NetworkMessageId<ChatRequestMessage>.Id, ids[0]);
        }

        [Test]
        public void ChatClient_Authenticated_SendsWhisper()
        {
            Authenticate();

            Assert.AreEqual(EChatReject.None, ChatClient.SendWhisper(TARGET_NAME, CHAT_TEXT));

            List<ushort> ids = FlushAndReadIds();
            Assert.AreEqual(1, ids.Count);
            Assert.AreEqual(NetworkMessageId<ChatWhisperRequestMessage>.Id, ids[0]);
        }

        /****************************************
        *                Helpers
        ****************************************/

        // 배치를 실제 전송 경로로 내보내고, 테스트 전송 계층이 받은 메시지 ID를 순서대로 돌려준다
        private List<ushort> FlushAndReadIds()
        {
            _transport.Sent.Clear();
            MethodInfo update = typeof(NetworkConnection).GetMethod("Update", INSTANCE_ANY);
            Assert.IsNotNull(update, "NetworkConnection.Update를 찾지 못했다");
            update.Invoke(NetworkClient.connection, null);

            var ids = new List<ushort>();
            foreach (byte[] batch in _transport.Sent)
            {
                var reader = new NetworkReader(batch);
                reader.ReadDouble();
                while (reader.Remaining > 0)
                {
                    ulong size = Compression.DecompressVarUInt(reader);
                    ids.Add(reader.ReadUShort());
                    reader.Position += (int)size - sizeof(ushort);
                }
            }

            return ids;
        }

        private static void Authenticate()
        {
            NetworkClient.connection.isAuthenticated = true;
        }

        private static object GetConnectState()
        {
            return ConnectStateField().GetValue(null);
        }

        private static void SetConnectState(string stateName)
        {
            FieldInfo field = ConnectStateField();
            field.SetValue(null, Enum.Parse(field.FieldType, stateName));
        }

        private static void SetConnectStateValue(object value)
        {
            ConnectStateField().SetValue(null, value);
        }

        private static FieldInfo ConnectStateField()
        {
            FieldInfo field = typeof(NetworkClient).GetField("connectState", STATIC_ANY);
            Assert.IsNotNull(field, "NetworkClient.connectState를 찾지 못했다");
            return field;
        }

        private static void SetConnection(NetworkConnectionToServer connection)
        {
            PropertyInfo property = typeof(NetworkClient).GetProperty("connection", STATIC_ANY);
            Assert.IsNotNull(property, "NetworkClient.connection을 찾지 못했다");
            property.GetSetMethod(true).Invoke(null, new object[] { connection });
        }

        private static void SetSingleton(NetworkManager manager)
        {
            PropertyInfo property = typeof(NetworkManager).GetProperty("singleton", STATIC_ANY);
            Assert.IsNotNull(property, "NetworkManager.singleton을 찾지 못했다");
            property.GetSetMethod(true).Invoke(null, new object[] { manager });
        }

        // 정적 필드를 통째로 저장하고 되돌린다. 컬렉션은 내용을 복사해 두었다가 같은 인스턴스에 다시 채운다
        private sealed class StaticSnapshot
        {
            public FieldInfo Field;
            public object    Value;
            public object[]  Items;
            public Array     ArrayCopy;
        }

        private static List<StaticSnapshot> SnapshotStatics()
        {
            var snapshots = new List<StaticSnapshot>();
            foreach (Type type in G_StaticTypes)
            {
                foreach (FieldInfo field in type.GetFields(STATIC_ANY | BindingFlags.DeclaredOnly))
                {
                    if (field.IsLiteral)
                    {
                        continue;
                    }

                    object value = field.GetValue(null);
                    var snapshot = new StaticSnapshot { Field = field, Value = value };
                    if (value is Array array)
                    {
                        snapshot.ArrayCopy = (Array)array.Clone();
                    }
                    else if ((value is IEnumerable enumerable) && (!(value is string)) && (HasSingleArgAdd(value.GetType())))
                    {
                        var items = new List<object>();
                        foreach (object item in enumerable)
                        {
                            items.Add(item);
                        }

                        snapshot.Items = items.ToArray();
                    }

                    snapshots.Add(snapshot);
                }
            }

            return snapshots;
        }

        private static void RestoreStatics(List<StaticSnapshot> snapshots)
        {
            if (snapshots == null)
            {
                return;
            }

            foreach (StaticSnapshot snapshot in snapshots)
            {
                if (snapshot.ArrayCopy != null)
                {
                    Array.Copy(snapshot.ArrayCopy, (Array)snapshot.Value, snapshot.ArrayCopy.Length);
                }

                if (snapshot.Items != null)
                {
                    Type type = snapshot.Value.GetType();
                    type.GetMethod("Clear", Type.EmptyTypes)?.Invoke(snapshot.Value, null);
                    MethodInfo add = type.GetMethod("Add", new[] { snapshot.Items.Length > 0 ? snapshot.Items[0].GetType() : typeof(object) })
                        ?? FindSingleArgAdd(type);
                    foreach (object item in snapshot.Items)
                    {
                        add.Invoke(snapshot.Value, new[] { item });
                    }
                }

                if (!snapshot.Field.IsInitOnly)
                {
                    snapshot.Field.SetValue(null, snapshot.Value);
                }
            }
        }

        private static bool HasSingleArgAdd(Type type)
        {
            return (type.GetMethod("Clear", Type.EmptyTypes) != null) && (FindSingleArgAdd(type) != null);
        }

        private static MethodInfo FindSingleArgAdd(Type type)
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                if ((method.Name == "Add") && (method.GetParameters().Length == 1))
                {
                    return method;
                }
            }

            return null;
        }

        // 보낸 배치를 저장만 하는 전송 계층. 실제 네트워크를 열지 않는다
        public sealed class RecordingTransport : Transport
        {
            public readonly List<byte[]> Sent = new List<byte[]>();

            public override bool Available() => true;
            public override bool ClientConnected() => true;
            public override void ClientConnect(string address) { }
            public override void ClientSend(ArraySegment<byte> segment, int channelId = Channels.Reliable)
            {
                var copy = new byte[segment.Count];
                Buffer.BlockCopy(segment.Array, segment.Offset, copy, 0, segment.Count);
                Sent.Add(copy);
            }
            public override void ClientDisconnect() { }
            public override Uri ServerUri() => null;
            public override bool ServerActive() => false;
            public override void ServerStart() { }
            public override void ServerSend(int connectionId, ArraySegment<byte> segment, int channelId = Channels.Reliable) { }
            public override void ServerDisconnect(int connectionId) { }
            public override string ServerGetClientAddress(int connectionId) => string.Empty;
            public override void ServerStop() { }
            public override int GetMaxPacketSize(int channelId = Channels.Reliable) => 1200;
            public override void Shutdown() { }
        }
    }
}
