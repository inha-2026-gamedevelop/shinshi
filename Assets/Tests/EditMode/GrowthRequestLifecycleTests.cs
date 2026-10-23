// System
using System;

// 서드파티
using NUnit.Framework;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;
using Shinshi.UI;

namespace Shinshi.Tests
{
    public sealed class GrowthRequestLifecycleTests
    {
        // 성장창 요청 수명: 결과 불명은 확정 실패로 바꾸지 않고, 재확인은 자기 요청만, 연결이 바뀌면 이전 요청을 놓는다

        /****************************************
        *                Fields
        ****************************************/

        private const float NOW     = 10f;
        private const float TIMEOUT = 3f;

        private static readonly EGrowthAction[] OWN_ACTIONS = { EGrowthAction.EquipmentRerollOptions };

        /****************************************
        *       GrowthRequestLifetime (keep-lock)
        ****************************************/

        [Test]
        public void Lifetime_Begin_EmptyId_ReturnsFalse()
        {
            GrowthRequestLifetime request = new GrowthRequestLifetime();

            Assert.That(request.Begin(Guid.Empty, EGrowthAction.EquipmentRerollOptions, NOW, TIMEOUT), Is.False);
            Assert.That(request.IsActive, Is.False);
        }

        [Test]
        public void Lifetime_Expire_BecomesUnknownOnlyAtDeadlineAndOnlyOnce()
        {
            GrowthRequestLifetime request = Begun(Guid.NewGuid());

            Assert.That(request.Expire(NOW + TIMEOUT - 0.1f), Is.False);
            Assert.That(request.IsOutcomeUnknown, Is.False);

            Assert.That(request.Expire(NOW + TIMEOUT), Is.True);
            Assert.That(request.IsOutcomeUnknown, Is.True);
            Assert.That(request.IsActive, Is.True, "결과 불명은 요청을 놓지 않는다");
            Assert.That(request.Expire(NOW + TIMEOUT + 1f), Is.False);
        }

        [Test]
        public void Lifetime_Resolve_BackendUnavailable_KeepsRequestAsUnknown()
        {
            Guid id = Guid.NewGuid();
            GrowthRequestLifetime request = Begun(id);

            EGrowthRequestResolve resolve = request.Resolve(id, ERejectReason.BackendUnavailable, out EGrowthAction action);

            Assert.That(resolve, Is.EqualTo(EGrowthRequestResolve.Unknown));
            Assert.That(action, Is.EqualTo(EGrowthAction.EquipmentRerollOptions));
            Assert.That(request.IsOutcomeUnknown, Is.True);
            Assert.That(request.OperationId, Is.EqualTo(id));
        }

        [Test]
        public void Lifetime_Resolve_Confirmed_SettlesAndClears()
        {
            Guid id = Guid.NewGuid();
            GrowthRequestLifetime request = Begun(id);

            EGrowthRequestResolve resolve = request.Resolve(id, ERejectReason.None, out _);

            Assert.That(resolve, Is.EqualTo(EGrowthRequestResolve.Settled));
            Assert.That(request.IsActive, Is.False);
        }

        [Test]
        public void Lifetime_Resolve_ForeignOrLateResult_IsIgnored()
        {
            Guid id = Guid.NewGuid();
            GrowthRequestLifetime request = Begun(id);

            Assert.That(request.Resolve(Guid.NewGuid(), ERejectReason.None, out _), Is.EqualTo(EGrowthRequestResolve.Ignored));
            Assert.That(request.IsActive, Is.True);

            request.Resolve(id, ERejectReason.None, out _);
            Assert.That(request.Resolve(id, ERejectReason.None, out _), Is.EqualTo(EGrowthRequestResolve.Ignored));
        }

