// System
using System;
using System.Globalization;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

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
        // 상태는 예약 전일 수 있어 세지 않는다. 정산을 이미 마친 세션이면 호스트가 정산 결과로 종료 원인을 정한다.
        //
        // 운영 종료(systemctl stop, 일반 종료 요청): 즉시 연결을 끊으면 진행 중인 출정이 잘려 장애 취소로 정산된다. 그래서
        // 드레인 대기 단계에서 새 배정을 막고(드레인 응답 확인), 진행 출정이 마감되기를 기한까지 기다린 뒤 기존 종료 경로로
        // 넘긴다. 기한을 넘기면 정상 정산으로 꾸미지 않고 강제 종료 경고와 전용 종료 코드로 남기고 API의 장애 복구에 맡긴다.
        // 기한은 systemd 중지 한도보다 짧아야 연결 정리와 flush 시간이 남는다

        /****************************************
        *                Fields
        ****************************************/

        // 초, 운영 종료 요청 뒤 진행 출정이 마감되기를 기다리는 상한. 이 뒤에 남는 연결 정리 시간까지 더해도 systemd 한도에 못 미친다
        public const double OPERATOR_DRAIN_MAX_SECONDS = 210.0;

        // 초, 진행 출정이 없을 때 드레인 응답을 기다리는 상한. 응답이 없어도 종료한다(heartbeat 부재로 API가 같은 결론을 낸다)
        public const double OPERATOR_DRAIN_ACK_WAIT_SECONDS = 30.0;

        // 초, 배포 유닛의 TimeoutStopSec와 같아야 하는 값. 시험이 종료 예산과 대조한다
        public const double SYSTEMD_STOP_TIMEOUT_SECONDS = 300.0;

        // 초, 종료 예산과 systemd 한도 사이에 남겨야 하는 여유
        public const double STOP_SAFETY_MARGIN_SECONDS = 30.0;

        // 운영 종료 기한을 넘겨 진행 출정을 두고 내린 종료 코드. 정산을 마친 종료가 아니므로 운영 알림 대상이다
        public const int EXIT_CODE_SHUTDOWN_FORCED = 76;

        private readonly IServerProcessControl _control;
        private readonly ServerPresence        _presence;
        private readonly DungeonSessionHost    _host;
        private readonly Func<bool>            _isSortieOpen;
        private readonly Func<double>          _clock;
        private readonly Action<string>        _warn;

        private EServerShutdownStage _stage               = EServerShutdownStage.Running;
        private int                  _exitCode            = Constants.Network.EXIT_CODE_RECYCLED;
        private double               _returnDeadline      = 0.0;
        private double               _flushUntil          = 0.0;
        private double               _drainDeadline       = 0.0;
        private double               _sessionBoundAt      = 0.0;
        private bool                 _hasSessionBound     = false;
        private double               _operatorDeadline    = 0.0;
        private double               _operatorAckDeadline = 0.0;
        private string               _operatorReason      = null;
        private bool                 _pendingReservation  = false;
        private int                  _duplicateRequests   = 0;

        /****************************************
        *              Properties
        ****************************************/

        public EServerShutdownStage Stage    => _stage;
        public int                  ExitCode => _exitCode;

        // 이미 종료 경로에 들어간 뒤 다시 들어온 운영 종료 요청 수
        public int DuplicateRequestCount => _duplicateRequests;

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> presence와 host는 없을 수 있다(개발 실행, 마을). clock은 단조 증가 초, null이면 게이트웨이 시계 </summary>
        public ServerShutdownController(IServerProcessControl control, ServerPresence presence, DungeonSessionHost host,
            Func<double> clock, Action<string> warn)
            : this(control, presence, host, null, clock, warn)
        {
        }

        /// <summary> isSortieOpen은 운영 종료가 기다려야 하는 출정이 남았는지다. null이면 host의 진실을 쓰고, 시험만 대신 넣는다 </summary>
        public ServerShutdownController(IServerProcessControl control, ServerPresence presence, DungeonSessionHost host,
            Func<bool> isSortieOpen, Func<double> clock, Action<string> warn)
        {
            if (control == null)
            {
                throw new ArgumentNullException(nameof(control));
            }

            _control      = control;
            _presence     = presence;
            _host         = host;
            _isSortieOpen = isSortieOpen ?? IsHostSortieOpen;
            _clock        = clock ?? BackendGateway.MonotonicSeconds;
            _warn         = warn;
        }

        /// <summary> 종료 경로 바깥(부트 실패 뒤 등)에서 바로 내릴 때 부른다, 이미 내려가는 중이면 무시한다 </summary>
        public void Request(int exitCode, double graceSeconds, string reason)
        {
            if ((_stage != EServerShutdownStage.Running) && (_stage != EServerShutdownStage.DrainWaiting))
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

        /// <summary> 운영 종료(일반 종료 요청, 협조 요청 파일)를 받는다. 처음 한 번만 신규 배정을 막고 진행 출정을 기다리며 참을 돌려준다 </summary>
        public bool RequestOperatorShutdown(string reason)
        {
            if (_stage != EServerShutdownStage.Running)
            {
                ++_duplicateRequests;
                return false;
            }

            double now = _clock();
            _stage               = EServerShutdownStage.DrainWaiting;
            _exitCode            = Constants.Network.EXIT_CODE_RECYCLED;
            _operatorDeadline    = now + OPERATOR_DRAIN_MAX_SECONDS;
            _operatorAckDeadline = now + OPERATOR_DRAIN_ACK_WAIT_SECONDS;
            _operatorReason      = "운영 종료 " + reason;

            // 드레인 응답은 직전 상태를 알려 주지 않는다. 예약된 뒤 아직 아무도 들어오지 않은 던전은 마지막으로 받은 상태로 가린다
            _pendingReservation = (_host != null) && (_host.SessionId == null) && (_presence != null)
                && ((_presence.InstanceState == GameSessionCodes.InstanceState.RESERVED)
                    || (_presence.InstanceState == GameSessionCodes.InstanceState.ACTIVE));
            _presence?.RequestDrain();
            _warn?.Invoke($"[Shutdown] 운영 종료 요청을 받아 신규 배정을 멈추고 진행 출정을 기다린다, reason={reason}");
            ServerJsonLogHandler.LogEvent(ServerLogLine.LEVEL_WARN, ServerLogLine.EVENT_DRAIN_REQUESTED, null, null,
                FormatExitData(_exitCode));
            return true;
        }

        /// <summary> Unity 종료 요청 처리기가 부른다. 종료 경로가 끝난 뒤(Exited)에만 종료를 허용하고, 그 전에는 운영 종료로 바꿔 막는다 </summary>
        public bool AllowsQuit(string reason)
        {
            if (_stage == EServerShutdownStage.Exited)
            {
                return true;
            }

            RequestOperatorShutdown(reason);
            return false;
        }

        /// <summary> 매 프레임 부른다 </summary>
        public void Tick()
        {
            double now = _clock();
            switch (_stage)
            {
                case EServerShutdownStage.Running:
                    TickFaults(now);
                    break;
                case EServerShutdownStage.DrainWaiting:
                    TickDrainWaiting(now);
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

        // 서버 풀 존재 상실과 출정 종료를 감지해 종료 경로로 넘긴다, 운영 종료를 기다리는 중에도 같은 규칙이다. 넘겼으면 참
        private bool TickFaults(double now)
        {
            if ((_presence != null) && (_presence.IsLost))
            {
                Request(Constants.Network.EXIT_CODE_PRESENCE_LOST, Constants.Network.RECYCLE_FAULT_GRACE_SECONDS,
                    "서버 풀 존재 상실");
                return true;
            }

            if (_host == null)
            {
                return false;
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
                return false;
            }

            ESortieTerminalReason reason = _host.TerminalReason;
            Request(ExitCodeFor(reason), GraceFor(reason), "출정 종료 " + reason);
            return true;
        }

        private void TickDrainWaiting(double now)
        {
            if (TickFaults(now))
            {
                return;
            }

            if (_isSortieOpen())
            {
                if (now >= _operatorDeadline)
                {
                    _warn?.Invoke("[Shutdown] 진행 출정이 기한 안에 마감되지 않아 강제로 내린다. 정상 정산으로 보지 않고 API 장애 복구에 맡긴다");
                    ServerJsonLogHandler.LogEvent(ServerLogLine.LEVEL_ERROR, ServerLogLine.EVENT_SHUTDOWN_FORCED, null, null,
                        FormatExitData(EXIT_CODE_SHUTDOWN_FORCED));
                    Request(EXIT_CODE_SHUTDOWN_FORCED, Constants.Network.RECYCLE_FAULT_GRACE_SECONDS, _operatorReason + ", 기한 초과");
                }

                return;
            }

            // 새 배정이 멈췄다는 응답을 받기 전에는 내리지 않는다. 받지 못해도 기한이 지나면 내린다
            bool drainAcknowledged = (_presence == null) || (_presence.IsDrainCompleted);
            if ((!drainAcknowledged) && (now < _operatorAckDeadline))
            {
                return;
            }

            Request(Constants.Network.EXIT_CODE_RECYCLED, Constants.Network.RECYCLE_FAULT_GRACE_SECONDS, _operatorReason);
        }

        // 마을은 host가 없어 기다릴 출정이 없다. 던전은 시작된 뒤 끝나지 않은 출정과 아직 아무도 들어오지 않은 예약을 기다린다
        private bool IsHostSortieOpen()
        {
            if ((_host == null) || (_host.IsTerminal))
            {
                return false;
            }

            return (_host.SessionId != null) || (_pendingReservation);
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
