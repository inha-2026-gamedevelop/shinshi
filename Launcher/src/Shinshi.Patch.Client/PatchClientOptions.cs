// System
using System;
using System.IO;

namespace Shinshi.Patch.Client
{
    /// <summary> 설치 위치와 대상 제품, 채널. 런처 버전을 주면 매니페스트의 최소 런처 버전을 확인한다 </summary>
    public sealed class PatchClientOptions
    {
        /****************************************
        *              Properties
        ****************************************/

        public string   InstallRoot     { get; }
        public string   Product         { get; }
        public string   Channel         { get; }
        public Version? LauncherVersion { get; init; }

        // 설치 루트 경로를 받아 남은 바이트를 돌려준다. 비우면 DriveInfo로 잰다. 테스트가 디스크 부족을 흉내 낼 때 쓴다
        public Func<string, long>? FreeSpaceProbe { get; init; }

        /****************************************
        *                Methods
        ****************************************/

        public PatchClientOptions(string installRoot, string product, string channel)
        {
            ArgumentException.ThrowIfNullOrEmpty(installRoot);
            if (!PatchFormat.IsKnownProduct(product))
            {
                throw new ArgumentException("Unknown patch product.", nameof(product));
            }

            if (!PatchFormat.IsKnownChannel(channel))
            {
                throw new ArgumentException("Unknown patch channel.", nameof(channel));
            }

            InstallRoot = Path.GetFullPath(installRoot);
            Product     = product;
            Channel     = channel;
        }
    }
}