        [Test]
        public void Lifetime_MarkRetrying_ClearsUnknownAndRestartsDeadline()
        {
            GrowthRequestLifetime request = Begun(Guid.NewGuid());
            request.Expire(NOW + TIMEOUT);

            request.MarkRetrying(NOW + 20f, TIMEOUT);

            Assert.That(request.IsOutcomeUnknown, Is.False);
            Assert.That(request.Expire(NOW + 20f + TIMEOUT - 0.1f), Is.False);
            Assert.That(request.Expire(NOW + 20f + TIMEOUT), Is.True);
        }

        [Test]
        public void Lifetime_CanRetry_OnlyForOwnUnknownRequestOnHeldLock()
        {
            Guid id = Guid.NewGuid();
            GrowthRequestLifetime request = Begun(id);
            GrowthWriteLock writeLock = new GrowthWriteLock();
            writeLock.TryBegin(id, EGrowthAction.EquipmentRerollOptions);

            Assert.That(CanRetry(request, writeLock), Is.False, "아직 응답을 기다리는 중이면 다시 확인하지 않는다");

            request.Resolve(id, ERejectReason.BackendUnavailable, out _);
            writeLock.Resolve(id, ERejectReason.BackendUnavailable);

            Assert.That(CanRetry(request, writeLock), Is.True);
        }

        [Test]
        public void Lifetime_CanRetry_RefusesLockHeldByAnotherWindowOrAction()
        {
            Guid id = Guid.NewGuid();
            GrowthRequestLifetime request = Begun(id);
            request.Expire(NOW + TIMEOUT);

            GrowthWriteLock otherOperation = new GrowthWriteLock();
            otherOperation.TryBegin(Guid.NewGuid(), EGrowthAction.EquipmentRerollOptions);
            otherOperation.Resolve(otherOperation.OperationId, ERejectReason.BackendUnavailable);
            Assert.That(CanRetry(request, otherOperation), Is.False, "다른 요청의 잠금은 다시 확인할 수 없다");

            GrowthWriteLock otherAction = new GrowthWriteLock();
            otherAction.TryBegin(id, EGrowthAction.TalismanLoadout);
            otherAction.Resolve(id, ERejectReason.BackendUnavailable);
            Assert.That(CanRetry(request, otherAction), Is.False, "이 창의 종류가 아니면 다시 확인할 수 없다");
        }

        [Test]
        public void Lifetime_DropIfLockLost_ReleasesOnlyWhenActiveAndOwnLockGone()
        {
            Guid id = Guid.NewGuid();
            GrowthRequestLifetime request = new GrowthRequestLifetime();

            Assert.That(request.DropIfLockLost(false, Guid.Empty), Is.False, "요청이 없으면 할 일이 없다");

            request.Begin(id, EGrowthAction.EquipmentRerollOptions, NOW, TIMEOUT);
            Assert.That(request.DropIfLockLost(true, id), Is.False, "내 번호의 잠금이 남아 있으면 유지한다");
            Assert.That(request.IsActive, Is.True);

            Assert.That(request.DropIfLockLost(false, Guid.Empty), Is.True);
            Assert.That(request.IsActive, Is.False);
        }

        [Test]
        public void Lifetime_DropIfLockLost_LockHeldByAnotherOperation_DropsOwnRequestAndKeepsForeignLock()
        {
            Guid own = Guid.NewGuid();
            Guid foreign = Guid.NewGuid();
            GrowthRequestLifetime request = Begun(own);
            GrowthWriteLock writeLock = new GrowthWriteLock();
            writeLock.TryBegin(foreign, EGrowthAction.TalismanLoadout);
            writeLock.Resolve(foreign, ERejectReason.BackendUnavailable);

            Assert.That(request.DropIfLockLost(writeLock.IsHeld, writeLock.OperationId), Is.True, "다른 창의 잠금을 내 요청으로 착각하지 않는다");

            Assert.That(request.IsActive, Is.False);
            Assert.That(writeLock.IsHeld, Is.True, "다른 창의 잠금은 건드리지 않는다");
            Assert.That(writeLock.OperationId, Is.EqualTo(foreign));
            Assert.That(writeLock.IsOutcomeUnknown, Is.True);
        }

