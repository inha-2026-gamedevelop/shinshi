// System
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Shinshi.Patch
{
    /// <summary> 릴리스 하나의 파일 목록과 빌드 신원, 매니페스트 봉투의 payload </summary>
    [DataContract]
    public sealed class ReleaseManifest
    {
        /****************************************
        *                Fields
        ****************************************/

        [DataMember(Name = "formatVersion")]      private int                 _formatVersion;
        [DataMember(Name = "product")]            private string?             _product;
        [DataMember(Name = "releaseId")]          private string?             _releaseId;
        [DataMember(Name = "channel")]            private string?             _channel;
        [DataMember(Name = "buildId")]            private string?             _buildId;
        [DataMember(Name = "buildKind")]          private string?             _buildKind;
        [DataMember(Name = "displayVersion")]     private string?             _displayVersion;

        // "major.minor.patch", 이보다 낮은 런처는 이 릴리스를 설치하지 않는다
        [DataMember(Name = "minLauncherVersion")] private string?             _minLauncherVersion;
        [DataMember(Name = "createdAtUnixMs")]    private long                _createdAtUnixMs;
        [DataMember(Name = "files")]              private List<ManifestFile>? _files;

        /****************************************
        *              Properties
        ****************************************/

        public int                         FormatVersion      => _formatVersion;
        public string                      Product            => _product ?? string.Empty;
        public string                      ReleaseId          => _releaseId ?? string.Empty;
        public string                      Channel            => _channel ?? string.Empty;
        public string                      BuildId            => _buildId ?? string.Empty;
        public string                      BuildKind          => _buildKind ?? string.Empty;
        public string                      DisplayVersion     => _displayVersion ?? string.Empty;
        public string                      MinLauncherVersion => _minLauncherVersion ?? string.Empty;
        public long                        CreatedAtUnixMs    => _createdAtUnixMs;
        public IReadOnlyList<ManifestFile> Files              => (IReadOnlyList<ManifestFile>?)_files ?? Array.Empty<ManifestFile>();

        /****************************************
        *                Methods
        ****************************************/

        public ReleaseManifest(string product, string releaseId, string channel, string buildId, string buildKind,
            string displayVersion, string minLauncherVersion, long createdAtUnixMs, IEnumerable<ManifestFile> files)
        {
            ArgumentNullException.ThrowIfNull(files);

            _formatVersion      = PatchFormat.FORMAT_VERSION;
            _product            = product;
            _releaseId          = releaseId;
            _channel            = channel;
            _buildId            = buildId;
            _buildKind          = buildKind;
            _displayVersion     = displayVersion;
            _minLauncherVersion = minLauncherVersion;
            _createdAtUnixMs    = createdAtUnixMs;
            _files              = new List<ManifestFile>(files);
        }

        private ReleaseManifest() { }

        /// <summary> 모든 파일 크기의 합 </summary>
        public long GetTotalSize()
        {
            long total = 0;
            foreach (ManifestFile file in Files)
            {
                total = checked(total + file.Size);
            }

            return total;
        }
    }
}
