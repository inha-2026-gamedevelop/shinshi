// System
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

// Project
using Shinshi.Contracts;
using Shinshi.Patch;

namespace Shinshi.Release
{
    /// <summary> publish 명령, 빌드 폴더를 콘텐츠 주소 저장소 배치로 게시하고 매니페스트와 채널 포인터에 서명한다 </summary>
    public sealed class ReleasePublisher
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string BLOBS_FOLDER    = "blobs";
        public const string RELEASES_FOLDER = "releases";
        public const string CHANNELS_FOLDER = "channels";
        public const string MANIFEST_FILE   = "manifest.json";
        public const string CHANNEL_EXT     = ".json";

        private const string RELEASE_ID_TIME_FORMAT = "yyyyMMdd-HHmmss";
        private const char   RELEASE_ID_SEPARATOR   = '-';
        private const int    RELEASE_ID_BUILD_CHARS = 8;
        private const int    BLOB_FOLDER_CHARS      = 2;

        private readonly TimeProvider _timeProvider;

        /****************************************
        *                Methods
        ****************************************/

        public ReleasePublisher()
            : this(TimeProvider.System)
        {
        }

        public ReleasePublisher(TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(timeProvider);
            _timeProvider = timeProvider;
        }

        /// <summary> 종료 코드를 돌려준다. 실패하면 채널 포인터는 바뀌지 않는다 </summary>
        public int Run(PublishOptions options, TextWriter output, TextWriter error)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(output);
            ArgumentNullException.ThrowIfNull(error);

