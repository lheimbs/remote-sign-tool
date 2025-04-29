using NLog;
using RemoteSignTool.Common.Dto;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace RemoteSignTool.Server.Services;

/// <summary>
/// Provides methods for signing files using the SignTool.
/// </summary>
public class SignToolService : ISignToolService
{
    private const string SignToolX64Path = @"C:\Program Files (x86)\Windows Kits\10\bin\x64\signtool.exe";
    private const string SignToolX86Path = @"C:\Program Files (x86)\Windows Kits\10\bin\x86\signtool.exe";
    private const string WindowsSDKRootPath = @"C:\Program Files (x86)\Windows Kits\10\bin\";

    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Tries to find the SignTool path.
    /// </summary>
    /// <param name="path">The SignTool path if found.</param>
    /// <returns>True if the SignTool path is found, otherwise false.</returns>
    public bool TryToFindSignToolPath(out string? path)
    {
        if (File.Exists(SignToolX64Path))
        {
            path = SignToolX64Path;
            return true;
        }
        else if (File.Exists(SignToolX86Path))
        {
            path = SignToolX86Path;
            return true;
        }
        else
        {
            DirectoryInfo sdkRoot = new DirectoryInfo(WindowsSDKRootPath);
            DirectoryInfo[] subDirs = sdkRoot.GetDirectories("10.*", SearchOption.AllDirectories);

            foreach (DirectoryInfo dirInfo in subDirs.Reverse())
            {
                string sdkPath = dirInfo.FullName;
                Logger.Log(NLog.LogLevel.Info, $"Searching for signtool in {sdkPath}...");

                string signToolPath = Path.Combine(sdkPath, "x64", "signtool.exe");

                if (File.Exists(signToolPath))
                {
                    path = signToolPath;
                    return true;
                }
            }
        }

        path = null;
        return false;
    }

    /// <summary>
    /// Signs the files using the SignTool.
    /// </summary>
    /// <param name="signToolPath">The path to the SignTool.</param>
    /// <param name="signSubcommands">The SignTool subcommands.</param>
    /// <param name="workingDirectory">The working directory.</param>
    /// <returns>The result of the signing process.</returns>
    public async Task<SignResultDto> Sign(string signToolPath, string signSubcommands, string workingDirectory)
    {
        using (var process = new Process())
        {
            var signToolArguments = string.Format("sign {0} *.*", signSubcommands);
            Logger.Info("Executing: {SignToolPath} {SignToolArguments} in {WorkingDirectory}", signToolPath, signToolArguments, workingDirectory);

            process.StartInfo.FileName = signToolPath;
            process.StartInfo.Arguments = signToolArguments;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.WorkingDirectory = workingDirectory;
            process.Start();

            string standardOutput = await process.StandardOutput.ReadToEndAsync();
            string standardError = await process.StandardError.ReadToEndAsync();

            process.WaitForExit(300000);

            return new SignResultDto()
            {
                ExitCode = process.ExitCode,
                StandardOutput = standardOutput,
                StandardError = standardError
            };
        }
    }
}
