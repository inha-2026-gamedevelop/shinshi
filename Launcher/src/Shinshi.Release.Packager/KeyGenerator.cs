// System
using System;
using System.IO;
using System.Text;

// Project
using Shinshi.Patch;

namespace Shinshi.Release
{
    /// <summary> keygen 명령, 키쌍 파일을 쓰고 keyId와 공개키만 출력한다. 개인키는 출력하지 않는다 </summary>
    public static class KeyGenerator
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string PRIVATE_KEY_FILE_NAME = "shinshi-patch-private.key";
        public const string PUBLIC_KEY_FILE_NAME  = "shinshi-patch-public.key";

        /****************************************
        *                Methods
        ****************************************/

        /// <summary> 종료 코드를 돌려준다. 개인키나 공개키 파일이 이미 있으면 덮어쓰지 않고 실패한다 </summary>
        public static int Run(KeygenOptions options, TextWriter output, TextWriter error)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(output);
            ArgumentNullException.ThrowIfNull(error);

            string privatePath = Path.Combine(options.OutFolder, PRIVATE_KEY_FILE_NAME);
            string publicPath  = Path.Combine(options.OutFolder, PUBLIC_KEY_FILE_NAME);
            if (File.Exists(privatePath) || File.Exists(publicPath))
            {
                error.WriteLine("error: a key file already exists in " + options.OutFolder + ". Refusing to overwrite.");
                return PackagerExitCodes.OUTPUT_CONFLICT;
            }

            PatchSigningKey key = PatchSigningKey.Generate();
            try
            {
                Directory.CreateDirectory(options.OutFolder);

                // 개인키를 먼저 쓴다. 중간에 실패해도 짝 없는 공개키만 남는 일이 없다
                WriteNewFile(privatePath, key.ExportBase64Seed());
                WriteNewFile(publicPath, key.PublicKey.ToBase64());
            }
            catch (IOException exception)
            {
                error.WriteLine("error: could not write key files: " + exception.Message);
                return PackagerExitCodes.IO_FAILURE;
            }
            catch (UnauthorizedAccessException exception)
            {
                error.WriteLine("error: could not write key files: " + exception.Message);
                return PackagerExitCodes.IO_FAILURE;
            }

            output.WriteLine("keyId:      " + key.KeyId);
            output.WriteLine("publicKey:  " + key.PublicKey.ToBase64());
            output.WriteLine("privateKey: " + privatePath + " (keep this file secret, never commit it)");
            output.WriteLine("publicFile: " + publicPath);
            return PackagerExitCodes.SUCCESS;
        }

        private static void WriteNewFile(string path, string text)
        {
            using FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            byte[] bytes = new UTF8Encoding(false).GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
        }
    }
}
