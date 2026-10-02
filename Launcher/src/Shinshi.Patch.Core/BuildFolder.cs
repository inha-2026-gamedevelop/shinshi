// System
using System.Collections.Generic;
using System.IO;

namespace Shinshi.Patch
{
    // Unity 플레이어 빌드는 실행 파일 옆에 "<이름>_Data" 폴더 하나를 둔다
    internal static class BuildFolder
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string DATA_FOLDER_PATTERN = "*_Data";

        /****************************************
        *                Methods
        ****************************************/

        // 빌드 폴더 바로 아래 *_Data 폴더들에서 relativeFile이 있는 파일의 전체 경로를 모은다
        public static List<string> FindInDataFolders(string buildFolder, string relativeFile)
        {
            List<string> found = new List<string>();
            if (!Directory.Exists(buildFolder))
            {
                return found;
            }

            foreach (string dataFolder in Directory.GetDirectories(buildFolder, DATA_FOLDER_PATTERN, SearchOption.TopDirectoryOnly))
            {
                string candidate = Path.Combine(dataFolder, relativeFile);
                if (File.Exists(candidate))
                {
                    found.Add(candidate);
                }
            }

            return found;
        }
    }
}
