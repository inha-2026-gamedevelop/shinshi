// System
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Contracts;
using Shinshi.Network;

namespace Shinshi.Tests
{
    public class ServerMenuGuideRecordTests
    {
        private const BindingFlags PRIVATE_INSTANCE = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PRIVATE_STATIC = BindingFlags.Static | BindingFlags.NonPublic;
        private const string ACCOUNT_A = "menu-guide-account-a";
        private const string ACCOUNT_B = "menu-guide-account-b";
        private const string MENU_ID = "menu_training";

        [Test]
        public void AccountChanges_CompletedAndPendingWorkAreClearedAndLateResponseIsIgnored()
        {
            ServerMenuGuideRecord record = ServerMenuGuideRecord.Instance;
            Type type = typeof(ServerMenuGuideRecord);
            Type session = typeof(LauncherSession);
            var completed = (HashSet<string>)type.GetField("_completed", PRIVATE_INSTANCE).GetValue(record);
            string[] previousCompleted = new List<string>(completed).ToArray();
            object previousAccount = type.GetField("_accountId", PRIVATE_INSTANCE).GetValue(record);
            object previousGeneration = type.GetField("_generation", PRIVATE_INSTANCE).GetValue(record);
            object previousLoaded = type.GetField("_loaded", PRIVATE_INSTANCE).GetValue(record);
            object previousState = session.GetField("_state", PRIVATE_STATIC).GetValue(null);
            object previousSessionAccount = session.GetField("_accountId", PRIVATE_STATIC).GetValue(null);
            object previousChanged = session.GetField("StateChanged", PRIVATE_STATIC).GetValue(null);
            object work = type.GetField("_work", PRIVATE_INSTANCE).GetValue(record);
            Assert.That(((ICollection)work).Count, Is.Zero, "실행 중인 기록 전송과 회귀 시험을 섞지 않는다");
            try
            {
                session.GetField("_state", PRIVATE_STATIC).SetValue(null, ELauncherSessionState.Received);
                session.GetField("_accountId", PRIVATE_STATIC).SetValue(null, ACCOUNT_A);
                _ = record.IsLoaded;
                type.GetField("_loaded", PRIVATE_INSTANCE).SetValue(record, true);
                completed.Add(MENU_ID);
                Assert.IsTrue(record.IsCompleted(MENU_ID));

                Type itemType = type.GetNestedType("WorkItem", BindingFlags.NonPublic);
                object oldItem = Activator.CreateInstance(itemType, true);
                itemType.GetField("AccountId").SetValue(oldItem, ACCOUNT_A);
                itemType.GetField("Generation").SetValue(oldItem, type.GetField("_generation", PRIVATE_INSTANCE).GetValue(record));
                itemType.GetField("MenuId").SetValue(oldItem, MENU_ID);
                work.GetType().GetMethod("Enqueue").Invoke(work, new[] { oldItem });

                session.GetField("_accountId", PRIVATE_STATIC).SetValue(null, ACCOUNT_B);
                Assert.IsFalse(record.IsLoaded, "다른 계정의 응답을 받기 전까지 첫 안내를 미룬다");
                Assert.That(completed, Is.Empty);
                Assert.That(((ICollection)work).Count, Is.Zero, "이전 계정의 재시도가 새 계정 자격으로 나가면 안 된다");
                Assert.IsFalse((bool)type.GetMethod("IsCurrent", PRIVATE_INSTANCE).Invoke(record, new[] { oldItem }));

                type.GetMethod("Apply", PRIVATE_INSTANCE).Invoke(record, new[] { oldItem,
                    new MenuGuideListResponse(new[] { MENU_ID }, 0) });
                Assert.IsFalse(record.IsLoaded, "늦은 이전 계정 응답을 현재 목록으로 받아들이면 안 된다");
                Assert.That(completed, Is.Empty);
            }
            finally
            {
                completed.Clear();
                foreach (string id in previousCompleted)
                {
                    completed.Add(id);
                }
                work.GetType().GetMethod("Clear").Invoke(work, null);
                type.GetField("_accountId", PRIVATE_INSTANCE).SetValue(record, previousAccount);
                type.GetField("_generation", PRIVATE_INSTANCE).SetValue(record, previousGeneration);
                type.GetField("_loaded", PRIVATE_INSTANCE).SetValue(record, previousLoaded);
                session.GetField("_state", PRIVATE_STATIC).SetValue(null, previousState);
                session.GetField("_accountId", PRIVATE_STATIC).SetValue(null, previousSessionAccount);
                session.GetField("StateChanged", PRIVATE_STATIC).SetValue(null, previousChanged);
            }
        }
    }
}
