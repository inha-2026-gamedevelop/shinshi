// System
using System;
using System.Globalization;
using System.Net;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 지표 내보내기의 수신 주소, 포트, 메시지 종류별 집계 여부. 포트가 0이면 꺼진 설정이다 </summary>
    public readonly struct ServerMetricsSettings
    {
        /****************************************
        *                Fields
        ****************************************/

        private const int    MAX_PORT   = 65535;
        private const string SWITCH_ON  = "1";
        private const string SWITCH_OFF = "0";

        private readonly IPAddress _bindAddress;

        public int       Port          { get; }
        public bool      MessageDetail { get; }
        public bool      Enabled       => Port > 0;
        public IPAddress BindAddress   => _bindAddress ?? IPAddress.Loopback;

        /****************************************
        *                Methods
        ****************************************/

        public ServerMetricsSettings(int port, IPAddress bindAddress, bool messageDetail)
        {
            Port          = port;
            _bindAddress  = bindAddress;
            MessageDetail = messageDetail;
        }

        /// <summary> 실행 인자가 환경 변수보다 우선한다. 포트가 없으면 꺼진 설정으로 참을 돌려준다 </summary>
        public static bool TryParse(string[] args, Func<string, string> readEnvironment, out ServerMetricsSettings settings,
            out string error)
        {
            settings = default;
            if (!ServerLaunchConfig.TryReadValue(args, readEnvironment, Constants.Network.ARG_METRICS_PORT,
                Constants.Network.ENV_METRICS_PORT, out string portText, out error))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(portText))
            {
                return true;
            }

            if ((!int.TryParse(portText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int port)) || (port > MAX_PORT))
            {
                error = $"{Constants.Network.ENV_METRICS_PORT} ({Constants.Network.ARG_METRICS_PORT}): 0~65535 정수가 필요하다";
                return false;
            }

            if (port == 0)
            {
                return true;
            }

            if (!ServerLaunchConfig.TryReadValue(args, readEnvironment, Constants.Network.ARG_METRICS_BIND_ADDRESS,
                Constants.Network.ENV_METRICS_BIND_ADDRESS, out string bindText, out error))
            {
                return false;
            }

            // 호스트 이름은 받지 않는다. 이름 풀이 결과에 따라 외부 주소로 열리는 실수를 막는다
            string address = string.IsNullOrWhiteSpace(bindText) ? Constants.Network.METRICS_DEFAULT_BIND_ADDRESS : bindText.Trim();
            if (!IPAddress.TryParse(address, out IPAddress bindAddress))
            {
                error = $"{Constants.Network.ENV_METRICS_BIND_ADDRESS} ({Constants.Network.ARG_METRICS_BIND_ADDRESS}): IP 주소가 필요하다";
                return false;
            }

            if (!ServerLaunchConfig.TryReadValue(args, readEnvironment, Constants.Network.ARG_METRICS_MESSAGE_DETAIL,
                Constants.Network.ENV_METRICS_MESSAGE_DETAIL, out string detailText, out error))
            {
                return false;
            }

            if (!TryParseSwitch(detailText, out bool detail))
            {
                error = $"{Constants.Network.ENV_METRICS_MESSAGE_DETAIL} ({Constants.Network.ARG_METRICS_MESSAGE_DETAIL}): 0 또는 1이 필요하다";
                return false;
            }

            settings = new ServerMetricsSettings(port, bindAddress, detail);
            return true;
        }

        private static bool TryParseSwitch(string text, out bool value)
        {
            value = false;
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            string trimmed = text.Trim();
            if (trimmed == SWITCH_ON)
            {
                value = true;
                return true;
            }

            return trimmed == SWITCH_OFF;
        }
    }
}
