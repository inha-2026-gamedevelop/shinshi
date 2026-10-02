// System
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Shinshi.Launcher.Account
{
    /// <summary> Windows DPAPI(CurrentUser) 암호화. 같은 Windows 사용자만 풀 수 있다 </summary>
    internal static class Dpapi
    {
        // System.Security.Cryptography.ProtectedData 패키지를 더하지 않으려고 crypt32를 직접 부른다

        /****************************************
        *                Fields
        ****************************************/

        // 대화 상자를 띄우지 않는다. 런처는 사용자 확인 없이 자동 로그인한다
        private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int    Size;
            public IntPtr Data;
        }

        /****************************************
        *                Methods
        ****************************************/

        public static byte[] Protect(byte[] plain, byte[] entropy)
        {
            ArgumentNullException.ThrowIfNull(plain);
            ArgumentNullException.ThrowIfNull(entropy);

            return Transform(plain, entropy, true);
        }

        /// <summary> 다른 사용자, 다른 PC에서 만든 값이나 손상된 값이면 false </summary>
        public static bool TryUnprotect(byte[] cipher, byte[] entropy, out byte[] plain)
        {
            ArgumentNullException.ThrowIfNull(cipher);
            ArgumentNullException.ThrowIfNull(entropy);

            try
            {
                plain = Transform(cipher, entropy, false);
                return true;
            }
            catch (CryptographicException)
            {
                plain = Array.Empty<byte>();
                return false;
            }
        }

        private static byte[] Transform(byte[] input, byte[] entropy, bool isProtect)
        {
            DataBlob inputBlob   = Allocate(input);
            DataBlob entropyBlob = Allocate(entropy);
            DataBlob outputBlob  = new DataBlob();
            try
            {
                bool isDone = isProtect
                    ? CryptProtectData(ref inputBlob, null, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN,
                        out outputBlob)
                    : CryptUnprotectData(ref inputBlob, IntPtr.Zero, ref entropyBlob, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN,
                        out outputBlob);
                if (!isDone)
                {
                    throw new CryptographicException(new Win32Exception(Marshal.GetLastWin32Error()).Message);
                }

                byte[] output = new byte[outputBlob.Size];
                Marshal.Copy(outputBlob.Data, output, 0, outputBlob.Size);
                return output;
            }
            finally
            {
                Release(ref inputBlob);
                Release(ref entropyBlob);
                if (outputBlob.Data != IntPtr.Zero)
                {
                    // 복호화 결과는 평문 토큰이라 해제 전에 지운다
                    ZeroUnmanaged(outputBlob);
                    LocalFree(outputBlob.Data);
                }
            }
        }

        private static DataBlob Allocate(byte[] bytes)
        {
            DataBlob blob = new DataBlob { Size = bytes.Length, Data = Marshal.AllocHGlobal(Math.Max(bytes.Length, 1)) };
            Marshal.Copy(bytes, 0, blob.Data, bytes.Length);
            return blob;
        }

        private static void Release(ref DataBlob blob)
        {
            if (blob.Data == IntPtr.Zero)
            {
                return;
            }

            ZeroUnmanaged(blob);
            Marshal.FreeHGlobal(blob.Data);
            blob.Data = IntPtr.Zero;
        }

        private static void ZeroUnmanaged(DataBlob blob)
        {
            for (int i = 0; i < blob.Size; ++i)
            {
                Marshal.WriteByte(blob.Data, i, 0);
            }
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(ref DataBlob dataIn, string? description, ref DataBlob entropy, IntPtr reserved,
            IntPtr prompt, int flags, out DataBlob dataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, ref DataBlob entropy, IntPtr reserved,
            IntPtr prompt, int flags, out DataBlob dataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);
    }
}
