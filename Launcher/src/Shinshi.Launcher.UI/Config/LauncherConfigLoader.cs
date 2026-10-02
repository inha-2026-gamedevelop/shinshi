// System
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;

// Third-party
using Newtonsoft.Json;

// Project
using Shinshi.Contracts;
using Shinshi.Launcher.Diagnostics;
using Shinshi.Patch;

namespace Shinshi.Launcher.Config
{
    /// <summary> launcher.json을 읽고 검사한다. 실패하면 화면에 보일 한국어 이유를 돌려준다 </summary>
    public static class LauncherConfigLoader
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string CONFIG_FILE_NAME = "launcher.json";

        // 설정 파일은 몇 줄짜리라 이보다 크면 잘못된 파일로 본다
        private const long   MAX_CONFIG_BYTES    = 64 * 1024;
        private const string DEFAULT_ROOT_FOLDER = "Shinshi";
        private const string HTTP_PREFIX         = "http://";
        private const string HTTPS_PREFIX        = "https://";

        /****************************************
        *                Methods
        ****************************************/

        public static string GetConfigPath() { return Path.Combine(AppContext.BaseDirectory, CONFIG_FILE_NAME); }

        public static bool TryLoad(string path, [NotNullWhen(true)] out LauncherSettings? settings, out string error)
        {
            settings = null;
            error    = string.Empty;

            if (!File.Exists(path))
            {
                error = "런처 설정 파일(launcher.json)이 없습니다. launcher.example.json을 참고해 만들어 주세요.";
                return false;
            }

            LauncherConfigDocument? document;
            try
            {
                FileInfo info = new FileInfo(path);
                if (info.Length > MAX_CONFIG_BYTES)
                {
                    error = "런처 설정 파일이 너무 큽니다.";
                    return false;
                }

                document = JsonConvert.DeserializeObject<LauncherConfigDocument>(File.ReadAllText(path), ContractJson.CreateSettings());
            }
            catch (JsonException exception)
            {
                LauncherLog.Warn("launcher.json parse failed: " + exception.Message);
                error = "런처 설정 파일 형식이 올바르지 않습니다.";
                return false;
            }
            catch (IOException exception)
            {
                LauncherLog.Warn("launcher.json read failed: " + exception.Message);
                error = "런처 설정 파일을 읽지 못했습니다.";
                return false;
            }
            catch (UnauthorizedAccessException exception)
            {
                LauncherLog.Warn("launcher.json access denied: " + exception.Message);
                error = "런처 설정 파일을 읽을 권한이 없습니다.";
                return false;
            }

            if (document == null)
            {
                error = "런처 설정 파일이 비어 있습니다.";
                return false;
            }

            return TryResolve(document, out settings, out error);
        }

        private static bool TryResolve(LauncherConfigDocument document, [NotNullWhen(true)] out LauncherSettings? settings,
            out string error)
        {
            settings = null;
            error    = string.Empty;

            string patchSource = document.PatchSource?.Trim() ?? string.Empty;
            if (patchSource.Length == 0)
            {
                error = "런처 설정에 패치 서버(patchSource)가 없습니다.";
                return false;
            }

            bool isHttp = patchSource.StartsWith(HTTP_PREFIX, StringComparison.OrdinalIgnoreCase)
                || patchSource.StartsWith(HTTPS_PREFIX, StringComparison.OrdinalIgnoreCase);
            if (isHttp && !Uri.TryCreate(patchSource, UriKind.Absolute, out Uri? _))
            {
                error = "런처 설정의 패치 서버 주소가 올바르지 않습니다.";
                return false;
            }

            string channel = document.Channel?.Trim() ?? PatchFormat.CHANNEL_STABLE;
            if (!PatchFormat.IsKnownChannel(channel))
            {
                error = "런처 설정의 채널(channel)은 internal 또는 stable이어야 합니다.";
                return false;
            }

            // 외부 빌드의 런처는 stable만 쓴다. 설정 파일을 고쳐 내부 채널을 받지 못하게 여기서 막는다
            if (!document.TeamBuild && (channel != PatchFormat.CHANNEL_STABLE))
            {
                LauncherLog.Warn("Non-team launcher requested channel '" + channel + "', using stable.");
                channel = PatchFormat.CHANNEL_STABLE;
            }

            // TrustedKeySet은 같은 키 ID가 두 번 오면 예외를 던지므로 여기서 중복을 걸러 낸다
            Dictionary<string, PatchPublicKey> keys = new Dictionary<string, PatchPublicKey>(StringComparer.Ordinal);
            if (document.PublicKeys != null)
            {
                foreach (string text in document.PublicKeys)
                {
                    if (PatchPublicKey.TryFromBase64(text, out PatchPublicKey? key))
                    {
                        keys.TryAdd(key.KeyId, key);
                        continue;
                    }

                    LauncherLog.Warn("launcher.json has an invalid public key entry, skipped.");
                }
            }

            if (keys.Count == 0)
            {
                error = "런처 설정에 올바른 패치 공개키(publicKeys)가 없습니다.";
                return false;
            }

            string installRoot = document.InstallRoot?.Trim() ?? string.Empty;
            if (installRoot.Length == 0)
            {
                installRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DEFAULT_ROOT_FOLDER);
            }

