// System
using System;
using System.Collections.Generic;
using System.Security.Cryptography;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 출정 비밀 salt, 출정 ID, 영구 참가자 ID, 처치 순번으로 개인 드롭을 결정한다, 같은 입력은 같은 결과다 </summary>
    public static class LootRoller
    {
        // 출정 ID, 참가자 ID, 처치 순번은 클라이언트도 알거나 셀 수 있다. 이것만으로 굴리면 드롭을 미리 계산해 처치 순서를
        // 고를 수 있으므로, start 확정 때 만든 비밀 salt를 맨 앞에 섞는다. salt는 프로세스 밖으로 나가지 않아 결과는
        // 이 프로세스 안에서만 결정적이다. 저장 재시도는 굴린 결과를 큐에 담아 다시 보내므로 다시 굴리지 않는다.
        // 내부 상태는 64비트라 salt 256비트를 다 쓰지는 못하지만, 출정 중 관측할 수 있는 드롭 수로는 상태를 찾을 수 없다

        /****************************************
        *                Fields
        ****************************************/

        private const ulong  HASH_OFFSET          = 14695981039346656037UL;
        private const ulong  HASH_PRIME           = 1099511628211UL;
        private const ulong  MIX_STEP             = 0x9E3779B97F4A7C15UL;
        private const ulong  MIX_FIRST            = 0xBF58476D1CE4E5B9UL;
        private const ulong  MIX_SECOND           = 0x94D049BB133111EBUL;
        private const int    BYTE_BITS            = 8;
        private const int    MIX_SHIFT_FIRST      = 30;
        private const int    MIX_SHIFT_SECOND     = 27;
        private const int    MIX_SHIFT_FINAL      = 31;
        private const int    DOUBLE_SHIFT         = 11;
        private const double DOUBLE_SCALE         = 1.0 / 9007199254740992.0;
        private const string BOSS_MATERIAL_DOMAIN = "shinshi.boss-material";
        private const string BOSS_AUCTION_DOMAIN  = "shinshi.boss-auction";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> effects는 시작 시 고정한 자기 직업 효과 ID 집합, 이미 보유한 효과도 포함한다 </summary>
        public static LootRollResult Roll(byte[] salt, string sessionId, string participantId, long killSequence,
            in EnemyLootRule rule, IReadOnlyList<string> effects)
        {
            if ((salt == null) || (salt.Length != Constants.Session.LOOT_SALT_BYTES))
            {
                throw new ArgumentException("출정 비밀 salt가 필요하다", nameof(salt));
            }

            if (string.IsNullOrEmpty(sessionId))
            {
                throw new ArgumentException("출정 ID가 비어 있다", nameof(sessionId));
            }

            if (string.IsNullOrEmpty(participantId))
            {
                throw new ArgumentException("영구 참가자 ID가 비어 있다", nameof(participantId));
            }

            if (killSequence <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(killSequence));
            }

            if ((rule.TalismanChance > 0.0) && ((effects == null) || (effects.Count == 0)))
            {
                throw new ArgumentException("부적 드롭에 필요한 직업 효과가 없다", nameof(effects));
            }

            ulong state = HASH_OFFSET;
            HashBytes(ref state, salt);
            HashString(ref state, sessionId);
            HashString(ref state, participantId);
            HashNumber(ref state, unchecked((ulong)killSequence));
            int tickets = (UnitInterval(Next(ref state)) < rule.TicketChance) ? rule.TicketQuantity : 0;
            bool talisman = UnitInterval(Next(ref state)) < rule.TalismanChance;
            int quantity = talisman ? rule.TalismanQuantity : 0;
            string effectId = null;
            if (quantity > 0)
            {
                // 나머지 편향을 제거해 후보 수가 2의 거듭제곱이 아니어도 균등하게 선택한다
                ulong count = (ulong)effects.Count;
                ulong threshold = unchecked(0UL - count) % count;
                ulong draw;
                do
                {
                    draw = Next(ref state);
                }
                while (draw < threshold);

                effectId = effects[(int)(draw % count)];
            }

            return new LootRollResult(rule.ExtraEssence, tickets, quantity, effectId);
        }

        /// <summary> 보스 처치 성공 때 참가자 한 명의 일반 보스 재료 수량을 정한다, 같은 입력은 같은 결과다 </summary>
        public static long RollBossMaterial(byte[] salt, string sessionId, string participantId, in BossRewardRule rule)
        {
            if ((salt == null) || (salt.Length != Constants.Session.LOOT_SALT_BYTES))
            {
                throw new ArgumentException("출정 비밀 salt가 필요하다", nameof(salt));
            }

            if ((string.IsNullOrEmpty(sessionId)) || (string.IsNullOrEmpty(participantId)))
            {
                throw new ArgumentException("출정 ID와 영구 참가자 ID가 필요하다");
            }

            // 처치 드롭과 같은 salt를 쓰므로 영역 이름을 섞어 처치 순번 해시와 겹치지 않게 한다
            ulong state = HASH_OFFSET;
            HashBytes(ref state, salt);
            HashString(ref state, BOSS_MATERIAL_DOMAIN);
            HashString(ref state, sessionId);
            HashString(ref state, participantId);
            long quantity = 0;
            for (int i = 0; i < rule.MaterialRolls; ++i)
            {
                if (UnitInterval(Next(ref state)) < rule.MaterialRollChance)
                {
                    quantity += rule.MaterialPerSuccess;
                }
            }

            return quantity;
        }

        /// <summary> 보스 처치 성공 때 파티 경매 희귀 재료가 나오는지 정한다, 파티가 한 번의 판정을 공유하고 같은 입력은 같은 결과다 </summary>
        public static bool RollBossAuction(byte[] salt, string sessionId, double chance)
        {
            if ((salt == null) || (salt.Length != Constants.Session.LOOT_SALT_BYTES))
            {
                throw new ArgumentException("출정 비밀 salt가 필요하다", nameof(salt));
            }

            if (string.IsNullOrEmpty(sessionId))
            {
                throw new ArgumentException("출정 ID가 필요하다", nameof(sessionId));
            }

            ulong state = HASH_OFFSET;
            HashBytes(ref state, salt);
            HashString(ref state, BOSS_AUCTION_DOMAIN);
            HashString(ref state, sessionId);
            return UnitInterval(Next(ref state)) < chance;
        }

        /// <summary> 출정 하나의 비밀 salt를 암호학적 난수로 만든다, start 확정 때 한 번 부른다 </summary>
        public static byte[] CreateSalt()
        {
            var salt = new byte[Constants.Session.LOOT_SALT_BYTES];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                random.GetBytes(salt);
            }

            return salt;
        }

        // 런타임 문자열 해시는 프로세스마다 달라질 수 있어 길이와 UTF-16 코드를 직접 해시한다
        private static void HashString(ref ulong state, string value)
        {
            HashNumber(ref state, (ulong)value.Length);
            for (int i = 0; i < value.Length; ++i)
            {
                unchecked
                {
                    state = (state ^ (byte)value[i]) * HASH_PRIME;
                    state = (state ^ (byte)(value[i] >> BYTE_BITS)) * HASH_PRIME;
                }
            }
        }

        private static void HashBytes(ref ulong state, byte[] value)
        {
            for (int i = 0; i < value.Length; ++i)
            {
                unchecked
                {
                    state = (state ^ value[i]) * HASH_PRIME;
                }
            }
        }

        private static void HashNumber(ref ulong state, ulong value)
        {
            for (int i = 0; i < sizeof(ulong); ++i)
            {
                unchecked
                {
                    state = (state ^ (byte)value) * HASH_PRIME;
                }

                value >>= BYTE_BITS;
            }
        }

        private static ulong Next(ref ulong state)
        {
            unchecked
            {
                state += MIX_STEP;
                ulong value = state;
                value = (value ^ (value >> MIX_SHIFT_FIRST)) * MIX_FIRST;
                value = (value ^ (value >> MIX_SHIFT_SECOND)) * MIX_SECOND;
                return value ^ (value >> MIX_SHIFT_FINAL);
            }
        }

        private static double UnitInterval(ulong value)
        {
            return (value >> DOUBLE_SHIFT) * DOUBLE_SCALE;
        }
    }
}
