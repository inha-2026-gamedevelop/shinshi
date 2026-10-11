// System
using System;
using System.Collections.Generic;

namespace Shinshi.AI
{
    // 투사체는 패턴이 끝난 뒤에도 지속물 관리자 안에서 계속 날아서 피격 기록이 패턴 안에 있을 수 없다. 한 시전의 발들이 이 객체 하나를 함께 들고 있어
    // 같은 시전의 다른 발이 이미 맞힌 횟수를 서로 볼 수 있다. 막힌 유효 타격(방어, 반격)도 횟수에 센다
    // 기록은 시전이 시작될 때 새로 만들어 시전 사이에 공유하지 않는다
    /// <summary> 한 시전에서 대상 한 명이 투사체에 맞은 횟수 기록, 상한에 닿은 대상은 같은 시전의 남은 발이 지나친다 </summary>
    public sealed class BossProjectileHitLimit
    {
        /****************************************
        *              Constants
        ****************************************/

        private const int INITIAL_CAPACITY = 8; // 대상별 기록의 시작 크기, 부족하면 늘어난다

        /****************************************
        *                Fields
        ****************************************/

        // 배열 안에서 제자리 수정하는 내부 값 묶음이라 필드를 public으로 둔다
        private struct Entry
        {
            public int CombatId;
            public int Hits;
        }

        private readonly int         _maxHits;
        private readonly List<Entry> _entries = new List<Entry>(INITIAL_CAPACITY);

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 대상 한 명이 한 시전에서 맞을 수 있는 최대 횟수 </summary>
        public int MaxHits => _maxHits;

        /****************************************
        *                Methods
        ****************************************/

        public BossProjectileHitLimit(int maxHits)
        {
            if (maxHits < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxHits));
            }

            _maxHits = maxHits;
        }

        /// <summary> 대상이 이 시전에서 지금까지 맞은 횟수, 막힌 유효 타격을 포함한다 </summary>
        public int GetHitCount(int combatId)
        {
            int index = FindEntry(combatId);
            return (index < 0) ? 0 : _entries[index].Hits;
        }

        /// <summary> 대상이 상한만큼 맞았는가, 그렇다면 이 시전의 남은 발은 대상을 지나친다 </summary>
        public bool IsExhausted(int combatId)
        {
            return GetHitCount(combatId) >= _maxHits;
        }

        /// <summary> 유효 타격 한 번을 센다, 상한을 넘겨 세지 않는다 </summary>
        public void RecordHit(int combatId)
        {
            int index = FindEntry(combatId);
            if (index < 0)
            {
                _entries.Add(new Entry { CombatId = combatId, Hits = 1 });
                return;
            }

            Entry entry = _entries[index];
            if (entry.Hits >= _maxHits)
            {
                return;
            }

            ++entry.Hits;
            _entries[index] = entry;
        }

        private int FindEntry(int combatId)
        {
            for (int i = 0; i < _entries.Count; ++i)
            {
                if (_entries[i].CombatId == combatId)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
