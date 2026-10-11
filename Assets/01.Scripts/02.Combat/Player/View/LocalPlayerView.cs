// System
using System;
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Data;

namespace Shinshi.Combat
{
    /// <summary>
    /// UI가 로컬 플레이어를 읽는 창구의 구현, 소유 캐릭터를 묶고 풀 때마다 같은 인스턴스가 구독을 새 캐릭터로 옮긴다.
    /// 서버 상태를 바꾸는 멤버는 없다
    /// </summary>
    public sealed class LocalPlayerView : ILocalPlayerView
    {
        /****************************************
        *                Fields
        ****************************************/

        // 씬 객체가 아니라 UI가 한 번 구독하면 캐릭터가 바뀌어도 그대로 쓰는 계약이라 정적 하나로 둔다
        private static LocalPlayerView _current = null;

        private readonly List<Action<LocalPlayerSnapshot>> _handlers = new List<Action<LocalPlayerSnapshot>>();

        public long TotalExperience => _combat != null ? _combat.TotalExperience : 0;
        private PlayerCombatState _combat         = null;
        private int               _bindingVersion = LocalPlayerSnapshot.UNBOUND_VERSION;
        private float             _resonanceMax   = 0f;

        /****************************************
        *              Properties
        ****************************************/

        public static LocalPlayerView Current
        {
            get
            {
                if (_current == null)
                {
                    _current = new LocalPlayerView();
                }

                return _current;
            }
        }

        public LocalPlayerSnapshot Snapshot
        {
            get
            {
                // 연결이 끊겨 개체가 파괴된 뒤에도 안전하게 빈 스냅샷을 돌려준다
                if (_combat == null)
                {
                    return LocalPlayerSnapshot.Unbound;
                }

                return new LocalPlayerSnapshot(_bindingVersion, _combat.PlayerClass, _combat.LifeState,
                    OperationReceipt.NO_REVISION);
            }
        }

        // 소유 클라이언트가 마지막 기술 요청의 거절을 받았을 때, HUD 칸 피드백용이다
        public event Action<ESkillRequestKind, byte, ERejectReason> SkillRejected;

        /****************************************
        *                Methods
        ****************************************/

        // 도메인 리로드를 끈 에디터에서 이전 플레이의 구독과 개체가 남지 않게 한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            _current = null;
        }

        public LocalPlayerGauges ReadGauges()
        {
            if (_combat == null)
            {
                return LocalPlayerGauges.Empty;
            }

            // 공명 최대치는 카탈로그 값이다. 정해질 때까지 0이면 비율이 0으로 나온다
            if ((_resonanceMax <= 0f) && (GameDB.IsInitialized))
            {
                _resonanceMax = GameDB.Skills.ResonanceMax;
            }

            return LocalPlayerHudRules.BuildGauges(_combat.Health, _combat.MaxHealth, _combat.Energy, _combat.MaxEnergy,
                _combat.Resonance, _resonanceMax, _combat.PlayerClass);
        }

        public void SubscribeSnapshot(Action<LocalPlayerSnapshot> handler)
        {
            if ((handler == null) || (_handlers.Contains(handler)))
            {
                return;
            }

            _handlers.Add(handler);
            handler(Snapshot);
        }

        public void UnsubscribeSnapshot(Action<LocalPlayerSnapshot> handler)
        {
            if (handler == null)
            {
                return;
            }

            _handlers.Remove(handler);
        }

        /// <summary> 소유 캐릭터를 묶는다, 이미 묶인 캐릭터가 있으면 새 캐릭터로 바꾸고 바인딩 번호를 올린다 </summary>
        public void Attach(PlayerCombatState combat)
        {
            if ((combat == null) || (combat == _combat))
            {
                return;
            }

            Unhook();
            _combat       = combat;
            _resonanceMax = 0f;
            ++_bindingVersion;
            _combat.LifeStateChanged   += OnCombatChanged;
            _combat.PlayerClassChanged += OnClassChanged;
            Notify();
        }

        /// <summary> 묶은 캐릭터를 푼다, 다른 캐릭터가 이미 묶였으면 무시한다 </summary>
        public void Detach(PlayerCombatState combat)
        {
            if ((combat == null) || (combat != _combat))
            {
                return;
            }

            Unhook();
            _combat = null;
            Notify();
        }

        /// <summary> 묶은 캐릭터의 입력이 받은 기술 거절을 UI 구독자에게 넘긴다 </summary>
        public void RaiseSkillRejected(ESkillRequestKind kind, byte slot, ERejectReason reason)
        {
            SkillRejected?.Invoke(kind, slot, reason);
        }

        private void Unhook()
        {
            if (_combat == null)
            {
                return;
            }

            _combat.LifeStateChanged   -= OnCombatChanged;
            _combat.PlayerClassChanged -= OnClassChanged;
        }

        private void OnCombatChanged(EParticipantLifeState lifeState)
        {
            Notify();
        }

        private void OnClassChanged(EPlayerClass playerClass)
        {
            Notify();
        }

        // 이산 변화에서만 불리고 handler가 구독을 바꿀 수 있어 사본으로 돌린다
        private void Notify()
        {
            if (_handlers.Count == 0)
            {
                return;
            }

            LocalPlayerSnapshot snapshot = Snapshot;
            Action<LocalPlayerSnapshot>[] copy = _handlers.ToArray();
            for (int i = 0; i < copy.Length; ++i)
            {
                if (_handlers.Contains(copy[i]))
                {
                    copy[i](snapshot);
                }
            }
        }
    }
}
