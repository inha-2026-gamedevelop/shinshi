// System
using System;

// Unity
using UnityEngine;

namespace Shinshi.Combat
{
    /// <summary> 서버 동료 판정과 네트워크 개체의 짝을 맞춘다, 소환되면 스폰하고 복귀, 재호출, 소유자 정리 때 내린다 </summary>
    public sealed class CompanionSpawnTracker
    {
        // 개체 수명을 소환, 만료, 사망, 피격, 세션 종료, 이탈 경로마다 따로 걸지 않고 매 틱 활성 번호만 비교한다.
        // 모든 정리 경로가 결국 CompanionServer.Stop으로 모이므로 새 정리 경로가 생겨도 개체가 남지 않는다.
        // 짝은 실행 객체 참조와 칸으로 찾는다. 재접속 보존 기술 서버는 같은 객체가 새 플레이어로 넘어가므로
        // 개체를 다시 만들지 않고 그대로 이어진다. 재호출은 전투 번호가 바뀌므로 옛 개체를 내리고 새로 스폰한다

        /****************************************
        *                Fields
        ****************************************/

        private struct Entry
        {
            public ICompanionSlotSource Source;
            public int                  Slot;
            public int                  CompanionId;
            public CompanionCombatState State;
        }

        private readonly ICompanionObjectHost _host;
        private readonly Entry[]              _entries;

        private int  _count          = 0;
        private bool _overflowLogged = false;

        /****************************************
        *              Properties
        ****************************************/

        public int Count    => _count;
        public int Capacity => _entries.Length;

        /****************************************
        *                Methods
        ****************************************/

        public CompanionSpawnTracker(ICompanionObjectHost host, int capacity)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "동료 개체 상한은 0보다 커야 한다");
            }

            _host    = host;
            _entries = new Entry[capacity];
        }

        /// <summary> 틱마다 Track보다 먼저 부른다, 활성 번호가 바뀌었거나 사라진 칸의 개체를 내린다 </summary>
        public void ReleaseStale()
        {
            for (int i = _count - 1; i >= 0; --i)
            {
                Entry entry = _entries[i];
                if (entry.Source.GetActiveCompanionId(entry.Slot) == entry.CompanionId)
                {
                    continue;
                }

                _host.Despawn(entry.Source, entry.Slot, entry.State);
                RemoveAt(i);
            }
        }

        /// <summary> 지금 틱을 도는 실행 객체마다 부른다, 짝이 없는 활성 동료만 스폰한다 </summary>
        public void Track(ICompanionSlotSource source)
        {
            if (source == null)
            {
                return;
            }

            int slotCount = source.CompanionSlotCount;
            for (int slot = 0; slot < slotCount; ++slot)
            {
                int companionId = source.GetActiveCompanionId(slot);
                if ((companionId == CombatIds.NONE) || (IndexOf(source, slot) >= 0))
                {
                    continue;
                }

                if (_count >= _entries.Length)
                {
                    // 상한은 파티 인원 x 칸 수라 정상 흐름에서는 닿지 않는다. 닿으면 판정은 그대로 돌고 화면에만 빠진다
                    if (!_overflowLogged)
                    {
                        _overflowLogged = true;
                        Debug.LogError($"[Combat] 동료 네트워크 개체 상한 {_entries.Length}을 넘어 스폰하지 않는다, " +
                            $"participantId={source.ParticipantId}, slot={slot}");
                    }

                    return;
                }

                // 실패한 칸은 기록하지 않아 다음 틱에 다시 시도한다
                if (!_host.TrySpawn(source, slot, out CompanionCombatState state))
                {
                    continue;
                }

                _entries[_count] = new Entry { Source = source, Slot = slot, CompanionId = companionId, State = state };
                ++_count;
            }
        }

        /// <summary> 서버 전투가 끝날 때 남은 개체를 모두 내린다 </summary>
        public void ReleaseAll()
        {
            for (int i = _count - 1; i >= 0; --i)
            {
                Entry entry = _entries[i];
                _host.Despawn(entry.Source, entry.Slot, entry.State);
                RemoveAt(i);
            }
        }

        public bool IsTracked(ICompanionSlotSource source, int slot) { return IndexOf(source, slot) >= 0; }

        private int IndexOf(ICompanionSlotSource source, int slot)
        {
            for (int i = 0; i < _count; ++i)
            {
                if ((ReferenceEquals(_entries[i].Source, source)) && (_entries[i].Slot == slot))
                {
                    return i;
                }
            }

            return -1;
        }

        private void RemoveAt(int index)
        {
            --_count;
            _entries[index]  = _entries[_count];
            _entries[_count] = default;
        }
    }
}
