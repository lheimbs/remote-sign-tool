using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;

namespace RemoteSignTool.Server.Results;

/// <summary>
/// Represents a file result that can be returned from an API controller.
/// </summary>
public class FileResult : FileContentResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileResult"/> class.
    /// </summary>
    /// <param name="fileContents">The file contents.</param>
    /// <param name="contentType">The content type of the file.</param>
    public FileResult(byte[] fileContents, string contentType) : base(fileContents, contentType)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileResult"/> class.
    /// </summary>
    /// <param name="fileContents">The file contents.</param>
    public FileResult(byte[] fileContents) : base(fileContents, "application/octet-stream")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileResult"/> class from a file path.
    /// </summary>
    /// <param name="filePath">The path to the file.</param>
    public FileResult(string filePath) : base(System.IO.File.ReadAllBytes(filePath), GetContentType(filePath))
    {
    }

    private static string GetContentType(string filePath)
    {
        var provider = new FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(filePath, out var contentType))
        {
            contentType = "application/octet-stream";
        }
        return contentType;
    }
}