            try
            {
                installRoot = Path.GetFullPath(installRoot);
                if (!isHttp)
                {
                    patchSource = Path.GetFullPath(patchSource);
                }
            }
            catch (Exception exception) when ((exception is ArgumentException) || (exception is NotSupportedException)
                || (exception is PathTooLongException))
            {
                error = "런처 설정의 경로가 올바르지 않습니다.";
                return false;
            }

            if (!TryResolveApiBase(document, out Uri? apiBaseUri, out error))
            {
                return false;
            }

            // 홈페이지 주소가 틀려도 회원가입 링크만 숨기면 되므로 설정 오류로 막지 않는다
            Uri? websiteUri = null;
            string websiteUrl = document.WebsiteUrl?.Trim() ?? string.Empty;
            if ((websiteUrl.Length > 0) && !TryParseHttpUri(websiteUrl, out websiteUri))
            {
                LauncherLog.Warn("launcher.json websiteUrl is not an absolute http(s) URL, signup link hidden.");
            }

            settings = new LauncherSettings(patchSource, isHttp, channel, new TrustedKeySet(keys.Values), installRoot, document.TeamBuild,
                apiBaseUri, websiteUri);
            return true;
        }

        private static bool TryResolveApiBase(LauncherConfigDocument document, out Uri? apiBaseUri, out string error)
        {
            apiBaseUri = null;
            error      = string.Empty;

            // 없어도 패치는 할 수 있다. 로그인과 게임 시작만 막고 화면에서 이유를 보인다
            string apiBaseUrl = document.ApiBaseUrl?.Trim() ?? string.Empty;
            if (apiBaseUrl.Length == 0)
            {
                return true;
            }

            if (!TryParseHttpUri(apiBaseUrl, out Uri? parsed))
            {
                error = "런처 설정의 API 주소(apiBaseUrl)가 올바르지 않습니다.";
                return false;
            }

            // 토큰과 비밀번호가 오가므로 외부 빌드는 https만 쓴다. 팀 빌드와 이 PC의 개발 서버는 http를 허용한다
            if ((parsed.Scheme == Uri.UriSchemeHttp) && !parsed.IsLoopback && !document.TeamBuild)
            {
                error = "런처 설정의 API 주소(apiBaseUrl)는 https여야 합니다.";
                return false;
            }

            apiBaseUri = parsed;
            return true;
        }

        private static bool TryParseHttpUri(string text, [NotNullWhen(true)] out Uri? uri)
        {
            if (!Uri.TryCreate(text, UriKind.Absolute, out uri))
            {
                return false;
            }

            if ((uri.Scheme == Uri.UriSchemeHttp) || (uri.Scheme == Uri.UriSchemeHttps))
            {
                return true;
            }

            uri = null;
            return false;
        }
    }
}