        [Test]
        public void Lifetime_DropIfLockLost_AfterOwnDropForeignUnknownLock_IsNotRetriedByThisWindow()
        {
            Guid own = Guid.NewGuid();
            GrowthRequestLifetime request = Begun(own);
            request.Expire(NOW + TIMEOUT);
            GrowthWriteLock writeLock = new GrowthWriteLock();
            writeLock.TryBegin(Guid.NewGuid(), EGrowthAction.EquipmentRerollOptions);
            writeLock.Resolve(writeLock.OperationId, ERejectReason.BackendUnavailable);
            Assert.That(CanRetry(request, writeLock), Is.False, "같은 종류의 다른 요청 잠금이라도 번호가 달라 다시 확인할 수 없다");

            request.DropIfLockLost(writeLock.IsHeld, writeLock.OperationId);

            Assert.That(request.IsActive, Is.False, "다른 창의 결과 불명 동안 이 창이 영구히 대기하지 않는다");
            Assert.That(CanRetry(request, writeLock), Is.False);
        }

        [Test]
        public void Lifetime_ReconnectClearsLock_UnknownRequestIsDroppedNotRetried()
        {
            Guid id = Guid.NewGuid();
            GrowthRequestLifetime request = Begun(id);
            GrowthWriteLock writeLock = new GrowthWriteLock();
            writeLock.TryBegin(id, EGrowthAction.EquipmentRerollOptions);
            request.Resolve(id, ERejectReason.BackendUnavailable, out _);
            writeLock.Resolve(id, ERejectReason.BackendUnavailable);
            Assert.That(CanRetry(request, writeLock), Is.True);

            writeLock.Clear();

            Assert.That(CanRetry(request, writeLock), Is.False, "연결이 바뀐 뒤에는 이전 원문을 다시 보내지 않는다");
            Assert.That(request.DropIfLockLost(writeLock.IsHeld, writeLock.OperationId), Is.True);
            Assert.That(request.IsActive, Is.False, "이전 연결의 결과 불명이 창을 영구히 막지 않는다");
        }

        /****************************************
        *   GrowthRefreshReleaseRequest (release)
        ****************************************/

        [Test]
        public void Release_Attach_WithoutPrepareOrWithEmptyId_ReturnsFalse()
        {
            GrowthRefreshReleaseRequest request = new GrowthRefreshReleaseRequest();
            Assert.That(request.Attach(Guid.NewGuid(), NOW, TIMEOUT), Is.False);

            request.Prepare(EGrowthAction.EquipmentEnhance);
            Assert.That(request.Attach(Guid.Empty, NOW, TIMEOUT), Is.False);
            Assert.That(request.IsInFlight, Is.True, "붙이지 못해도 화면이 정리할 때까지 진행 중으로 남는다");
            Assert.That(request.OperationId, Is.EqualTo(Guid.Empty));
        }

        [Test]
        public void Release_Matches_RequiresSameIdAndAction()
        {
            Guid id = Guid.NewGuid();
            GrowthRefreshReleaseRequest request = new GrowthRefreshReleaseRequest();

            request.Prepare(EGrowthAction.Training);
            Assert.That(request.Matches(Guid.Empty, EGrowthAction.Training), Is.False, "번호가 붙기 전에는 무엇도 짝짓지 않는다");

            request.Attach(id, NOW, TIMEOUT);
            Assert.That(request.Matches(id, EGrowthAction.Training), Is.True);
            Assert.That(request.Matches(id, EGrowthAction.TrainingRespec), Is.False);
            Assert.That(request.Matches(Guid.NewGuid(), EGrowthAction.Training), Is.False);
        }

        [Test]
        public void Release_IsExpired_OnTimeoutOrDisconnectOnlyAfterAttach()
        {
            GrowthRefreshReleaseRequest request = new GrowthRefreshReleaseRequest();
            request.Prepare(EGrowthAction.EquipmentEnhance);
            Assert.That(request.IsExpired(NOW + 100f, false), Is.False, "번호가 붙기 전에는 만료로 보지 않는다");

            request.Attach(Guid.NewGuid(), NOW, TIMEOUT);
            Assert.That(request.IsExpired(NOW + TIMEOUT - 0.1f, true), Is.False);
            Assert.That(request.IsExpired(NOW + TIMEOUT, true), Is.True);
            Assert.That(request.IsExpired(NOW + 0.1f, false), Is.True);
        }

