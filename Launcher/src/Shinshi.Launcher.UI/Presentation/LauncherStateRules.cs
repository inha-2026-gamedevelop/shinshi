namespace Shinshi.Launcher.Presentation
{
    /// <summary> 런처 상태를 큰 버튼의 글자, 동작, 하단 상태 글로 바꾸는 순수 계산 </summary>
    public static class LauncherStateRules
    {
        /****************************************
        *                Methods
        ****************************************/

        public static EPrimaryAction GetPrimaryAction(ELauncherState state)
        {
            switch (state)
            {
                case ELauncherState.NotInstalled:
                    return EPrimaryAction.Install;
                case ELauncherState.UpdateAvailable:
                    return EPrimaryAction.Update;
                case ELauncherState.Ready:
                    return EPrimaryAction.Launch;
                case ELauncherState.Updating:
                case ELauncherState.Repairing:
                    return EPrimaryAction.Cancel;
                case ELauncherState.ConfigError:
                case ELauncherState.CheckFailed:
                    return EPrimaryAction.Retry;
                default:
                    return EPrimaryAction.None;
            }
        }

        public static string GetPrimaryText(ELauncherState state)
        {
            switch (state)
            {
                case ELauncherState.Checking:
                    return "확인 중...";
                case ELauncherState.NotInstalled:
                    return "설치";
                case ELauncherState.UpdateAvailable:
                    return "업데이트";
                case ELauncherState.Ready:
                    return "게임 시작";
                case ELauncherState.Updating:
                case ELauncherState.Repairing:
                    return "취소";
                case ELauncherState.Launching:
                    return "시작 중...";
                case ELauncherState.GameRunning:
                    return "실행 중";
                case ELauncherState.LauncherOutdated:
                    return "런처 업데이트 필요";
                case ELauncherState.ConfigError:
                case ELauncherState.CheckFailed:
                    return "다시 시도";
                default:
                    return string.Empty;
            }
        }

        public static bool IsPrimaryEnabled(ELauncherState state) { return GetPrimaryAction(state) != EPrimaryAction.None; }

        // 진행 중에는 큰 버튼이 취소가 되므로 주황 대신 회색 보조 모양으로 바꾼다
        public static bool IsSecondaryStyle(ELauncherState state) { return GetPrimaryAction(state) == EPrimaryAction.Cancel; }

        public static bool IsOperationRunning(ELauncherState state)
        {
            return (state == ELauncherState.Updating) || (state == ELauncherState.Repairing);
        }

        // 파일 검증 및 복구는 설치가 있고 다른 작업이 없을 때만 고를 수 있다
        public static bool CanRepair(ELauncherState state, bool hasInstall)
        {
            if (!hasInstall)
            {
                return false;
            }

            return (state == ELauncherState.Ready) || (state == ELauncherState.UpdateAvailable) || (state == ELauncherState.CheckFailed);
        }

        public static string GetStatusText(ELauncherState state, string installedVersion)
        {
            switch (state)
            {
                case ELauncherState.Checking:
                    return "확인 중...";
                case ELauncherState.ConfigError:
                    return "설정 오류";
                case ELauncherState.CheckFailed:
                    return "확인 실패";
                case ELauncherState.LauncherOutdated:
                    return "런처 업데이트 필요";
                case ELauncherState.NotInstalled:
                    return "설치 필요";
                case ELauncherState.UpdateAvailable:
                    return "업데이트 필요";
                case ELauncherState.Ready:
                    return (installedVersion.Length == 0) ? "설치됨" : "설치됨 " + installedVersion;
                case ELauncherState.Updating:
                    return "업데이트 중";
                case ELauncherState.Repairing:
                    return "검증 중";
                case ELauncherState.Launching:
                    return "게임 시작 중";
                case ELauncherState.GameRunning:
                    return "실행 중";
                default:
                    return string.Empty;
            }
        }
    }
}