            try
            {
                Publish(options, output);
                return PackagerExitCodes.SUCCESS;
            }
            catch (PackagerException exception)
            {
                error.WriteLine("error: " + exception.Message);
                return exception.ExitCode;
            }
            catch (IOException exception)
            {
                error.WriteLine("error: " + exception.Message);
                return PackagerExitCodes.IO_FAILURE;
            }
            catch (UnauthorizedAccessException exception)
            {
                error.WriteLine("error: " + exception.Message);
                return PackagerExitCodes.IO_FAILURE;
            }
        }

        private void Publish(PublishOptions options, TextWriter output)
        {
            ValidateOptions(options);
            PatchSigningKey key = LoadKey(options.KeyFile);

            // 모든 검사와 서명은 파일을 하나라도 쓰기 전에 끝낸다
            List<ScannedFile> files = BuildScanner.Scan(options.BuildFolder);
            RejectOutputInsideBuild(options);
            ResolveIdentity(options, out string buildId, out string buildKind, out string displayVersion);
            EnforceChannelPolicy(options, buildKind);

            string releaseId    = options.ReleaseId ?? CreateDefaultReleaseId(buildId);
            string productRoot  = Path.Combine(options.OutFolder, options.Product);
            string manifestDir  = Path.Combine(productRoot, RELEASES_FOLDER, releaseId);
            string manifestPath = Path.Combine(manifestDir, MANIFEST_FILE);
            string pointerPath  = Path.Combine(productRoot, CHANNELS_FOLDER, options.Channel + CHANNEL_EXT);
            if (!PatchValidation.IsValidReleaseId(releaseId))
            {
                throw new PackagerException(PackagerExitCodes.USAGE, "Release id is not valid: " + releaseId);
            }

            if (File.Exists(manifestPath))
            {
                throw new PackagerException(PackagerExitCodes.OUTPUT_CONFLICT, "Release already exists in the output folder: " + releaseId);
            }

            CheckExistingPointer(options, key, pointerPath);

            List<ManifestFile> entries = new List<ManifestFile>(files.Count);
            foreach (ScannedFile file in files)
            {
                entries.Add(new ManifestFile(file.RelativePath, file.Size, PatchHash.ComputeFile(file.FullPath)));
            }

            long nowMs = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
            ReleaseManifest manifest = new ReleaseManifest(options.Product, releaseId, options.Channel, buildId, buildKind,
                displayVersion, options.MinLauncher, nowMs, entries);
            if (!PatchDocuments.TrySealManifest(manifest, key, out byte[]? manifestEnvelope, out string? manifestSha256, out EPatchError manifestError))
            {
                throw new PackagerException(PackagerExitCodes.INVALID_BUILD, "Manifest was rejected: " + manifestError);
            }

            ChannelPointer pointer = new ChannelPointer(options.Product, options.Channel, releaseId, options.Sequence, manifestSha256, nowMs);
            if (!PatchDocuments.TrySealChannelPointer(pointer, key, out byte[]? pointerEnvelope, out EPatchError pointerError))
            {
                throw new PackagerException(PackagerExitCodes.INVALID_BUILD, "Channel pointer was rejected: " + pointerError);
            }

            int newBlobs = WriteBlobs(productRoot, files, entries);

            // 매니페스트를 먼저 쓰고 포인터를 마지막에 쓴다. 중간에 죽어도 포인터가 없는 매니페스트를 가리키는 일이 없다
            Directory.CreateDirectory(manifestDir);
            AtomicFile.WriteBytes(manifestPath, manifestEnvelope, false);
            Directory.CreateDirectory(Path.GetDirectoryName(pointerPath)!);
            AtomicFile.WriteBytes(pointerPath, pointerEnvelope, true);

            // TODO(민성): S3/R2 업로드와 API 허용 buildId 등록은 범위 밖이다. 업로드는 게시 폴더를 그대로 올린다
            output.WriteLine("product:   " + options.Product);
            output.WriteLine("channel:   " + options.Channel);
            output.WriteLine("releaseId: " + releaseId);
            output.WriteLine("buildId:   " + buildId);
            output.WriteLine("sequence:  " + options.Sequence.ToString(CultureInfo.InvariantCulture));
            output.WriteLine("files:     " + entries.Count.ToString(CultureInfo.InvariantCulture));
            output.WriteLine("bytes:     " + manifest.GetTotalSize().ToString(CultureInfo.InvariantCulture));
            output.WriteLine("newBlobs:  " + newBlobs.ToString(CultureInfo.InvariantCulture));
        }

        private static void ValidateOptions(PublishOptions options)
        {
            if (!PatchFormat.IsKnownProduct(options.Product))
            {
                throw new PackagerException(PackagerExitCodes.USAGE, "Product must be game or launcher: " + options.Product);
            }

            if (!PatchFormat.IsKnownChannel(options.Channel))
            {
                throw new PackagerException(PackagerExitCodes.USAGE, "Channel must be internal or stable: " + options.Channel);
            }

            if (options.Sequence < PatchValidation.MIN_SEQUENCE)
            {
                throw new PackagerException(PackagerExitCodes.USAGE, "Sequence must be at least 1.");
            }

            if (!PatchValidation.TryParseLauncherVersion(options.MinLauncher, out _))
            {
                throw new PackagerException(PackagerExitCodes.USAGE, "Min launcher version must look like 1.0.0: " + options.MinLauncher);
            }

            if ((options.ReleaseId != null) && !PatchValidation.IsValidReleaseId(options.ReleaseId))
            {
                throw new PackagerException(PackagerExitCodes.USAGE, "Release id is not valid: " + options.ReleaseId);
            }

            bool hasIdentityOptions = (options.BuildId != null) || (options.BuildKind != null) || (options.DisplayVersion != null);
            if ((options.Product == PatchFormat.PRODUCT_GAME) && hasIdentityOptions)
            {
                throw new PackagerException(PackagerExitCodes.USAGE,
                    "build-id, build-kind and display-version are read from build-identity.json for the game product.");
            }

            if ((options.Product == PatchFormat.PRODUCT_LAUNCHER)
                && ((options.BuildId == null) || (options.BuildKind == null) || (options.DisplayVersion == null)))
            {
                throw new PackagerException(PackagerExitCodes.USAGE,
                    "The launcher product needs --build-id, --build-kind and --display-version.");
            }
        }

        private static PatchSigningKey LoadKey(string keyFile)
        {
            if (!File.Exists(keyFile))
            {
                throw new PackagerException(PackagerExitCodes.KEY_ERROR, "Key file does not exist: " + keyFile);
            }

            // 크기를 먼저 보고 거대한 파일을 통째로 읽지 않는다
            if (new FileInfo(keyFile).Length > PatchSigningKey.SEED_BYTES * 4)
            {
                throw new PackagerException(PackagerExitCodes.KEY_ERROR, "Key file is too large to be a signing key.");
            }

            if (!PatchSigningKey.TryFromBase64Seed(File.ReadAllText(keyFile), out PatchSigningKey? key))
            {
                throw new PackagerException(PackagerExitCodes.KEY_ERROR, "Key file does not contain a base64 Ed25519 seed.");
            }

            return key;
        }

        private static void RejectOutputInsideBuild(PublishOptions options)
        {
            string build = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.BuildFolder)) + Path.DirectorySeparatorChar;
            string output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.OutFolder)) + Path.DirectorySeparatorChar;
            if (output.StartsWith(build, StringComparison.OrdinalIgnoreCase))
            {
                throw new PackagerException(PackagerExitCodes.USAGE, "Output folder must not be inside the build folder.");
            }
        }

        private static void ResolveIdentity(PublishOptions options, out string buildId, out string buildKind, out string displayVersion)
        {
            if (options.Product == PatchFormat.PRODUCT_LAUNCHER)
            {
                // 런처는 Unity 빌드가 아니라 build-identity.json이 없다. 값은 옵션으로 받는다
                buildId        = options.BuildId!;
                buildKind      = options.BuildKind!;
                displayVersion = options.DisplayVersion!;
                return;
            }

            if (!BuildIdentityReader.TryRead(options.BuildFolder, out BuildIdentity? identity, out EPatchError error))
            {
                throw new PackagerException(PackagerExitCodes.INVALID_BUILD, "Cannot read build-identity.json: " + error);
            }

            buildId        = identity.BuildId;
            buildKind      = identity.BuildKind;
            displayVersion = identity.DisplayVersion ?? string.Empty;
        }

        private static void EnforceChannelPolicy(PublishOptions options, string buildKind)
        {
            if (options.Channel != PatchFormat.CHANNEL_STABLE)
            {
                return;
            }

            if (buildKind != BuildIdentity.KIND_RELEASE)
            {
                throw new PackagerException(PackagerExitCodes.RELEASE_REFUSED,
                    "The stable channel only accepts Release builds, this build is " + buildKind + ".");
            }

            // boot.config 검사는 Unity 게임 빌드에만 해당한다
            if (options.Product != PatchFormat.PRODUCT_GAME)
            {
                return;
            }

            if (!DevBuildMarkers.TryFind(options.BuildFolder, out IReadOnlyList<string> markers, out EPatchError error))
            {
                throw new PackagerException(PackagerExitCodes.RELEASE_REFUSED,
                    "Cannot verify the build has no development markers: " + error);
            }

            if (markers.Count > 0)
            {
                throw new PackagerException(PackagerExitCodes.RELEASE_REFUSED,
                    "The stable channel refuses builds with development markers in boot.config: " + string.Join(", ", markers));
            }
        }

        private string CreateDefaultReleaseId(string buildId)
        {
            StringBuilder suffix = new StringBuilder();
            foreach (char character in buildId)
            {
                if (suffix.Length >= RELEASE_ID_BUILD_CHARS)
                {
                    break;
                }

                if (IsReleaseIdChar(character))
                {
                    suffix.Append(character);
                }
            }

            string time = _timeProvider.GetUtcNow().UtcDateTime.ToString(RELEASE_ID_TIME_FORMAT, CultureInfo.InvariantCulture);

            // 끝의 점은 Windows 폴더 이름에서 지워지므로 릴리스 id 규칙이 거절한다
            string tail = suffix.ToString().TrimEnd('.');
            return (tail.Length == 0) ? time : (time + RELEASE_ID_SEPARATOR + tail);
        }

        private static bool IsReleaseIdChar(char character)
        {
            bool isLetter = ((character >= 'A') && (character <= 'Z')) || ((character >= 'a') && (character <= 'z'));
            bool isDigit  = (character >= '0') && (character <= '9');
            return isLetter || isDigit || (character == '.') || (character == '_') || (character == '-');
        }

        // 기존 포인터가 서명이 맞고 같은 제품, 채널을 가리키는 경우에만 sequence를 비교한다.
        // 열 수 없으면 덮어쓰지 않고 실패한다. 조용히 덮어쓰면 키가 바뀌었거나 손상된 것을 놓친다
        private static void CheckExistingPointer(PublishOptions options, PatchSigningKey key, string pointerPath)
        {
            if (!File.Exists(pointerPath))
            {
                return;
            }

            if (new FileInfo(pointerPath).Length > PatchFormat.MAX_CHANNEL_ENVELOPE_BYTES)
            {
                throw new PackagerException(PackagerExitCodes.OUTPUT_CONFLICT, "Existing channel pointer is too large: " + pointerPath);
            }

            TrustedKeySet trusted = new TrustedKeySet(key.PublicKey);
            if (!PatchDocuments.TryOpenChannelPointer(File.ReadAllBytes(pointerPath), trusted, out ChannelPointer? existing, out EPatchError error))
            {
                throw new PackagerException(PackagerExitCodes.OUTPUT_CONFLICT,
                    "Existing channel pointer cannot be verified with this key (" + error + "). Refusing to overwrite: " + pointerPath);
            }

            if ((existing.Product != options.Product) || (existing.Channel != options.Channel))
            {
                throw new PackagerException(PackagerExitCodes.OUTPUT_CONFLICT, "Existing channel pointer is for another product or channel.");
            }

            if (options.Sequence <= existing.Sequence)
            {
                throw new PackagerException(PackagerExitCodes.SEQUENCE_REJECTED,
                    "Sequence " + options.Sequence.ToString(CultureInfo.InvariantCulture) + " must be greater than the published "
                    + existing.Sequence.ToString(CultureInfo.InvariantCulture) + ".");
            }
        }

        // 새로 쓴 blob 수를 돌려준다
        private static int WriteBlobs(string productRoot, List<ScannedFile> files, List<ManifestFile> entries)
        {
            HashSet<string> handled  = new HashSet<string>(StringComparer.Ordinal);
            int             newBlobs = 0;
            for (int i = 0; i < files.Count; ++i)
            {
                string sha = entries[i].Sha256;
                if (!handled.Add(sha))
                {
                    continue;
                }

                string folder = Path.Combine(productRoot, BLOBS_FOLDER, sha.Substring(0, BLOB_FOLDER_CHARS));
                string blob   = Path.Combine(folder, sha);
                if (File.Exists(blob))
                {
                    // 이름과 내용이 다른 blob을 믿고 건너뛰면 설치 단계에서 깨진 파일이 배포된다
                    if (PatchHash.ComputeFile(blob) != sha)
                    {
                        throw new PackagerException(PackagerExitCodes.OUTPUT_CONFLICT, "Existing blob does not match its hash: " + blob);
                    }

                    continue;
                }

                Directory.CreateDirectory(folder);
                if (!AtomicFile.TryCopyVerified(files[i].FullPath, blob, sha, files[i].Size))
                {
                    throw new PackagerException(PackagerExitCodes.INVALID_BUILD,
                        "File changed while publishing: " + files[i].RelativePath);
                }

                ++newBlobs;
            }

            return newBlobs;
        }
    }
}
