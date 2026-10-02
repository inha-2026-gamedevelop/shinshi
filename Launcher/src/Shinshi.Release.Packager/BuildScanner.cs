// System
using System;
using System.Collections.Generic;
using System.IO;

// Project
using Shinshi.Patch;

namespace Shinshi.Release
{
    // 빌드 폴더의 파일 전체를 나열한다
    internal static class BuildScanner
    {
        /****************************************
        *                Fields
        ****************************************/

        private const char MANIFEST_SEPARATOR = '/';

        // Unity가 빌드 옆에 남기는 디버그 심볼 폴더. 이름 그대로 배포하면 안 되므로 빌드 루트에서만 건너뛴다
        private static readonly string[] G_DoNotShipSuffixes =
        {
            "_BackUpThisFolder_ButDontShipItWithYourGame",
            "_BurstDebugInformation_DoNotShip"
        };

        /****************************************
        *                Methods
        ****************************************/

        // 재분석 지점(심볼릭 링크, 정션)은 폴더든 파일이든 거부한다. 빌드 폴더 밖의 파일이
        // 게시물에 섞이는 것을 막고, 해시한 파일과 복사하는 파일이 달라지는 틈을 없앤다
        public static List<ScannedFile> Scan(string buildFolder)
        {
            DirectoryInfo root = new DirectoryInfo(buildFolder);
            if (!root.Exists)
            {
                throw new PackagerException(PackagerExitCodes.INVALID_BUILD, "Build folder does not exist: " + buildFolder);
            }

            if (root.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new PackagerException(PackagerExitCodes.INVALID_BUILD, "Build folder is a symbolic link or reparse point.");
            }

            List<ScannedFile> files = new List<ScannedFile>();
            Collect(root, root.FullName, files);

            // 같은 빌드면 같은 매니페스트가 나오도록 경로 순서를 고정한다
            files.Sort((left, right) => string.CompareOrdinal(left.RelativePath, right.RelativePath));
            return files;
        }

        private static void Collect(DirectoryInfo directory, string rootFullName, List<ScannedFile> files)
        {
            foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos())
            {
                string relative = Path.GetRelativePath(rootFullName, entry.FullName).Replace(Path.DirectorySeparatorChar, MANIFEST_SEPARATOR);
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new PackagerException(PackagerExitCodes.INVALID_BUILD,
                        "Symbolic links and reparse points are not allowed in a build: " + relative);
                }

                if (entry is DirectoryInfo subDirectory)
                {
                    if (IsDoNotShipFolder(subDirectory, rootFullName))
                    {
                        continue;
                    }

                    Collect(subDirectory, rootFullName, files);
                    continue;
                }

                files.Add(new ScannedFile(entry.FullName, relative, ((FileInfo)entry).Length));
            }
        }

        private static bool IsDoNotShipFolder(DirectoryInfo directory, string rootFullName)
        {
            if (!string.Equals(directory.Parent?.FullName, rootFullName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            for (int i = 0; i < G_DoNotShipSuffixes.Length; ++i)
            {
                if (directory.Name.EndsWith(G_DoNotShipSuffixes[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    // 빌드 폴더의 파일 하나
    internal sealed class ScannedFile
    {
        /****************************************
        *              Properties
        ****************************************/

        public string FullPath     { get; }
        public string RelativePath { get; }
        public long   Size         { get; }

        /****************************************
        *                Methods
        ****************************************/

        public ScannedFile(string fullPath, string relativePath, long size)
        {
            FullPath     = fullPath;
            RelativePath = relativePath;
            Size         = size;
        }
    }
}
