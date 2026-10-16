// System
using System.Collections.Generic;

// Unity
using UnityEngine;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    // 같은 전달함 행이 두 번 올 수 있다(수신 확인 유실, 5초 재전송, 서버 이동 뒤 새 서버가 다시 꺼냄).
    // 전달 ID는 한 표의 IDENTITY라 채팅과 알림 사이에 겹치지 않으므로 ChatLog와 SocialClient가 기억 하나를 함께 쓴다.
    // 기억은 대화 기록과 수명이 같다. 서버 이동 중에는 유지하고 ChatLog.Clear(완전 종료, 캐릭터 선택 복귀)에서만 비운다.
    // 확인 대기 목록은 연결마다 다르다. 새 서버는 이전 서버가 보낸 기록이 없어 그 확인을 버리므로 연결이 끝나면 비운다
    /// <summary> 받은 전달 ID의 중복 제거와 서버로 보낼 수신 확인 대기 목록, 메인 스레드에서만 부른다 </summary>
    public static class DeliveryDedupe
    {
        /****************************************
        *                Fields
        ****************************************/

        private static readonly long[]        G_Ring       = new long[Constants.Chat.DELIVERY_DEDUPE_CAPACITY];
        private static readonly HashSet<long> _seen        = new HashSet<long>();
        private static readonly List<long>    _pendingAcks = new List<long>(Constants.Social.MAX_ACK_IDS_PER_MESSAGE);
        private static readonly HashSet<long> _pendingSet  = new HashSet<long>();

        private static int _next  = 0;
        private static int _count = 0;

        /****************************************
        *              Properties
        ****************************************/

        public static int SeenCount       => _count;
        public static int PendingAckCount => _pendingAcks.Count;

        /****************************************
        *                Methods
        ****************************************/

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            Clear();
        }

        /// <summary> 처음 본 ID면 기억하고 참, 이미 본 ID면 거짓. 0은 전달함을 거치지 않은 줄이라 항상 참이고 기억하지 않는다 </summary>
        public static bool TryMarkSeen(long deliveryId)
        {
            if (deliveryId == 0)
            {
                return true;
            }

            if (_seen.Contains(deliveryId))
            {
                return false;
            }

            if (_count == G_Ring.Length)
            {
                _seen.Remove(G_Ring[_next]);
            }
            else
            {
                ++_count;
            }

            G_Ring[_next] = deliveryId;
            _next         = (_next + 1) % G_Ring.Length;
            _seen.Add(deliveryId);
            return true;
        }

        // 중복으로 버린 줄도 확인해야 서버가 행을 지운다. 그래서 표시 여부와 상관없이 확인 대기에 넣는다
        /// <summary> 받은 전달 ID를 처리한다. 확인 대기에 넣고, 처음 본 ID면 참(표시), 중복이면 거짓 </summary>
        public static bool Accept(long deliveryId)
        {
            if (deliveryId == 0)
            {
                return true;
            }

            if (_pendingSet.Add(deliveryId))
            {
                _pendingAcks.Add(deliveryId);
            }

            return TryMarkSeen(deliveryId);
        }

        /// <summary> 확인 대기 앞에서부터 최대 max개를 꺼낸다, 없으면 null </summary>
        public static long[] TakeAcks(int max)
        {
            int count = Mathf.Min(max, _pendingAcks.Count);
            if (count <= 0)
            {
                return null;
            }

            var ids = new long[count];
            for (int i = 0; i < count; ++i)
            {
                ids[i] = _pendingAcks[i];
                _pendingSet.Remove(ids[i]);
            }

            _pendingAcks.RemoveRange(0, count);
            return ids;
        }

        /// <summary> 연결이 끝났을 때 부른다. 확인 대기만 비우고 본 ID 기억은 남긴다 </summary>
        public static void ClearPendingAcks()
        {
            _pendingAcks.Clear();
            _pendingSet.Clear();
        }

        /// <summary> 대화 기록을 비울 때(완전 종료, 캐릭터 선택 복귀) 함께 비운다 </summary>
        public static void Clear()
        {
            _seen.Clear();
            _pendingAcks.Clear();
            _pendingSet.Clear();
            _next  = 0;
            _count = 0;
        }
    }
}
