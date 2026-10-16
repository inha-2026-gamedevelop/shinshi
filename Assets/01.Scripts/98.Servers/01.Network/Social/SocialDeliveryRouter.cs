// 프로젝트 내부
using Shinshi.Contracts;

namespace Shinshi.Network
{
    // 로컬 즉시 전달과 꺼내기가 같은 경로를 쓴다. 대상이 이 서버에 없으면 보내지 않고 기록도 남기지 않는다.
    // 행은 DB에 남아 대상이 새로 접속한 서버가 꺼낸다
    /// <summary> 전달함 행을 대상 캐릭터의 연결로 보내고 확인 대기에 적는다, 메인 스레드에서만 부른다 </summary>
    public sealed class SocialDeliveryRouter
    {
        /****************************************
        *                Fields
        ****************************************/

        private readonly ISocialAudience      _audience;
        private readonly SocialDeliveryLedger _ledger;

        /****************************************
        *              Properties
        ****************************************/

        public SocialDeliveryLedger Ledger => _ledger;

        /****************************************
        *                Methods
        ****************************************/

        public SocialDeliveryRouter(ISocialAudience audience, SocialDeliveryLedger ledger)
        {
            _audience = audience;
            _ledger   = ledger;
        }

        /// <summary> 행 배열을 받은 순서대로 보낸다. 보낸 행 수 </summary>
        public int RouteAll(SocialDeliveryRow[] rows)
        {
            if (rows == null)
            {
                return 0;
            }

            int sent = 0;
            for (int i = 0; i < rows.Length; ++i)
            {
                if (Route(rows[i]))
                {
                    ++sent;
                }
            }

            return sent;
        }

        /// <summary> 행 하나를 대상 연결로 보낸다, 대상이 없거나 행이 계약 밖이면 거짓 </summary>
        public bool Route(SocialDeliveryRow row)
        {
            if ((row == null) || (!_audience.TryFindParticipant(row.TargetCharacterId, out int participantId)))
            {
                return false;
            }

            if (SocialDeliveryRules.IsChat(row))
            {
                if (!SocialDeliveryRules.TryBuildChat(row, out ChatBroadcastMessage chat))
                {
                    return false;
                }

                _audience.Send(participantId, chat);
            }
            else
            {
                if (!SocialDeliveryRules.TryBuildNotice(row, out SocialNoticeMessage notice))
                {
                    return false;
                }

                _audience.Send(participantId, notice);
            }

            _ledger.RecordSent(participantId, row.TargetCharacterId, row.DeliveryId);
            return true;
        }
    }
}
