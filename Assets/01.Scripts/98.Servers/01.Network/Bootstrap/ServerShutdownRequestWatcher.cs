// System
using System;
using System.Globalization;
using System.IO;

namespace Shinshi.Network
{
    /// <summary> 협조 종료 요청 파일을 읽고 지우는 파일 시스템 경계, 시험이 대체한다 </summary>
    public interface IShutdownRequestFileSystem
    {
        bool DirectoryExists(string path);

        bool FileExists(string path);

        /// <summary> 읽기 실패는 예외로 알린다 </summary>
        string ReadAllText(string path);

        /// <summary> 삭제 실패는 예외로 알린다 </summary>
        void Delete(string path);
    }

    /// <summary> 운영 체제 파일 시스템을 쓰는 기본 구현 </summary>
    public sealed class SystemShutdownRequestFileSystem : IShutdownRequestFileSystem
    {
        public bool DirectoryExists(string path)
        {
            return Directory.Exists(path);
        }

        public bool FileExists(string path)
        {
            return File.Exists(path);
        }

        public string ReadAllText(string path)
        {
            return File.ReadAllText(path);
        }

        public void Delete(string path)
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// systemd ExecStop이 만든 협조 종료 요청 파일을 주기적으로 확인한다. 메인 스레드에서만 부른다.
    /// 파일 내용은 종료할 프로세스의 PID 한 줄이다. 자기 PID와 다른 파일은 이전 프로세스가 남긴 것이라 무시하고 지운다.
    /// 요청 파일은 서비스 전용 RuntimeDirectory(0700) 안에만 두며 인증을 우회하는 공개 경로가 아니다
    /// </summary>
    public sealed class ServerShutdownRequestWatcher
    {
        /****************************************
        *                Fields
        ****************************************/

        // 요청 파일 경로를 알리는 환경변수, 없으면 파일을 감시하지 않는다
        public const string ENV_REQUEST_FILE = "SHINSHI_SHUTDOWN_REQUEST_FILE";

        // 초, 파일 확인 주기
        public const double POLL_INTERVAL_SECONDS = 1.0;

        // 초, 같은 종류의 경고를 다시 남기기까지의 간격
        public const double WARN_INTERVAL_SECONDS = 60.0;

        // 쓰는 도중에 읽어 PID를 해석하지 못한 파일을 지우기까지 연속으로 허용하는 확인 횟수
        public const int MAX_UNPARSABLE_POLLS = 5;

        // 이보다 긴 내용은 PID 한 줄이 아니다
        public const int MAX_CONTENT_LENGTH = 32;

        private readonly string                     _path;
        private readonly int                        _ownPid;
        private readonly IShutdownRequestFileSystem _fileSystem;
        private readonly Func<double>               _clock;
        private readonly Action<string>             _warn;

        private double _nextPollAt      = 0.0;
        private double _nextWarnAt      = 0.0;
        private int    _unparsablePolls = 0;
        private bool   _requested       = false;

        /****************************************
        *              Properties
        ****************************************/

        public bool IsRequested => _requested;

        /****************************************
        *                Methods
        ****************************************/

        public ServerShutdownRequestWatcher(string path, int ownPid, IShutdownRequestFileSystem fileSystem,
            Func<double> clock, Action<string> warn)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("요청 파일 경로가 비었다", nameof(path));
            }

            if (fileSystem == null)
            {
                throw new ArgumentNullException(nameof(fileSystem));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _path       = path;
            _ownPid     = ownPid;
            _fileSystem = fileSystem;
            _clock      = clock;
            _warn       = warn;
        }

