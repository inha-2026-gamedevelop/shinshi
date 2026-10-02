// Project
using Shinshi.Patch.Client;

namespace Shinshi.Launcher.Presentation
{
    /// <summary> 패치 엔진 오류 코드를 사용자에게 보일 한국어 문장으로 바꾼다 </summary>
    public static class PatchErrorText
    {
        /****************************************
        *                Methods
        ****************************************/

        public static string ToMessage(EPatchClientError error)
        {
            switch (error)
            {
                case EPatchClientError.None:
                    return string.Empty;
                case EPatchClientError.Network:
                    return "패치 서버에 연결할 수 없습니다. 네트워크 연결을 확인해 주세요.";
                case EPatchClientError.DocumentRejected:
                case EPatchClientError.ManifestMismatch:
                case EPatchClientError.SequenceRollback:
                    return "패치 정보 검증에 실패했습니다. 잠시 후 다시 시도해 주세요.";
                case EPatchClientError.LauncherOutdated:
                    return "런처가 오래되었습니다. 새 런처를 받아 주세요.";
                case EPatchClientError.ReleaseConflict:
                    return "설치된 버전 정보가 패치 서버와 맞지 않습니다.";
                case EPatchClientError.NotInstalled:
                    return "게임이 설치되어 있지 않습니다.";
                case EPatchClientError.InvalidPath:
                    return "설치 경로가 올바르지 않습니다.";
                case EPatchClientError.DiskSpace:
                    return "디스크 공간이 부족합니다.";
                case EPatchClientError.HashMismatch:
                    return "파일 검증에 실패했습니다. 다시 시도해 주세요.";
                case EPatchClientError.GameRunning:
                    return "게임이 실행 중입니다. 게임을 종료한 뒤 다시 시도해 주세요.";
                case EPatchClientError.Busy:
                    return "다른 런처가 업데이트 중입니다.";
                case EPatchClientError.Cancelled:
                    return "작업을 취소했습니다.";
                case EPatchClientError.IoFailure:
                    return "파일을 읽거나 쓰지 못했습니다. 다른 프로그램이 설치 폴더를 쓰고 있는지 확인해 주세요.";
                default:
                    return "알 수 없는 오류가 발생했습니다.";
            }
        }

        // 문의할 때 코드를 함께 받을 수 있게 문장 뒤에 영문 코드를 붙인다
        public static string ToMessageWithCode(EPatchClientError error)
        {
            if (error == EPatchClientError.None)
            {
                return string.Empty;
            }

            return ToMessage(error) + " (" + error + ")";
        }

        public static string ToStageText(EPatchStage stage)
        {
            switch (stage)
            {
                case EPatchStage.Checking:
                    return "확인 중";
                case EPatchStage.Preparing:
                    return "준비 중";
                case EPatchStage.Downloading:
                    return "다운로드 중";
                case EPatchStage.Verifying:
                    return "검증 중";
                case EPatchStage.Finalizing:
                    return "마무리 중";
                case EPatchStage.Completed:
                    return "완료";
                default:
                    return string.Empty;
            }
        }
    }
}
