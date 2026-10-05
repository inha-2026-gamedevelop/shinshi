// System
using System;
using System.Globalization;
using System.IO;
using System.Text;

// Unity
using UnityEngine;

// 서드파티
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

// 프로젝트 내부
using Shinshi.Common;
using Shinshi.Contracts;

namespace Shinshi.Network
{
    /// <summary> 빌드 후처리가 StreamingAssets에 쓴 build-identity.json을 읽고 실행 중인 프로토콜, 콘텐츠 해시와 대조한다 </summary>
    public static class BuildIdentityLoader
    {
        // 파일은 설치 폴더에 있어 누구나 바꿀 수 있다. 신원 값은 API가 허용 목록과 다시 대조하므로 여기서는
        // 다른 빌드의 파일이 섞이지 않았는지만 막는다. protocolVersion과 contentHash는 실행 중인 값과 다르면 거절한다

        /****************************************
        *                Fields
        ****************************************/

        // 정상 파일은 1KB 안쪽이다. 큰 파일을 통째로 읽지 않으려는 상한
        private const long MAX_FILE_BYTES = 64 * 1024;
        private const int  MAX_JSON_DEPTH = 4;

        private static readonly UTF8Encoding _strictUtf8 = new UTF8Encoding(false, true);

        // 빌드 후처리와 같은 해석 방식. 타입 이름 처리는 끈다, 파일 내용이 만들 타입을 고르게 두면 원격 코드 실행 경로가 된다
        private static readonly JsonSerializerSettings _jsonSettings = CreateJsonSettings();

        /****************************************
        *              Properties
        ****************************************/

        /// <summary> 플레이어 빌드의 신원 파일 경로, Writer가 실행 파일 옆 데이터 폴더의 StreamingAssets에 쓰는 자리와 같다 </summary>
        public static string RuntimePath => Path.Combine(Application.streamingAssetsPath, BuildIdentity.FILE_NAME);

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 실행 중인 빌드의 신원을 읽는다, 에디터는 파일이 없으므로 null과 EditorSkipped다 </summary>
        public static BuildIdentity LoadForRuntime(string contentHash, out EBuildIdentityStatus status)
        {
#if UNITY_EDITOR
            status = EBuildIdentityStatus.EditorSkipped;
            return null;
#else
            return Load(RuntimePath, Constants.Network.PROTOCOL_VERSION, contentHash, out status);
#endif
        }

        /// <summary> 파일을 읽어 대조한다, Loaded가 아니면 null </summary>
        public static BuildIdentity Load(string path, int expectedProtocolVersion, string expectedContentHash,
            out EBuildIdentityStatus status)
        {
            if (string.IsNullOrEmpty(path) || (!File.Exists(path)))
            {
                status = EBuildIdentityStatus.Missing;
                return null;
            }

            byte[] bytes;
            try
            {
                if (new FileInfo(path).Length > MAX_FILE_BYTES)
                {
                    status = EBuildIdentityStatus.Corrupt;
                    return null;
                }

                bytes = File.ReadAllBytes(path);
            }
            catch (IOException)
            {
                status = EBuildIdentityStatus.Corrupt;
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                status = EBuildIdentityStatus.Corrupt;
                return null;
            }

            string json;
            try
            {
                json = _strictUtf8.GetString(bytes);
            }
            catch (ArgumentException)
            {
                status = EBuildIdentityStatus.Corrupt;
                return null;
            }

            return Parse(json, expectedProtocolVersion, expectedContentHash, out status);
        }

        /// <summary> JSON 본문을 읽어 대조한다, Loaded가 아니면 null </summary>
        public static BuildIdentity Parse(string json, int expectedProtocolVersion, string expectedContentHash,
            out EBuildIdentityStatus status)
        {
            status = EBuildIdentityStatus.Corrupt;
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            BuildIdentity identity;
            try
            {
                identity = JsonConvert.DeserializeObject<BuildIdentity>(json, _jsonSettings);
            }
            catch (JsonException)
            {
                return null;
            }

            if (!IsComplete(identity))
            {
                return null;
            }

            if (identity.ProtocolVersion != expectedProtocolVersion)
            {
                status = EBuildIdentityStatus.ProtocolMismatch;
                return null;
            }

            if (!string.Equals(identity.ContentHash, expectedContentHash, StringComparison.Ordinal))
            {
                status = EBuildIdentityStatus.ContentHashMismatch;
                return null;
            }

            status = EBuildIdentityStatus.Loaded;
            return identity;
        }

        private static bool IsComplete(BuildIdentity identity)
        {
            if (identity == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(identity.BuildId) || string.IsNullOrWhiteSpace(identity.ContentHash))
            {
                return false;
            }

            return string.Equals(identity.BuildKind, BuildIdentity.KIND_RELEASE, StringComparison.Ordinal)
                || string.Equals(identity.BuildKind, BuildIdentity.KIND_DEVELOPMENT, StringComparison.Ordinal);
        }

        private static JsonSerializerSettings CreateJsonSettings()
        {
            var settings = new JsonSerializerSettings();
            settings.ContractResolver    = new DefaultContractResolver();
            settings.ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor;
            settings.TypeNameHandling    = TypeNameHandling.None;
            settings.DateParseHandling   = DateParseHandling.None;
            settings.Culture             = CultureInfo.InvariantCulture;
            settings.MaxDepth            = MAX_JSON_DEPTH;
            return settings;
        }
    }
}
