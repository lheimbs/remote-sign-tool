using Microsoft.AspNetCore.Mvc;
using NLog;
using RemoteSignTool.Common.Dto;
using RemoteSignTool.Server.Services;
using ICSharpCode.SharpZipLib.Zip;
using System.IO;
using System.Threading.Tasks;

namespace RemoteSignTool.Server.Controllers;

/// <summary>
/// Controller for handling signing tool operations.
/// </summary>
[Route("api/signtool")]
[ApiController]
public class SignToolController : ControllerBase
{
    private const string TempDirectoryName = "Temp";
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly ISignToolService _signToolService;

    /// <summary>
    /// Initializes a new instance of the <see cref="SignToolController"/> class.
    /// </summary>
    /// <param name="signToolService">The signing tool service.</param>
    public SignToolController(ISignToolService signToolService)
    {
        _signToolService = signToolService;
    }

    /// <summary>
    /// Pings the controller to check if it's alive.
    /// </summary>
    /// <returns>An IActionResult indicating success.</returns>
    [HttpGet("ping")]
    public IActionResult Ping()
    {
        Logger.Info("Ping received");
        return Ok();
    }

    /// <summary>
    /// Signs a file using the provided signing tool.
    /// </summary>
    /// <param name="dto">The signing data transfer object.</param>
    /// <returns>An IActionResult containing the signing result.</returns>
    [HttpPost("sign")]
    public async Task<IActionResult> Sign([FromBody] SignDto dto)
    {
        Logger.Info("Start signing files");

        var archivePath = Path.Combine(UploadController.UploadDirectoryName, dto.ArchiveName);
        if (!System.IO.File.Exists(archivePath))
        {
            Logger.Warn("Archive has not been found: {ArchiveName}", dto.ArchiveName);
            return BadRequest($"Archive has not been found: {dto.ArchiveName}");
        }

        var extractionDirectoryName = Path.Combine(TempDirectoryName, Path.GetRandomFileName());
        Directory.CreateDirectory(extractionDirectoryName);
        Logger.Info("Directory created: {DirectoryName}", extractionDirectoryName);

        // Extract the archive using SharpZipLib
        ExtractZipArchive(archivePath, extractionDirectoryName);
        Logger.Info("Files have been extracted to: {DirectoryName}", extractionDirectoryName);

        string signToolPath;
        if (!_signToolService.TryToFindSignToolPath(out signToolPath))
        {
            Logger.Error("SignTool is not installed");
            return StatusCode(500, new FileNotFoundException("SignTool is not installed", "signtool.exe"));
        }

        var signResult = await _signToolService.Sign(signToolPath, dto.SignSubcommands, extractionDirectoryName);

        var signedArchiveName = string.Format("{0}_signed.zip", Path.GetFileNameWithoutExtension(dto.ArchiveName));
        if (signResult.ExitCode == 0)
        {
            Logger.Info("SignTool successfully signed files");
            // Create archive with signed files
            CreateZipArchive(Directory.GetFiles(extractionDirectoryName), 
                Path.Combine(UploadController.UploadDirectoryName, signedArchiveName));
            Logger.Info("Archive with signed files created: {ArchiveName}", signedArchiveName);
        }
        else
        {
            Logger.Error("SignTool exited with code: {ExitCode}", signResult.ExitCode);
            Logger.Error(signResult.StandardError);
        }

        var result = new SignResultDto()
        {
            ExitCode = signResult.ExitCode,
            StandardOutput = signResult.StandardOutput,
            StandardError = signResult.StandardError,
            DownloadUrl = signResult.ExitCode == 0 ? Url.Link("DownloadApi", new { fileName = Uri.EscapeDataString(signedArchiveName) }) : null
        };

        return Ok(result);
    }

    /// <summary>
    /// Extracts a zip archive to the specified directory.
    /// </summary>
    /// <param name="archivePath">Path to the zip archive.</param>
    /// <param name="outputDirectory">Directory to extract files to.</param>
    private static void ExtractZipArchive(string archivePath, string outputDirectory)
    {
        using var fs = System.IO.File.OpenRead(archivePath);
        using var zipInputStream = new ZipInputStream(fs);
        
        ZipEntry entry;
        while ((entry = zipInputStream.GetNextEntry()) != null)
        {
            if (string.IsNullOrEmpty(entry.Name) || entry.IsDirectory)
                continue;
                
            var outputPath = Path.Combine(outputDirectory, entry.Name);
            
            // Create directory if it doesn't exist
            var directoryName = Path.GetDirectoryName(outputPath);
            if (directoryName != null && !Directory.Exists(directoryName))
            {
                Directory.CreateDirectory(directoryName);
            }
            
            using var outputStream = System.IO.File.Create(outputPath);
            byte[] buffer = new byte[4096];
            int size;
            while ((size = zipInputStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                outputStream.Write(buffer, 0, size);
            }
        }
    }

    /// <summary>
    /// Creates a zip archive from the specified files.
    /// </summary>
    /// <param name="filesToAdd">Array of file paths to include in the archive.</param>
    /// <param name="archivePath">Output path for the zip archive.</param>
    private static void CreateZipArchive(string[] filesToAdd, string archivePath)
    {
        using var zipOutputStream = new ZipOutputStream(System.IO.File.Create(archivePath));
        zipOutputStream.SetLevel(9); // Maximum compression
        
        byte[] buffer = new byte[4096];
        
        foreach (var filePath in filesToAdd)
        {
            var fileName = Path.GetFileName(filePath);
            var entry = new ZipEntry(fileName);
            entry.DateTime = System.IO.File.GetLastWriteTime(filePath);
            zipOutputStream.PutNextEntry(entry);
            
            using var fs = System.IO.File.OpenRead(filePath);
            int sourceBytes;
            do
            {
                sourceBytes = fs.Read(buffer, 0, buffer.Length);
                zipOutputStream.Write(buffer, 0, sourceBytes);
            } while (sourceBytes > 0);
        }
        
        zipOutputStream.Finish();
    }
}
