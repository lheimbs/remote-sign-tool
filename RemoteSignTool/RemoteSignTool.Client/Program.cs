using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Handlers;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.Zip;
using NLog;
using RemoteSignTool.Common.Dto;

namespace RemoteSignTool.Client;

/// <summary>
/// Main program class for the Remote Sign Tool client application.
/// Handles command-line arguments processing and communication with the signing server.
/// </summary>
class Program
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private const string TempDirectoryName = "Temp";
    
    /// <summary>
    /// Dictionary of supported sign options with their parameter count
    /// </summary>
    private static readonly Dictionary<string, int> supportedSignOptions = new Dictionary<string, int>
    {
        // Certificate selection options
        { "/a", 0 },
        { "/c", 1 },
        { "/i", 1 },
        { "/n", 1 },
        { "/r", 1 },
        { "/s", 1 },
        { "/sm", 0 },
        { "/sha1", 1 },
        { "/fd", 1 },
        { "/u", 1 },
        { "/uw", 0 },
        // Private Key selection options
        { "/csp", 1 },
        { "/kc", 1 },
        // Signing parameter options
        { "/as", 0 },
        { "/d", 1 },
        { "/du", 1 },
        { "/t", 1 },
        { "/tr", 1 },
        { "/tseal", 1 },
        { "/td", 1 },
        // I'm not sure if I correctly interpret: "This option may be given multiple times.
        { "/sa", 2 },
        { "/seal", 0 },
        { "/itos", 0 },
        { "/force", 0 },
        { "/nosealwarn", 0 },
        // Other options
        { "/ph", 0 },
        { "/nph", 0 },
        { "/rmc", 0 },
        { "/q", 0 },
        { "/v", 0 },
        { "/debug", 0 }
    };

    /// <summary>
    /// Dictionary of unsupported sign options with their parameter count
    /// </summary>
    private static readonly Dictionary<string, int> unsupportedSignOptions = new Dictionary<string, int>()
    {
        // Certificate selection options
        { "/ac", 1 },
        { "/f", 1 },
        { "/p", 1 },
        // Digest options
        { "/dg", 1 },
        { "/ds", 0 },
        { "/di", 1 },
        { "/dxml", 0 },
        { "/dlib", 1 },
        { "/dmdf", 1 },
        // PKCS7 options
        { "/p7", 1 },
        { "/p7co", 1 },
        { "/p7ce", 1 }
    };

    /// <summary>
    /// Main entry point for the application.
    /// Processes command-line arguments and coordinates the signing process.
    /// </summary>
    /// <param name="args">Command-line arguments</param>
    /// <returns>Exit code indicating success or failure</returns>
    static int Main(string[] args)
    {
        if (!args.Any())
        {
            Logger.Error("Invalid number of arguments");
            return ErrorCodes.NoArguments;
        }

        if (args[0] != "sign")
        {
            Logger.Error("Remote signtool supports only sign command");
            return ErrorCodes.UnsupportedCommand;
        }

        var serverBaseAddress = ConfigurationManager.AppSettings["ServerBaseUrl"];
        if (string.IsNullOrWhiteSpace(serverBaseAddress))
        {
            Logger.Error("ServerBaseUrl is not configured in App.config");
            return 2;
        }

        var filesToSign = new List<string>();
        var fileNameToDirectoryLookup = new Dictionary<string, string>();
        var signSubcommands = new List<string>();
        for (int i = 1; i < args.Length; i++)
        {
            if (unsupportedSignOptions.ContainsKey(args[i]))
            {
                Logger.Error("Subcommand: {0} is not supported", args[i]);
                return ErrorCodes.UnsupportedSubcommand;
            }

            if (supportedSignOptions.ContainsKey(args[i]))
            {
                for (int j = i; j <= i + supportedSignOptions[args[i]]; j++)
                {
                    // Save supported sign subcommands for furture use
                    signSubcommands.Add(args[j].Any(char.IsWhiteSpace) ? string.Format("\"{0}\"", args[j]) : args[j]);
                }

                i += supportedSignOptions[args[i]];
                continue;
            }

            if (args[i].StartsWith(@"/"))
            {
                Logger.Error("Unknown subcommand: {0}", args[i]);
                return ErrorCodes.UnknownSubcommand;
            }

            var directoryName = Path.GetDirectoryName(args[i]);
            if (string.IsNullOrEmpty(directoryName))
            {
                directoryName = ".";
            }

            // Path.GetFileName(args[i]) - it doesn't have to be exact file name, e.g. *.msi
            var matchingFiles = Directory.GetFiles(directoryName, Path.GetFileName(args[i]));

            foreach (var matchingFilePath in matchingFiles)
            {
                // This is exact file name
                var matchingFileName = Path.GetFileName(matchingFilePath);
                if (!fileNameToDirectoryLookup.ContainsKey(matchingFileName))
                {
                    filesToSign.Add(matchingFilePath);
                    fileNameToDirectoryLookup.Add(matchingFileName, directoryName);
                }
                else
                {
                    Logger.Error("Current version doesn't support multiple files with the same name for single signature.");
                    Logger.Error("File names: {0}", matchingFilePath);
                    return ErrorCodes.MultipleFilesWithTheSameNameNotSupported;
                }
            }
        }

        Directory.CreateDirectory(TempDirectoryName);
        var archiveToUploadName = string.Format("{0}.zip", Path.GetRandomFileName());
        var archiveToUploadPath = Path.Combine(TempDirectoryName, archiveToUploadName);
        
        CreateZipArchive(filesToSign, archiveToUploadPath);

        string signedArchivePath;
        try
        {
            Logger.Info($"Uploading zip file: {archiveToUploadName}, containing files: {string.Join(", ", filesToSign.ToArray())}");
            signedArchivePath = CommunicateWithServer(archiveToUploadPath, string.Join(" ", signSubcommands)).Result;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to communicate with server");
            return ErrorCodes.ServerCommunicationFailed;
        }
        finally
        {
            File.Delete(archiveToUploadPath);
        }

        if (!string.IsNullOrEmpty(signedArchivePath))
        {
            var targetDirectoryName = signedArchivePath.Substring(0, signedArchivePath.Length - 4);
            
            ExtractZipArchive(signedArchivePath, targetDirectoryName);

            foreach (var signedFile in Directory.GetFiles(targetDirectoryName))
            {
                var signedFileNameWithoutDir = Path.GetFileName(signedFile);
                File.Copy(signedFile, Path.Combine(fileNameToDirectoryLookup[signedFileNameWithoutDir], signedFileNameWithoutDir), true);
            }

            Directory.Delete(targetDirectoryName, true);
            File.Delete(signedArchivePath);
            return ErrorCodes.Ok;
        }
        else
        {
            return ErrorCodes.SignToolInvalidExitCode;
        }
    }

    /// <summary>
    /// Creates a zip archive from the specified files.
    /// </summary>
    /// <param name="filesToAdd">List of file paths to include in the archive</param>
    /// <param name="archivePath">Output path for the zip archive</param>
    private static void CreateZipArchive(List<string> filesToAdd, string archivePath)
    {
        using var zipOutputStream = new ZipOutputStream(File.Create(archivePath));
        zipOutputStream.SetLevel(9); // Maximum compression
        
        byte[] buffer = new byte[4096];
        
        foreach (var filePath in filesToAdd)
        {
            var fileName = Path.GetFileName(filePath);
            var entry = new ZipEntry(fileName);
            entry.DateTime = File.GetLastWriteTime(filePath);
            zipOutputStream.PutNextEntry(entry);
            
            using var fs = File.OpenRead(filePath);
            int sourceBytes;
            do
            {
                sourceBytes = fs.Read(buffer, 0, buffer.Length);
                zipOutputStream.Write(buffer, 0, sourceBytes);
            } while (sourceBytes > 0);
        }
        
        zipOutputStream.Finish();
    }

    /// <summary>
    /// Extracts a zip archive to the specified directory.
    /// </summary>
    /// <param name="archivePath">Path to the zip archive</param>
    /// <param name="outputDirectory">Directory to extract files to</param>
    private static void ExtractZipArchive(string archivePath, string outputDirectory)
    {
        using var fs = File.OpenRead(archivePath);
        using var zipInputStream = new ZipInputStream(fs);
        
        Directory.CreateDirectory(outputDirectory);
        
        ZipEntry entry;
        while ((entry = zipInputStream.GetNextEntry()) != null)
        {
            if (string.IsNullOrEmpty(entry.Name) || entry.IsDirectory)
                continue;
                
            var outputPath = Path.Combine(outputDirectory, entry.Name);
            
            using var outputStream = File.Create(outputPath);
            byte[] buffer = new byte[4096];
            int size;
            while ((size = zipInputStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                outputStream.Write(buffer, 0, size);
            }
        }
    }

    /// <summary>
    /// Communicates with the signing server to upload files, initiate signing, and download results.
    /// </summary>
    /// <param name="archivePath">Path to the archive to upload</param>
    /// <param name="signSubcommands">Signing commands to pass to the server</param>
    /// <returns>Path to the downloaded signed archive, or null if signing failed</returns>
    private static async Task<string> CommunicateWithServer(string archivePath, string signSubcommands)
    {
        using (var progressHandler = new ProgressMessageHandler())
        {
            progressHandler.HttpSendProgress += SendProgressHandler;
            progressHandler.HttpReceiveProgress += ReceiveProgressHandler;

            using (var client = HttpClientFactory.Create(progressHandler))
            {
                var archiveName = Path.GetFileName(archivePath);
                var serverBaseAddress = ConfigurationManager.AppSettings["ServerBaseUrl"];
                client.BaseAddress = new Uri(serverBaseAddress);
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using (var content = new MultipartFormDataContent())
                {
                    var fileContent = new StreamContent(File.OpenRead(archivePath));
                    fileContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
                    {
                        FileName = Path.GetFileName(archivePath)
                    };
                    content.Add(fileContent);

                    var requestUri = "api/upload/save";
                    var uploadResponse = await client.PostAsync(requestUri, content);

                    if (!uploadResponse.IsSuccessStatusCode)
                    {
                        await ShowErrorAsync(uploadResponse);
                        return null;
                    }
                }

                var signRequestDto = new SignDto()
                {
                    ArchiveName = archiveName,
                    SignSubcommands = signSubcommands
                };

                Logger.Info($"Perform sign for: {archiveName}, using commands: {signSubcommands}");

                var signResponse = await client.PostAsJsonAsync("api/signtool/sign", signRequestDto);
                if (!signResponse.IsSuccessStatusCode)
                {
                    await ShowErrorAsync(signResponse);
                    return null;
                }

                var signResponseDto = await signResponse.Content.ReadAsAsync<SignResultDto>();
                if (signResponseDto.ExitCode != 0)
                {
                    Logger.Error("signtool.exe exited with code: {0}", signResponseDto.ExitCode);
                    Logger.Error(signResponseDto.StandardOutput);
                    Logger.Error(signResponseDto.StandardError);
                    return null;
                }

                Logger.Info($"Begin to download signed archive: {archiveName}");

                var downloadReponse = await client.GetStreamAsync(signResponseDto.DownloadUrl);
                var signedArchiveName = Path.GetFileName(signResponseDto.DownloadUrl);
                var signedArchivePath = Path.Combine(TempDirectoryName, signedArchiveName);

                using (var fileStream = File.Create(signedArchivePath))
                {
                    await downloadReponse.CopyToAsync(fileStream);
                }

                Logger.Info($"Delete archives {archiveName}, {signedArchiveName} from server");

                await client.PostAsJsonAsync("api/upload/remove", new List<string>() { archiveName, signedArchiveName });
                return signedArchivePath;
            }
        }
    }

    /// <summary>
    /// Shows error details from an HTTP response.
    /// </summary>
    /// <param name="response">The HTTP response message containing error details</param>
    /// <returns>Task representing the asynchronous operation</returns>
    private static async Task ShowErrorAsync(HttpResponseMessage response)
    {
        Logger.Error("Status code: {0}", response.StatusCode);
        Logger.Error(await response.Content.ReadAsStringAsync());
    }
    
    /// <summary>
    /// Handler for HTTP send progress events.
    /// </summary>
    /// <param name="sender">Event sender</param>
    /// <param name="e">Event arguments containing progress information</param>
    private static void SendProgressHandler(object sender, HttpProgressEventArgs e)
    {
        LogProgress("Sending:", e);
    }

    /// <summary>
    /// Handler for HTTP receive progress events.
    /// </summary>
    /// <param name="sender">Event sender</param>
    /// <param name="e">Event arguments containing progress information</param>
    private static void ReceiveProgressHandler(object sender, HttpProgressEventArgs e)
    {
        LogProgress("Receive:", e);
    }

    /// <summary>
    /// Logs HTTP operation progress information.
    /// </summary>
    /// <param name="prefix">Operation type prefix (Sending/Receive)</param>
    /// <param name="e">Event arguments containing progress information</param>
    private static void LogProgress(string prefix, HttpProgressEventArgs e)
    {
        Logger.Info($"{prefix} ({e.ProgressPercentage}%) Transfered: {e.BytesTransferred}, Total: {e.TotalBytes}");
    }
}
