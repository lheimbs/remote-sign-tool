using Microsoft.AspNetCore.Mvc;
using NLog;

namespace RemoteSignTool.Server.Controllers;

/// <summary>
/// Controller for handling file uploads and downloads.
/// </summary>
[Route("api/upload")]
[ApiController]
public class UploadController : ControllerBase
{
    /// <summary>
    /// The directory where uploaded files are stored.
    /// </summary>
    public const string UploadDirectoryName = "Upload";

    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Initializes a new instance of the <see cref="UploadController"/> class.
    /// </summary>
    public UploadController()
    {
        // Ensure the upload directory exists
        Directory.CreateDirectory(UploadDirectoryName);
    }

    /// <summary>
    /// Downloads a file from the server.
    /// </summary>
    /// <param name="fileName">The name of the file to download.</param>
    /// <returns>The file to be downloaded, or NotFound if the file does not exist.</returns>
    [HttpGet("download/{fileName}")]
    public IActionResult Download(string fileName)
    {
        var filePath = Path.Combine(UploadDirectoryName, fileName);

        if (!System.IO.File.Exists(filePath))
        {
            Logger.Warn("File not found for download: {FileName}", fileName);
            return NotFound();
        }

        Logger.Info("Downloading file: {FileName}", fileName);

        var bytes = System.IO.File.ReadAllBytes(filePath);
        return File(bytes, "application/octet-stream", fileName);
    }

    /// <summary>
    /// Saves uploaded files to the server's file system.
    /// </summary>
    /// <returns>An IActionResult indicating the result of the operation.</returns>
    [HttpPost("save")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
    public async Task<IActionResult> Save()
    {
        try
        {
            // Ensure we've read the form data completely
            var form = await Request.ReadFormAsync();
            
            var httpRequest = HttpContext.Request;

            if (httpRequest.Form.Files == null || httpRequest.Form.Files.Count == 0)
            {
                Logger.Warn("No file sent in the request.");
                return BadRequest("No file sent in the request.");
            }

            var savedFiles = new List<string>();
            foreach (var file in httpRequest.Form.Files)
            {
                if (file.Length > 0)
                {
                    var fileName = Path.GetFileName(file.FileName);
                    var filePath = Path.Combine(UploadDirectoryName, fileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }

                    savedFiles.Add(fileName);
                    Logger.Info("File saved successfully: {FilePath}", filePath);
                }
            }

            return Ok(new { Message = $"Successfully saved {savedFiles.Count} files", Files = savedFiles });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error saving file.");
            return StatusCode(500, new { Error = "Error saving file.", Details = ex.Message });
        }
    }

    /// <summary>
    /// Removes a file from the server.
    /// </summary>
    /// <param name="fileNames">A list of file names to remove.</param>
    /// <returns>Ok if the removal is successful.</returns>
    [HttpPost("remove")]
    public IActionResult Remove([FromBody] List<string> fileNames)
    {
        if (fileNames != null)
        {
            foreach (var fileName in fileNames)
            {
                var filePath = Path.Combine(UploadDirectoryName, fileName);
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                    Logger.Info("File removed: {FilePath}", filePath);
                }
                else
                {
                    Logger.Warn("File not found for removal: {FilePath}", filePath);
                }
            }
        }

        return Ok();
    }
}
