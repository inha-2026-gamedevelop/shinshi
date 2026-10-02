// System
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;

// Third-party
using Newtonsoft.Json;

// Project
using Shinshi.Contracts;

namespace Shinshi.Patch.Client
{
    /// <summary> current.json과 state/channels.json 읽기, 쓰기. 쓰기는 임시 파일 + 이름 바꾸기로 원자적이다 </summary>
    internal static class PatchStateStore
    {
        /****************************************
        *                Fields
        ****************************************/

        // 상태 파일은 몇 줄짜리라 이보다 크면 손상으로 본다
        private const int MAX_STATE_FILE_BYTES = 1024 * 1024;

        private static readonly UTF8Encoding G_Utf8 = new UTF8Encoding(false);

        /****************************************
        *                Methods
        ****************************************/

        public static bool TryReadCurrentReleaseId(PatchInstallLayout layout, [NotNullWhen(true)] out string? releaseId)
        {
            releaseId = null;
            CurrentInstallDocument? document = Read<CurrentInstallDocument>(layout.CurrentFilePath);
            if ((document == null) || (document.FormatVersion != PatchFormat.FORMAT_VERSION)
                || !PatchValidation.IsValidReleaseId(document.ReleaseId))
            {
                return false;
            }

            releaseId = document.ReleaseId;
            return true;
        }

        public static void WriteCurrentReleaseId(PatchInstallLayout layout, string releaseId)
        {
            Write(layout.CurrentFilePath, new CurrentInstallDocument(releaseId));
        }

        public static long ReadSequence(PatchInstallLayout layout, string product, string channel)
        {
            return ReadChannelState(layout).GetSequence(product, channel);
        }

        public static void RaiseSequence(PatchInstallLayout layout, string product, string channel, long sequence)
        {
            ChannelStateDocument state = ReadChannelState(layout);
            state.RaiseSequence(product, channel, sequence);
            Write(layout.ChannelStatePath, state);
        }

        // 파일이 없거나 깨졌으면 처음 설치처럼 0부터 본다. 로컬 파일을 고칠 수 있으면 설치 폴더도 고칠 수 있어
        // 여기서 거절해 얻는 것이 없고, 거절하면 상태 파일이 깨진 사용자가 영영 업데이트를 받지 못한다
        private static ChannelStateDocument ReadChannelState(PatchInstallLayout layout)
        {
            ChannelStateDocument? state = Read<ChannelStateDocument>(layout.ChannelStatePath);
            if ((state == null) || (state.FormatVersion != PatchFormat.FORMAT_VERSION))
            {
                return new ChannelStateDocument();
            }

            return state;
        }

        private static T? Read<T>(string path)
            where T : class
        {
            if (!File.Exists(path))
            {
                return null;
            }

            byte[]? bytes = PatchFileSystem.ReadAllBytesLimited(path, MAX_STATE_FILE_BYTES);
            if (bytes == null)
            {
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<T>(G_Utf8.GetString(bytes), ContractJson.CreateSettings());
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static void Write(string path, object document)
        {
            string json = JsonConvert.SerializeObject(document, ContractJson.CreateSettings());
            PatchFileSystem.WriteAllBytesAtomic(path, G_Utf8.GetBytes(json));
        }
    }
}
