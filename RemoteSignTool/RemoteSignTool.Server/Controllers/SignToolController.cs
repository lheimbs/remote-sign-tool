using Microsoft.AspNetCore.Mvc;
using NLog;
using RemoteSignTool.Common.Dto;
using RemoteSignTool.Server.Services;
using Ionic.Zip;

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

        using (ZipFile zip = new ZipFile(archivePath))
        {
            zip.ExtractAll(extractionDirectoryName);
            Logger.Info("Files have been extracted to: {DirectoryName}", extractionDirectoryName);
        }

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
            using (ZipFile zip = new ZipFile())
            {
                // Currently, we flatten the hierarchy of files for sake of simplicity
                zip.AddFiles(Directory.GetFiles(extractionDirectoryName), false, string.Empty);
                zip.Save(Path.Combine(UploadController.UploadDirectoryName, signedArchiveName));
                Logger.Info("Archive with signed files created: {ArchiveName}", signedArchiveName);
            }
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
}