        [Test]
        public void Release_Begin_EqualsPrepareThenAttach()
        {
            Guid id = Guid.NewGuid();
            GrowthRefreshReleaseRequest request = new GrowthRefreshReleaseRequest();

            Assert.That(request.Begin(id, EGrowthAction.EquipmentEnhance, NOW, TIMEOUT), Is.True);

            Assert.That(request.IsInFlight, Is.True);
            Assert.That(request.OperationId, Is.EqualTo(id));
            Assert.That(request.Action, Is.EqualTo(EGrowthAction.EquipmentEnhance));
            Assert.That(request.Begin(Guid.Empty, EGrowthAction.EquipmentEnhance, NOW, TIMEOUT), Is.False);
        }

        [Test]
        public void Release_Clear_ReturnsOwnIdAndResets()
        {
            Guid id = Guid.NewGuid();
            GrowthRefreshReleaseRequest request = new GrowthRefreshReleaseRequest();
            request.Begin(id, EGrowthAction.Training, NOW, TIMEOUT);

            Assert.That(request.Clear(), Is.EqualTo(id));

            Assert.That(request.IsInFlight, Is.False);
            Assert.That(request.OperationId, Is.EqualTo(Guid.Empty));
            Assert.That(request.Clear(), Is.EqualTo(Guid.Empty));
        }

        [Test]
        public void Release_OwnIdReleasesLock_ForeignHolderIsKept()
        {
            Guid own = Guid.NewGuid();
            GrowthRefreshReleaseRequest request = new GrowthRefreshReleaseRequest();
            GrowthWriteLock writeLock = new GrowthWriteLock();
            writeLock.TryBegin(own, EGrowthAction.EquipmentEnhance);
            request.Begin(own, EGrowthAction.EquipmentEnhance, NOW, TIMEOUT);

            Assert.That(writeLock.Release(request.Clear()), Is.True);
            Assert.That(writeLock.IsHeld, Is.False);

            Guid foreign = Guid.NewGuid();
            writeLock.TryBegin(foreign, EGrowthAction.TalismanLoadout);
            request.Begin(own, EGrowthAction.EquipmentEnhance, NOW, TIMEOUT);

            Assert.That(writeLock.Release(request.Clear()), Is.False, "다른 창의 요청 잠금은 풀지 않는다");
            Assert.That(writeLock.IsHeld, Is.True);
            Assert.That(writeLock.OperationId, Is.EqualTo(foreign));
        }

        [Test]
        public void Release_NeverAttached_ReleasesNothing()
        {
            GrowthRefreshReleaseRequest request = new GrowthRefreshReleaseRequest();
            GrowthWriteLock writeLock = new GrowthWriteLock();
            Guid foreign = Guid.NewGuid();
            writeLock.TryBegin(foreign, EGrowthAction.TalismanLoadout);
            request.Prepare(EGrowthAction.Training);

            Assert.That(writeLock.Release(request.Clear()), Is.False);
            Assert.That(writeLock.IsHeld, Is.True);
        }

        /****************************************
        *                Helpers
        ****************************************/

        private static GrowthRequestLifetime Begun(Guid id)
        {
            GrowthRequestLifetime request = new GrowthRequestLifetime();
            request.Begin(id, EGrowthAction.EquipmentRerollOptions, NOW, TIMEOUT);
            return request;
        }

        private static bool CanRetry(GrowthRequestLifetime request, GrowthWriteLock writeLock)
        {
            return request.CanRetry(writeLock.IsHeld, writeLock.IsOutcomeUnknown, writeLock.Action, writeLock.OperationId, OWN_ACTIONS);
        }
    }
}
