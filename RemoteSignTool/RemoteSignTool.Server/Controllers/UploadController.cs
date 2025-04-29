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
    /// Uploads a file to the server.
    /// </summary>
    /// <returns>Ok if the upload is successful, or BadRequest if there are issues.</returns>
    [HttpPost("save")]
    public async Task<IActionResult> Save()
    {
        try
        {
            var httpRequest = HttpContext.Request;

            if (httpRequest.Form.Files.Count == 0)
            {
                Logger.Warn("No file sent in the request.");
                return BadRequest("No file sent in the request.");
            }

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

                    Logger.Info("File saved successfully: {FilePath}", filePath);
                }
            }

            return Ok();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error saving file.");
            return StatusCode(500, "Error saving file.");
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
