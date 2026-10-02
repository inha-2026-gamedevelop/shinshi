namespace Shinshi.Backend.Tests.AdminCli
{
    /// <summary> 한 번 실행한 결과 </summary>
    internal sealed class CliRun
    {
        /****************************************
        *              Properties
        ****************************************/

        public int ExitCode { get; }
        public string Out { get; }
        public string Error { get; }
        public int SecretReads { get; }

        /****************************************
        *                Methods
        ****************************************/

        public CliRun(int exitCode, string output, string error, int secretReads)
        {
            ExitCode    = exitCode;
            Out         = output;
            Error       = error;
            SecretReads = secretReads;
        }

        public override string ToString()
        {
            return "exit " + ExitCode + "\nstdout:\n" + Out + "\nstderr:\n" + Error;
        }
    }
}
