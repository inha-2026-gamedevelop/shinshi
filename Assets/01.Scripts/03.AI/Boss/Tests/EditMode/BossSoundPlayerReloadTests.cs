// System
using System;
using System.Collections;
using System.Reflection;

// Unity
using UnityEngine;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.AI;
using Shinshi.Common;

namespace Shinshi.Tests
{
    // 월백과 무철 소리 재생기는 코드가 같아서 한 묶음으로 검증한다. 스크립트를 다시 컴파일하면 배열은 복원되고 Dictionary만 사라지므로,
    // 같은 상태를 필드 값으로 만들어 정리와 재활성화가 예외 없이 이어지는지 확인한다
    // 실제 재컴파일과 오디오 출력은 이 테스트가 확인하지 않는다
    public class BossSoundPlayerReloadTests
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int          VOICE_COUNT = 16;
        private const int          LOOP_KEY    = 7;
        private const BindingFlags MEMBER      = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /****************************************
        *                Fields
        ****************************************/

        private GameObject _root;
        private AudioClip  _clip;

        /****************************************
        *               Set Up
        ****************************************/

        [SetUp]
        public void SetUp()
        {
            _clip = AudioClip.Create("TestClip", 4800, 1, 48000, false);
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                UnityEngine.Object.DestroyImmediate(_root);
            }

