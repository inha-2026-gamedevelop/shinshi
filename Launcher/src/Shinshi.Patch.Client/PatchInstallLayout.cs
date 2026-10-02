// System
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Shinshi.Patch.Client
{
    /// <summary> 설치 폴더 배치. 모든 경로는 PatchPaths.TryResolveUnderRoot로 루트 아래에서만 만든다 </summary>
    public sealed class PatchInstallLayout
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string VERSIONS_FOLDER  = "versions";
        public const string STAGING_FOLDER   = "staging";
        public const string MANIFESTS_FOLDER = "manifests";
        public const string STATE_FOLDER     = "state";
        public const string CURRENT_FILE     = "current.json";
        public const string CHANNELS_FILE    = "channels.json";
        public const string LOCK_FILE        = "patch.lock";

        /****************************************
        *              Properties
        ****************************************/

        public string InstallRoot      { get; }
        public string ProductPath      { get; }
        public string VersionsPath     { get; }
        public string StagingPath      { get; }
        public string ManifestsPath    { get; }
        public string CurrentFilePath  { get; }
        public string LockFilePath     { get; }
        public string ChannelStatePath { get; }

        /****************************************
        *                Methods
        ****************************************/

        public PatchInstallLayout(string installRoot, string product)
        {
            ArgumentException.ThrowIfNullOrEmpty(installRoot);
            ArgumentException.ThrowIfNullOrEmpty(product);

            InstallRoot      = Path.GetFullPath(installRoot);
            ProductPath      = Resolve(InstallRoot, product);
            VersionsPath     = Resolve(ProductPath, VERSIONS_FOLDER);
            StagingPath      = Resolve(ProductPath, STAGING_FOLDER);
            ManifestsPath    = Resolve(ProductPath, MANIFESTS_FOLDER);
            CurrentFilePath  = Resolve(ProductPath, CURRENT_FILE);
            LockFilePath     = Resolve(ProductPath, LOCK_FILE);
            ChannelStatePath = Resolve(InstallRoot, STATE_FOLDER + PatchPaths.SEPARATOR + CHANNELS_FILE);
        }

        /// <summary> versions/releaseId 폴더 경로. releaseId 형식이 틀리면 false </summary>
        public bool TryGetVersionPath(string? releaseId, [NotNullWhen(true)] out string? path)
        {
            path = null;
            if (!PatchValidation.IsValidReleaseId(releaseId))
            {
                return false;
            }

            return PatchPaths.TryResolveUnderRoot(VersionsPath, releaseId, out path);
        }

        /// <summary> 보관용 매니페스트 봉투 경로 manifests/releaseId.json. releaseId 형식이 틀리면 false </summary>
        public bool TryGetManifestCopyPath(string? releaseId, [NotNullWhen(true)] out string? path)
        {
            path = null;
            if (!PatchValidation.IsValidReleaseId(releaseId))
            {
                return false;
            }

            return PatchPaths.TryResolveUnderRoot(ManifestsPath, releaseId + PatchKeys.JSON_EXTENSION, out path);
        }

        /// <summary> staging 아래 이름 하나의 경로 </summary>
        public bool TryGetStagingPath(string name, [NotNullWhen(true)] out string? path)
        {
            return PatchPaths.TryResolveUnderRoot(StagingPath, name, out path);
        }

        private static string Resolve(string root, string relative)
        {
            if (!PatchPaths.TryResolveUnderRoot(root, relative, out string? path))
            {
                throw new ArgumentException("Install layout path cannot be resolved under the install root.", nameof(root));
            }

            return path;
        }
    }
}
