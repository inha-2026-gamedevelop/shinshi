// System
using System;
using System.Globalization;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Network;

namespace Shinshi.UI
{
    /// <summary> 거래 창이 쓰는 순수 계산, 단계별 버튼 상태와 안내 글자, 카운트다운 </summary>
    public static class TradeViewRules
    {
        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 단계, 내 확정, 내 수락에 따른 버튼 상태. 실행 중(Committing)에는 모든 조작이 막힌다 </summary>
        public static TradeButtonStates ButtonsFor(ETradeState state, bool myLocked, bool myAccepted)
        {
            switch (state)
            {
                case ETradeState.Requested:
                    return new TradeButtonStates(false, false, false, false, true, false);
                case ETradeState.Editing:
                    return new TradeButtonStates(true, true, false, false, true, false);
                case ETradeState.Locked:
                case ETradeState.Countdown:
                    return new TradeButtonStates(true, true, myLocked, false, true, false);
                case ETradeState.Accepting:
                    return new TradeButtonStates(true, true, myLocked, !myAccepted, true, false);
                case ETradeState.Committing:
                    return new TradeButtonStates(false, false, myLocked, false, false, true);
                default:
                    return new TradeButtonStates(false, false, false, false, false, false);
            }
        }

        /// <summary> 단계별 안내 한 줄 </summary>
        public static string StatusText(ETradeState state, ERejectReason reason, bool myLocked, bool myAccepted, bool partnerAccepted,
            float countdownRemaining)
        {
            switch (state)
            {
                case ETradeState.Requested:
                    return "상대의 응답을 기다리는 중입니다";
                case ETradeState.Editing:
                    return EditingText(reason);
                case ETradeState.Locked:
                    return myLocked ? "상대의 확정을 기다리는 중입니다" : "상대가 확정했습니다. 내용을 확인하고 확정하세요";
                case ETradeState.Countdown:
                    return string.Format(CultureInfo.InvariantCulture, "{0}초 뒤에 수락할 수 있습니다", CeilSeconds(countdownRemaining));
                case ETradeState.Accepting:
                    if (myAccepted)
                    {
                        return "상대의 수락을 기다리는 중입니다";
                    }

                    return partnerAccepted ? "상대가 수락했습니다" : "수락 버튼을 눌러 거래를 완료하세요";
                case ETradeState.Committing:
                    return "처리 중입니다. 잠시 기다려 주세요";
                case ETradeState.Done:
                    return "거래가 완료되었습니다";
                case ETradeState.Failed:
                    return FailureText(reason, "거래를 완료하지 못했습니다");
                case ETradeState.Cancelled:
                    return FailureText(reason, "거래가 취소되었습니다");
                default:
                    return string.Empty;
            }
        }

        /// <summary> 서버 종료 시각까지 남은 초를 올림한 정수 </summary>
        public static int CeilSeconds(float remaining)
        {
            return (remaining <= 0f) ? 0 : (int)Math.Ceiling(remaining);
        }

        /// <summary> 상태를 받은 시각에 남은 초를 더한 로컬 종료 시각 </summary>
        public static double LocalEnd(double nowLocal, float remaining)
        {
            return nowLocal + Math.Max(0f, remaining);
        }

        public static float RemainingAt(double endLocal, double nowLocal)
        {
            return (float)Math.Max(0.0, endLocal - nowLocal);
        }

        /// <summary> 엽전 입력 글자를 읽는다, 비었으면 0, 숫자가 아니거나 상한을 넘으면 거짓 </summary>
        public static bool TryParseCurrency(string text, out long value)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                value = 0;
                return true;
            }

            return AuctionViewRules.TryParseNumber(text, out value) && (value <= TradeSettings.MAX_CURRENCY);
        }

        public static bool IsFinished(ETradeState state)
        {
            return (state == ETradeState.Done) || (state == ETradeState.Failed) || (state == ETradeState.Cancelled);
        }

        public static string InviteText(string fromName)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}님이 거래를 요청했습니다. 수락할까요?", fromName);
        }

        public static string AcceptConfirmText(string partnerName)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}님과 거래를 완료할까요?", partnerName);
        }

        private static string EditingText(ERejectReason reason)
        {
            if (reason != ERejectReason.None)
            {
                string reasonText = RejectText.Describe(reason);
                if (!string.IsNullOrEmpty(reasonText))
                {
                    return reasonText + ". 내용을 확인하고 다시 확정하세요";
                }
            }

            return "거래할 품목과 엽전을 올리고 확정하세요";
        }

        private static string FailureText(ERejectReason reason, string fallback)
        {
            string text = RejectText.Describe(reason);
            return string.IsNullOrEmpty(text) ? fallback : fallback + " (" + text + ")";
        }
    }
}
