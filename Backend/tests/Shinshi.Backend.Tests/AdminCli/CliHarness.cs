// System
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

// Project
using Shinshi.AdminCli;

namespace Shinshi.Backend.Tests.AdminCli
{
    /// <summary> 임시 세션 폴더와 주입한 HTTP 핸들러로 명령줄 도구를 실행한다. 실행마다 새 콘솔과 CliContext를 만든다 </summary>
    internal sealed class CliHarness : IDisposable
    {
        /****************************************
        *                Fields
        ****************************************/

        public const string API = "http://localhost";

        private readonly Func<HttpMessageHandler> _handlers;

        /****************************************
        *              Properties
        ****************************************/

        public string DirectoryPath { get; }
        public AdminSessionStore Store { get; }
        public Dictionary<string, string> Environment { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public TimeProvider Time { get; set; } = TimeProvider.System;

        /****************************************
        *                Methods
        ****************************************/

        public CliHarness(Func<HttpMessageHandler> handlers)
        {
            _handlers     = handlers;
            DirectoryPath = Path.Combine(Path.GetTempPath(), "shinshi-admin-cli-" + Guid.NewGuid().ToString("N"));
            Store         = new AdminSessionStore(DirectoryPath);
        }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, true);
            }
        }

        /// <summary> 인자 끝에 --api를 붙여 실행한다. input은 확인 질문과 비밀 입력에 차례로 쓰인다 </summary>
        public async Task<CliRun> RunAsync(string[] args, params string?[] input)
        {
            TestCliConsole console = new TestCliConsole(input);
            CliContext context = new CliContext(console, Store, ReadEnvironment, Time, _handlers);
            int exitCode = await AdminCliApp.RunAsync(args.Concat(new[] { "--api", API }).ToArray(), context);
            return new CliRun(exitCode, console.OutText, console.ErrorText, console.SecretReads);
        }

        public void SaveSession(string token, DateTimeOffset expiresAt)
        {
            Store.Save(new AdminSession(API, token, expiresAt.ToUnixTimeMilliseconds(), Guid.NewGuid().ToString("D"),
                "tester", Shinshi.Contracts.Admin.AdminContractCodes.Role.OWNER));
        }

        private string? ReadEnvironment(string name)
        {
            return Environment.TryGetValue(name, out string? value) ? value : null;
        }
    }
}