        /// <summary> 환경변수 값으로 감시자를 만든다, 값이 없거나 절대 경로가 아니면 null이다 </summary>
        public static ServerShutdownRequestWatcher TryCreate(string envValue, int ownPid, IShutdownRequestFileSystem fileSystem,
            Func<double> clock, Action<string> warn)
        {
            if (string.IsNullOrWhiteSpace(envValue))
            {
                return null;
            }

            string path = envValue.Trim();
            if (!Path.IsPathRooted(path))
            {
                warn?.Invoke($"[Shutdown] {ENV_REQUEST_FILE} 값이 절대 경로가 아니라 협조 종료 파일을 감시하지 않는다");
                return null;
            }

            ServerShutdownRequestWatcher watcher = new ServerShutdownRequestWatcher(path, ownPid, fileSystem, clock, warn);
            watcher.CheckDirectory();
            return watcher;
        }

        /// <summary> 종료 요청을 처음 확인한 시점에 한 번만 참이다. 이미 요청했거나 확인 주기 전이면 거짓이다 </summary>
        public bool Poll()
        {
            if (_requested)
            {
                return false;
            }

            double now = _clock();
            if (now < _nextPollAt)
            {
                return false;
            }

            _nextPollAt = now + POLL_INTERVAL_SECONDS;

            string content;
            try
            {
                if (!_fileSystem.FileExists(_path))
                {
                    _unparsablePolls = 0;
                    return false;
                }

                content = _fileSystem.ReadAllText(_path);
            }
            catch (Exception e)
            {
                WarnRateLimited(now, $"[Shutdown] 협조 종료 요청 파일을 읽지 못했다, file={Path.GetFileName(_path)}, error={e.GetType().Name}");
                return false;
            }

            string trimmed = (content == null) ? string.Empty : content.Trim();
            int    pid     = 0;
            bool   parsed  = (trimmed.Length > 0) && (trimmed.Length <= MAX_CONTENT_LENGTH)
                && int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out pid);
            if (!parsed)
            {
                // 요청을 쓰는 도중이면 곧 완성된다, 계속 해석하지 못하면 쓰레기로 보고 지운다
                ++_unparsablePolls;
                if (_unparsablePolls >= MAX_UNPARSABLE_POLLS)
                {
                    _unparsablePolls = 0;
                    WarnRateLimited(now, $"[Shutdown] 해석할 수 없는 협조 종료 요청 파일을 지운다, file={Path.GetFileName(_path)}");
                    TryDelete(now);
                }

                return false;
            }

            _unparsablePolls = 0;
            if (pid != _ownPid)
            {
                WarnRateLimited(now, $"[Shutdown] 다른 프로세스를 가리키는 오래된 협조 종료 요청 파일을 지운다, file={Path.GetFileName(_path)}");
                TryDelete(now);
                return false;
            }

            _requested = true;
            TryDelete(now);
            return true;
        }

        // 시작할 때 요청 파일이 들어갈 디렉터리가 없으면 한 번 알린다, 요청 파일 정리 권한은 지울 때 다시 확인한다
        private void CheckDirectory()
        {
            try
            {
                string directory = Path.GetDirectoryName(_path);
                if ((!string.IsNullOrEmpty(directory)) && (!_fileSystem.DirectoryExists(directory)))
                {
                    _warn?.Invoke($"[Shutdown] 협조 종료 요청 디렉터리가 아직 없다, file={Path.GetFileName(_path)}");
                }
            }
            catch (Exception e)
            {
                _warn?.Invoke($"[Shutdown] 협조 종료 요청 디렉터리를 확인하지 못했다, error={e.GetType().Name}");
            }
        }

        private void TryDelete(double now)
        {
            try
            {
                _fileSystem.Delete(_path);
            }
            catch (Exception e)
            {
                WarnRateLimited(now, $"[Shutdown] 협조 종료 요청 파일을 지우지 못했다, file={Path.GetFileName(_path)}, error={e.GetType().Name}");
            }
        }

        private void WarnRateLimited(double now, string message)
        {
            if (now < _nextWarnAt)
            {
                return;
            }

            _nextWarnAt = now + WARN_INTERVAL_SECONDS;
            _warn?.Invoke(message);
        }
    }
}
