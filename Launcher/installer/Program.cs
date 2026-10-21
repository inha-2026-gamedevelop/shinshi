// System
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Windows.Forms;

namespace Shinshi.Setup
{
    internal static class Program
    {
        /****************************************
        *                Fields
        ****************************************/

        private const string TITLE          = "신시 런처 설치";
        private const string EXECUTABLE     = "ShinshiLauncher.exe";
        private const string CONFIG         = "launcher.json";
        private const string INSTANCE_MUTEX = @"Local\Shinshi.Setup";
        private const int    ID_LENGTH      = 16;

        /****************************************
        *                Methods
        ****************************************/

        [STAThread]
        private static int Main(string[] args)
        {
            bool verifyOnly = (args.Length == 1) && (args[0] == "--verify-only");
            bool quiet = (args.Length == 2) && (args[0] == "--accept-certificate-sha256");
            using Mutex mutex = new Mutex(true, INSTANCE_MUTEX, out bool first);
            if (!first)
            {
                return 2;
            }

            try
            {
                using Stream payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("LauncherPayload")
                    ?? throw new InvalidDataException("런처 설치 파일이 없습니다.");
                string version = Convert.ToHexString(SHA256.HashData(payload)).Substring(0, ID_LENGTH);
                payload.Position = 0;
                using ZipArchive archive = new ZipArchive(payload, ZipArchiveMode.Read);
                ValidatePayload(archive);
                using X509Certificate2? certificate = ReadCertificate();
                if (quiet && ((certificate == null) || !string.Equals(args[1],
                    certificate.GetCertHashString(HashAlgorithmName.SHA256), StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidDataException("명시적으로 승인한 인증서 지문과 다릅니다.");
                }
                if (verifyOnly)
                {
                    return 0;
                }

                string message = "신시 런처를 설치하고 바탕화면에 바로가기를 만듭니다.\n게임 파일과 자동 로그인 정보는 유지됩니다.";
                if (certificate != null)
                {
                    message += "\n\nLAN 테스트 서버 인증서를 현재 사용자의 신뢰할 수 있는 루트 인증 기관에 등록합니다."
                        + "\n대상: " + certificate.Subject + "\nSHA-256: " + certificate.GetCertHashString(HashAlgorithmName.SHA256);
                }

                if (!quiet && (MessageBox.Show(message + "\n\n설치할까요?", TITLE, MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information) != DialogResult.Yes))
                {
                    return 2;
                }

                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Shinshi", "launcher", "versions");
                Directory.CreateDirectory(root);
                string destination = Path.Combine(root, version);
                string staging = Path.Combine(root, ".install-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(staging);
                try
                {
                    archive.ExtractToDirectory(staging);
                    if (certificate != null)
                    {
                        InstallCertificate(certificate);
                    }

                    if (!Directory.Exists(destination))
                    {
                        Directory.Move(staging, destination);
                    }
                    else
                    {
                        // 같은 설치본의 재실행도 손상된 파일을 복구한다. 실행 중이면 덮어쓰기를 실패로 알린다.
                        foreach (string file in Directory.GetFiles(staging))
                        {
                            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
                        }
                    }

                    CreateShortcut(destination);
                }
                finally
                {
                    if (Directory.Exists(staging))
                    {
                        Directory.Delete(staging, true);
                    }
                }

                if (!quiet)
                {
                    MessageBox.Show("설치가 완료됐습니다. 바탕화면의 신시 런처를 실행하세요.", TITLE);
                }
                return 0;
            }
            catch (Exception exception)
            {
                if (!verifyOnly && !quiet)
                {
                    MessageBox.Show("설치하지 못했습니다. 런처가 실행 중이면 닫고 다시 시도해 주세요.\n\n" + exception.Message,
                        TITLE, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                return 1;
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }

        private static void ValidatePayload(ZipArchive archive)
        {
            if ((archive.Entries.Count != 2) || (archive.GetEntry(EXECUTABLE) == null) || (archive.GetEntry(CONFIG) == null))
            {
                throw new InvalidDataException("런처 설치 파일 구성이 올바르지 않습니다.");
            }

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (entry.Length == 0)
                {
                    throw new InvalidDataException("빈 설치 파일입니다.");
                }

                using Stream source = entry.Open();
                source.CopyTo(Stream.Null);
            }
        }

        private static X509Certificate2? ReadCertificate()
        {
            using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ServerCertificate");
            if (stream == null)
            {
                return null;
            }

            using MemoryStream buffer = new MemoryStream();
            stream.CopyTo(buffer);
            X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(buffer.ToArray());
            X509BasicConstraintsExtension? constraints = certificate.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault();
            if (certificate.HasPrivateKey || (certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow)
                || (certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow) || (constraints?.CertificateAuthority != true))
            {
                certificate.Dispose();
                throw new InvalidDataException("유효한 서버 CA 공개 인증서가 아닙니다.");
            }

            return certificate;
        }

        private static void InstallCertificate(X509Certificate2 certificate)
        {
            using X509Store store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            if (store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, false).Count == 0)
            {
                store.Add(certificate);
            }

            if (store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, false).Count == 0)
            {
                throw new InvalidOperationException("인증서 설치를 확인하지 못했습니다.");
            }
        }

        private static void CreateShortcut(string directory)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            Type shellType = Type.GetTypeFromProgID("WScript.Shell", true)!;
            dynamic shell = Activator.CreateInstance(shellType)!;
            try
            {
                dynamic shortcut = shell.CreateShortcut(Path.Combine(desktop, "신시 런처.lnk"));
                try
                {
                    shortcut.TargetPath = Path.Combine(directory, EXECUTABLE);
                    shortcut.WorkingDirectory = directory;
                    shortcut.IconLocation = Path.Combine(directory, EXECUTABLE) + ",0";
                    shortcut.Description = "신시 런처";
                    shortcut.Save();
                }
                finally
                {
                    Marshal.FinalReleaseComObject(shortcut);
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }
}