            UnityEngine.Object.DestroyImmediate(_clip);
        }

        /****************************************
        *                Tests
        ****************************************/

        [TestCase(typeof(WolbaekSoundPlayer))]
        [TestCase(typeof(MucheolSoundPlayer))]
        public void StopAll_CalledTwiceAfterLoop_ReleasesVoiceAndDoesNotThrow(Type type)
        {
            MonoBehaviour player = CreatePlayer(type);
            StartLoop(player, LOOP_KEY);

            Assert.DoesNotThrow(() => Call(player, "StopAll"));
            Assert.DoesNotThrow(() => Call(player, "StopAll"));
            Assert.AreEqual(0, LoopCount(player));
            Assert.AreEqual(0, BusyVoiceCount(player));
        }

        [TestCase(typeof(WolbaekSoundPlayer))]
        [TestCase(typeof(MucheolSoundPlayer))]
        public void StopAll_LoopsMissing_StillReleasesBusyVoices(Type type)
        {
            MonoBehaviour player = CreatePlayer(type);
            StartLoop(player, LOOP_KEY);
            Set(player, "_loops", null);

            Assert.DoesNotThrow(() => Call(player, "StopAll"));
            Assert.AreEqual(0, BusyVoiceCount(player), "Dictionary가 없어도 살아 있는 칸은 비워야 한다");
        }

        [TestCase(typeof(WolbaekSoundPlayer))]
        [TestCase(typeof(MucheolSoundPlayer))]
        public void OnDisable_LoopsMissing_DoesNotThrowAndReleasesBusyVoices(Type type)
        {
            MonoBehaviour player = CreatePlayer(type);
            Call(player, "OnEnable");
            StartLoop(player, LOOP_KEY);
            Set(player, "_loops", null);

            Assert.DoesNotThrow(() => Call(player, "OnDisable"));
            Assert.AreEqual(0, BusyVoiceCount(player));
        }

        [TestCase(typeof(WolbaekSoundPlayer))]
        [TestCase(typeof(MucheolSoundPlayer))]
        public void OnEnable_LoopsMissingAfterReload_RebuildsLoopsWithoutNewVoiceChildren(Type type)
        {
            MonoBehaviour player = CreatePlayer(type);
            StartLoop(player, LOOP_KEY);
            Set(player, "_loops", null);

            Call(player, "OnEnable");

            Assert.AreEqual(0, LoopCount(player));
            Assert.AreEqual(0, BusyVoiceCount(player), "키 연결을 잃은 반복음의 칸은 비워야 한다");
            Assert.AreEqual(VOICE_COUNT, _root.transform.childCount, "Voice 자식을 다시 만들면 안 된다");

            Call(player, "OnDisable");
        }

        [TestCase(typeof(WolbaekSoundPlayer))]
        [TestCase(typeof(MucheolSoundPlayer))]
        public void StartLoop_AfterReloadRecovery_PlaysAgainWithoutDuplicates(Type type)
        {
            MonoBehaviour player = CreatePlayer(type);
            StartLoop(player, LOOP_KEY);
            Set(player, "_loops", null);
            Call(player, "OnEnable");

            StartLoop(player, LOOP_KEY);
            StartLoop(player, LOOP_KEY);

            Assert.AreEqual(1, LoopCount(player), "같은 키는 한 번만 잡혀야 한다");
            Assert.AreEqual(1, BusyVoiceCount(player));
            Assert.AreEqual(VOICE_COUNT, _root.transform.childCount);

            Call(player, "OnDisable");
        }

        [TestCase(typeof(WolbaekSoundPlayer))]
        [TestCase(typeof(MucheolSoundPlayer))]
        public void OnEnable_NormalActivationWithRunningLoop_KeepsLoop(Type type)
        {
            MonoBehaviour player = CreatePlayer(type);
            StartLoop(player, LOOP_KEY);

            Call(player, "OnEnable");

            Assert.AreEqual(1, LoopCount(player), "재컴파일이 아닌 일반 활성화는 재생 중인 소리를 끊으면 안 된다");
            Assert.AreEqual(1, BusyVoiceCount(player));

            Call(player, "OnDisable");
        }

        [TestCase(typeof(WolbaekSoundPlayer))]
        [TestCase(typeof(MucheolSoundPlayer))]
        public void DisableThenEnable_AfterLoop_StopsOnDisableAndPlaysAgain(Type type)
        {
            MonoBehaviour player = CreatePlayer(type);
            Call(player, "OnEnable");
            StartLoop(player, LOOP_KEY);

            Call(player, "OnDisable");
            Assert.AreEqual(0, LoopCount(player));
            Assert.AreEqual(0, BusyVoiceCount(player));

            Call(player, "OnEnable");
            StartLoop(player, LOOP_KEY);
            Assert.AreEqual(1, LoopCount(player));

            Call(player, "OnDisable");
        }

        /****************************************
        *                Helpers
        ****************************************/

        // 편집 모드에서는 Awake가 불리지 않으므로 ClientView의 Awake를 직접 불러 Play 때와 같은 초기화를 한다
        private MonoBehaviour CreatePlayer(Type type)
        {
            _root = new GameObject("SoundPlayerTest");
            MonoBehaviour player = (MonoBehaviour)_root.AddComponent(type);
            typeof(ClientView).GetMethod("Awake", MEMBER).Invoke(player, null);

            FieldInfo catalogField = type.GetField("_catalog", MEMBER);
            Type catalogType = catalogField.FieldType;
            FieldInfo entriesField = catalogType.GetField("_entries", MEMBER);
            Type entryType = entriesField.FieldType.GetElementType();

            object entry = Activator.CreateInstance(entryType);
            entryType.GetField("_id", MEMBER).SetValue(entry, FirstSound(type));
            entryType.GetField("_clip", MEMBER).SetValue(entry, _clip);
            entryType.GetField("_maxVoices", MEMBER).SetValue(entry, 3);

            Array entries = Array.CreateInstance(entryType, 1);
            entries.SetValue(entry, 0);

            ScriptableObject catalog = ScriptableObject.CreateInstance(catalogType);
            entriesField.SetValue(catalog, entries);
            catalogField.SetValue(player, catalog);
            return player;
        }

        private static object FirstSound(Type playerType)
        {
            Type soundType = playerType.GetMethod("StartLoop").GetParameters()[1].ParameterType;
            return Enum.GetValues(soundType).GetValue(1);
        }

        private static void StartLoop(MonoBehaviour player, int key)
        {
            player.GetType().GetMethod("StartLoop").Invoke(player, new object[] { key, FirstSound(player.GetType()), Vector3.zero, 0f });
        }

        private static void Call(MonoBehaviour player, string method)
        {
            try
            {
                player.GetType().GetMethod(method, MEMBER).Invoke(player, null);
            }
            catch (TargetInvocationException e)
            {
                throw e.InnerException;
            }
        }

        private static void Set(MonoBehaviour player, string field, object value)
        {
            player.GetType().GetField(field, MEMBER).SetValue(player, value);
        }

        private static int LoopCount(MonoBehaviour player)
        {
            object loops = player.GetType().GetField("_loops", MEMBER).GetValue(player);
            return (loops == null) ? 0 : ((IDictionary)loops).Count;
        }

        // 칸이 비는 시각이 지금보다 뒤인 칸 수, 반복음과 예약음이 차지한 칸이다
        private static int BusyVoiceCount(MonoBehaviour player)
        {
            double[] ends = (double[])player.GetType().GetField("_voiceEnd", MEMBER).GetValue(player);
            double now = AudioSettings.dspTime;
            int count = 0;
            for (int i = 0; i < ends.Length; ++i)
            {
                if (ends[i] > now)
                {
                    ++count;
                }
            }

            return count;
        }
    }
}
