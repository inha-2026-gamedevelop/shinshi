using System;
using System.Globalization;

namespace Shinshi.Editor
{
    /// <summary> 모든 화면이 공유하는 응답 정책 시나리오 키 </summary>
    public static class UiSandboxScenarioKeys
    {
        public const string RESPONSE_MODE  = "response.mode";
        public const string RESPONSE_DELAY = "response.delaySeconds";
        public const string RESPONSE_ERROR = "response.error";

        public const float MAX_DELAY_SECONDS = 300.0f;
    }

    /// <summary> 가짜 요청이 끝나는 방식 </summary>
    public enum EUiSandboxResponseMode
    {
        Success,
        Rejected,
        Disconnected,
        Timeout
    }

    /// <summary> 요청을 시작하는 순간 시나리오에서 떠 둔 응답 정책. 이후 시나리오가 바뀌어도 이 값은 변하지 않는다 </summary>
    public readonly struct UiSandboxResponsePolicy
    {
        public const string DEFAULT_REJECTED     = "요청이 거절되었습니다";
        public const string DEFAULT_DISCONNECTED = "서버와의 연결이 끊겼습니다";
        public const string DEFAULT_TIMEOUT      = "요청 시간이 초과되었습니다";

        public EUiSandboxResponseMode Mode { get; }
        public float DelaySeconds { get; }
        public string Error { get; }

        public UiSandboxResponsePolicy(EUiSandboxResponseMode mode, float delaySeconds, string error)
        {
            Mode         = mode;
            DelaySeconds = SanitizeDelay(delaySeconds);
            Error        = error ?? string.Empty;
        }

        public bool IsSuccess
        {
            get { return Mode == EUiSandboxResponseMode.Success; }
        }

        public static UiSandboxResponsePolicy FromScenario(UiSandboxScenarioSO scenario)
        {
            if (scenario == null)
            {
                return new UiSandboxResponsePolicy(EUiSandboxResponseMode.Success, 0.0f, string.Empty);
            }

            string modeText = scenario.GetString(UiSandboxScenarioKeys.RESPONSE_MODE, string.Empty);
            if ((string.IsNullOrWhiteSpace(modeText))
                || (!Enum.TryParse(modeText.Trim(), true, out EUiSandboxResponseMode mode))
                || (!Enum.IsDefined(typeof(EUiSandboxResponseMode), mode)))
            {
                mode = EUiSandboxResponseMode.Success;
            }

            float delay  = scenario.GetFloat(UiSandboxScenarioKeys.RESPONSE_DELAY, 0.0f);
            string error = scenario.GetString(UiSandboxScenarioKeys.RESPONSE_ERROR, string.Empty);
            return new UiSandboxResponsePolicy(mode, delay, error);
        }

        /// <summary> 실패 모드에서 보여 줄 메시지. 사용자가 적은 오류 문구가 있으면 그것을 우선한다 </summary>
        public string ResolveFailureMessage()
        {
            if (!string.IsNullOrWhiteSpace(Error))
            {
                return Error;
            }

            switch (Mode)
            {
                case EUiSandboxResponseMode.Rejected:     return DEFAULT_REJECTED;
                case EUiSandboxResponseMode.Disconnected: return DEFAULT_DISCONNECTED;
                case EUiSandboxResponseMode.Timeout:      return DEFAULT_TIMEOUT;
                default:                                  return string.Empty;
            }
        }

        private static float SanitizeDelay(float delaySeconds)
        {
            if ((float.IsNaN(delaySeconds)) || (delaySeconds <= 0.0f))
            {
                return 0.0f;
            }

            return Math.Min(delaySeconds, UiSandboxScenarioKeys.MAX_DELAY_SECONDS);
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}, {1:0.##}초", Mode, DelaySeconds);
        }
    }
}
