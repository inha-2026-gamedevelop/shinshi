// System
using System;
using System.Globalization;

// 프로젝트 내부
using Shinshi.Common;

namespace Shinshi.Network
{
    /// <summary> 출정이 끝났거나 서버 풀 존재를 잃은 프로세스를 내린다, 드레인 -> 남은 연결 정리 -> 종료 순서다 </summary>
    public sealed class ServerShutdownController
    {
        // 던전 프로세스는 출정 하나만 맡는다. 끝난 세션을 출정 번호(epoch)로 되돌려 다음 파티에 다시 쓰면 명단, 보존소,
        // 레지스트리의 닫힘 기록, 전투 개체, 정적 설치 상태 중 하나만 남아도 이전 출정이 섞인다. 그래서 프로세스를 내리고
        // supervisor가 새 bootId로 다시 띄운다.
        //
        // 드레인은 종료를 결정한 즉시 보낸다. 정산 직후 API가 이 인스턴스를 IDLE로 되돌리면 곧 내려갈 프로세스에 새 파티가
        // 배정될 수 있어서다. 연결은 결과 화면을 볼 시간을 준 뒤 끊고, 끊김 통지가 나갈 시간을 더 준 뒤 끝낸다.
        //
        // 출정 종료 감지: 출정을 받은 뒤 보낸 heartbeat가 IDLE이나 이 프로세스가 요청하지 않은 DRAINING을 받으면 API가 예약을
        // 풀었거나 정산을 마친 것이다(정산, 예약 해제 뒤 API는 인스턴스를 Draining으로 둔다). 받은 시각 이전에 보낸 heartbeat의
        // 상태는 예약 전일 수 있어 세지 않는다. 정산을 이미 마친 세션이면 호스트가 정산 결과로 종료 원인을 정한다

        /****************************************
        *                Fields
        ****************************************/

        private readonly IServerProcessControl _control;
        private readonly ServerPresence        _presence;
        private readonly DungeonSessionHost    _host;
        private readonly Func<double>          _clock;
        private readonly Action<string>        _warn;

        private EServerShutdownStage _stage           = EServerShutdownStage.Running;
        private int                  _exitCode        = Constants.Network.EXIT_CODE_RECYCLED;
        private double               _returnDeadline  = 0.0;
        private double               _flushUntil      = 0.0;
        private double               _drainDeadline   = 0.0;
        private double               _sessionBoundAt  = 0.0;
        private bool                 _hasSessionBound = false;

        /****************************************
        *              Properties
        ****************************************/

        public EServerShutdownStage Stage    => _stage;
        public int                  ExitCode => _exitCode;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> presence와 host는 없을 수 있다(개발 실행, 마을). clock은 단조 증가 초, null이면 게이트웨이 시계 </summary>
        public ServerShutdownController(IServerProcessControl control, ServerPresence presence, DungeonSessionHost host,
            Func<double> clock, Action<string> warn)
        {
            if (control == null)
            {
                throw new ArgumentNullException(nameof(control));
            }

            _control  = control;
            _presence = presence;
            _host     = host;
            _clock    = clock ?? BackendGateway.MonotonicSeconds;
            _warn     = warn;
        }

        /// <summary> 종료 경로 바깥(부트 실패 뒤 등)에서 바로 내릴 때 부른다, 이미 진행 중이면 무시한다 </summary>
        public void Request(int exitCode, double graceSeconds, string reason)
        {
            if (_stage != EServerShutdownStage.Running)
            {
                return;
            }

            double now = _clock();
            _stage          = EServerShutdownStage.Returning;
            _exitCode       = exitCode;
            _returnDeadline = now + graceSeconds;
            _drainDeadline  = now + Constants.Network.RECYCLE_DRAIN_WAIT_SECONDS;
            _presence?.RequestDrain();
            _warn?.Invoke($"[Shutdown] 프로세스를 내린다, reason={reason}, exitCode={exitCode}");
            ServerJsonLogHandler.LogEvent(ServerLogLine.LEVEL_WARN, ServerLogLine.EVENT_DRAIN_REQUESTED, null, null,
                FormatExitData(exitCode));
        }

        /// <summary> 매 프레임 부른다 </summary>
        public void Tick()
        {
            double now = _clock();
            switch (_stage)
            {
                case EServerShutdownStage.Running:
                    TickRunning(now);
                    break;
                case EServerShutdownStage.Returning:
                    if ((_control.ConnectionCount == 0) || (now >= _returnDeadline))
                    {
                        _control.DisconnectAll();
                        _flushUntil = now + Constants.Network.RECYCLE_DISCONNECT_FLUSH_SECONDS;
                        _stage      = EServerShutdownStage.Flushing;
                    }

                    break;
                case EServerShutdownStage.Flushing:
                    if ((now >= _flushUntil) && ((_presence == null) || (_presence.IsDrainCompleted) || (now >= _drainDeadline)))
                    {
                        // 드레인 응답을 받지 못하고 기한으로 내리는 경우만 강제 종료로 남긴다. 정상 마감 근거가 없어서다
                        if ((_presence != null) && (!_presence.IsDrainCompleted))
                        {
                            ServerJsonLogHandler.LogEvent(ServerLogLine.LEVEL_ERROR, ServerLogLine.EVENT_SHUTDOWN_FORCED, null,
                                null, FormatExitData(_exitCode));
                        }

                        _stage = EServerShutdownStage.Exited;
                        _control.Quit(_exitCode);
                    }

                    break;
            }
        }

        private void TickRunning(double now)
        {
            if ((_presence != null) && (_presence.IsLost))
            {
                Request(Constants.Network.EXIT_CODE_PRESENCE_LOST, Constants.Network.RECYCLE_FAULT_GRACE_SECONDS,
                    "서버 풀 존재 상실");
                return;
            }

            if (_host == null)
            {
                return;
            }

            if ((!_hasSessionBound) && (_host.SessionId != null))
            {
                _hasSessionBound = true;
                _sessionBoundAt  = now;
            }

            if ((_hasSessionBound) && (_presence != null) && (!_host.IsTerminal) && (_presence.ReportsSortieEndedSince(_sessionBoundAt)))
            {
                _host.OnReservationReleased();
            }

            if (!_host.IsTerminal)
            {
                return;
            }

            ESortieTerminalReason reason = _host.TerminalReason;
            Request(ExitCodeFor(reason), GraceFor(reason), "출정 종료 " + reason);
        }

        private static string FormatExitData(int exitCode)
        {
            return "{\"exitCode\":" + exitCode.ToString(CultureInfo.InvariantCulture) + "}";
        }

        /// <summary> 출정 종료 원인별 종료 코드, 불변식 위반만 오류 코드다 </summary>
        public static int ExitCodeFor(ESortieTerminalReason reason)
        {
            return (reason == ESortieTerminalReason.InvariantViolation)
                ? Constants.Network.EXIT_CODE_INVARIANT
                : Constants.Network.EXIT_CODE_RECYCLED;
        }

        /// <summary> 정상 정산 뒤에는 결과 화면을 볼 시간을 주고, 그 밖에는 짧게 준다 </summary>
        public static double GraceFor(ESortieTerminalReason reason)
        {
            return ((reason == ESortieTerminalReason.Settled) || (reason == ESortieTerminalReason.SettlementAbandoned))
                ? Constants.Network.RECYCLE_RETURN_GRACE_SECONDS
                : Constants.Network.RECYCLE_FAULT_GRACE_SECONDS;
        }
    }
}
