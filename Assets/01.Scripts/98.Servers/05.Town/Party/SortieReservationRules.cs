// System
using System;

// 프로젝트 내부
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 준비를 마친 방을 출정 예약 요청으로 옮기는 순수 계산 </summary>
    public static class SortieReservationRules
    {
        // 출정 계약 값 대응(잠정). 일반전은 지역 ID를 지도와 전투 정의로 쓰고 보스와 난이도는 비운다.
        // 보스전은 보스전 정의 ID를 지도와 전투 정의로, 정의의 보스 ID와 난이도를 그대로 넣는다. API는 보스전에 bossId와
        // 난이도가 없거나 일반전에 있으면 형식 오류(400)로 거절한다
        // TODO(민성): 지도 ID 체계(씬, 지역, 보스 경기장)가 정해지면 mapId를 그 값으로 바꾼다

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 방의 입장 순서대로 참가자를 넣는다. 준비하지 않은 참가자가 있으면 null </summary>
        public static ReservationRequest Build(string operationId, PartyRoom room, string contentVersion)
        {
            if (room == null)
            {
                throw new ArgumentNullException(nameof(room));
            }

            if ((string.IsNullOrEmpty(operationId)) || (room.Count == 0) || (!room.AllReady))
            {
                return null;
            }

            var participants = new ReservationParticipant[room.Count];
            for (int i = 0; i < room.Count; ++i)
            {
                TownMember member = room.MemberAt(i);
                participants[i] = new ReservationParticipant(member.AccountId, member.CharacterId, room.CharacterRevisionAt(i),
                    room.ReservedPotionsAt(i) ?? Array.Empty<ReservationPotion>());
            }

            if (room.Mode == EPartyMode.Boss)
            {
                return new ReservationRequest(operationId, GameSessionCodes.Mode.BOSS, room.ContentId, contentVersion,
                    participants, room.ContentId, room.BossId, room.Difficulty);
            }

            return new ReservationRequest(operationId, GameSessionCodes.Mode.NORMAL, room.ContentId, contentVersion, participants,
                room.ContentId, null, GameSessionCodes.Difficulty.NONE);
        }
    }
}
