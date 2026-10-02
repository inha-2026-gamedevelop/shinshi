// System
using System.Runtime.Serialization;

namespace Shinshi.Contracts
{
    /// <summary> 게임이 세션을 받았다는 수신 확인, 받지 못하면 런처는 게임을 끝내고 발급한 세션 폐기를 시도한다 </summary>
    [DataContract]
    public sealed class LauncherHandoffAck
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "protocolVersion")] private int _protocolVersion;

        // 런처가 파이프 클라이언트 PID와 한 번 더 대조한다
        [DataMember(Name = "gamePid")]         private int _gamePid;

        /****************************************
        *              Properties
        ****************************************/

        public int ProtocolVersion => _protocolVersion;
        public int GamePid         => _gamePid;

        /****************************************
        *                Methods
        ****************************************/

        public LauncherHandoffAck(int protocolVersion, int gamePid)
        {
            _protocolVersion = protocolVersion;
            _gamePid         = gamePid;
        }

        private LauncherHandoffAck() { }
    }
}
