// System
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;

// Third-party
using Newtonsoft.Json;

// Project
using Shinshi.Contracts;

namespace Shinshi.Patch
{
    /// <summary> 빌드 폴더의 *_Data/StreamingAssets/build-identity.json을 읽는다 </summary>
    public static class BuildIdentityReader
    {
        /****************************************
        *                Fields
        ****************************************/

        public const int MAX_IDENTITY_BYTES = 64 * 1024;

        private const string STREAMING_ASSETS_FOLDER = "StreamingAssets";
        private const char   BYTE_ORDER_MARK         = '\uFEFF';

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 정확히 한 개의 신원 파일을 읽는다. 없거나 둘 이상이면 실패 </summary>
        public static bool TryRead(string buildFolder, [NotNullWhen(true)] out BuildIdentity? identity, out EPatchError error)
        {
            ArgumentException.ThrowIfNullOrEmpty(buildFolder);

            identity = null;
            List<string> found = BuildFolder.FindInDataFolders(buildFolder,
                Path.Combine(STREAMING_ASSETS_FOLDER, BuildIdentity.FILE_NAME));
            if (found.Count == 0)
            {
                error = EPatchError.BuildIdentityMissing;
                return false;
            }

            // 어느 쪽이 실제 실행 파일의 신원인지 알 수 없으므로 하나를 고르지 않고 실패한다
            if (found.Count > 1)
            {
                error = EPatchError.BuildIdentityAmbiguous;
                return false;
            }

            return TryReadFile(found[0], out identity, out error);
        }

        /// <summary> build-identity.json 파일 하나를 읽고 필수 필드를 확인한다 </summary>
        public static bool TryReadFile(string path, [NotNullWhen(true)] out BuildIdentity? identity, out EPatchError error)
        {
            ArgumentException.ThrowIfNullOrEmpty(path);

            identity = null;
            if (new FileInfo(path).Length > MAX_IDENTITY_BYTES)
            {
                error = EPatchError.TooLarge;
                return false;
            }

            if (!PatchEnvelope.TryDecodeUtf8(File.ReadAllBytes(path), out string? text))
            {
                error = EPatchError.Malformed;
                return false;
            }

            BuildIdentity? parsed;
            try
            {
                // 편집기에 따라 BOM이 붙어 저장될 수 있다
                parsed = JsonConvert.DeserializeObject<BuildIdentity>(text.TrimStart(BYTE_ORDER_MARK), ContractJson.CreateSettings());
            }
            catch (JsonException)
            {
                error = EPatchError.Malformed;
                return false;
            }

            if (parsed == null)
            {
                error = EPatchError.Malformed;
                return false;
            }

            if (string.IsNullOrWhiteSpace(parsed.BuildId)
                || ((parsed.BuildKind != BuildIdentity.KIND_DEVELOPMENT) && (parsed.BuildKind != BuildIdentity.KIND_RELEASE)))
            {
                error = EPatchError.InvalidField;
                return false;
            }

            identity = parsed;
            error    = EPatchError.None;
            return true;
        }
    }
}
