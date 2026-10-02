// System
using System;

// Project
using Shinshi.Patch;

namespace Shinshi.Launcher.Config
{
    /// <summary> 검사를 통과한 런처 설정 </summary>
    public sealed class LauncherSettings
    {
        /****************************************
        *              Properties
        ****************************************/

        public string        PatchSource  { get; }
        public bool          IsHttpSource { get; }
        public string        Channel      { get; }
        public TrustedKeySet Keys         { get; }
        public string        InstallRoot  { get; }
        public bool          TeamBuild    { get; }

        // 없으면 로그인과 게임 시작을 막는다. 게임에도 같은 주소를 넘긴다
        public Uri?          ApiBaseUri   { get; }

        // 회원가입 링크를 여는 홈페이지. 없으면 링크를 숨긴다
        public Uri?          WebsiteUri   { get; }

        /****************************************
        *                Methods
        ****************************************/

        public LauncherSettings(string patchSource, bool isHttpSource, string channel, TrustedKeySet keys, string installRoot,
            bool teamBuild, Uri? apiBaseUri, Uri? websiteUri)
        {
            PatchSource  = patchSource;
            IsHttpSource = isHttpSource;
            Channel      = channel;
            Keys         = keys;
            InstallRoot  = installRoot;
            TeamBuild    = teamBuild;
            ApiBaseUri   = apiBaseUri;
            WebsiteUri   = websiteUri;
        }
    }
}
