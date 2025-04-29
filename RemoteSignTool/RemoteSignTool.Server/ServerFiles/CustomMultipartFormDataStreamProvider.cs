using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace RemoteSignTool.Server.ServerFiles;

/// <summary>
/// Provides a custom implementation to handle multipart form data.
/// </summary>
public class CustomMultipartFormDataStreamProvider
{
    private readonly string _targetDirectory;
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomMultipartFormDataStreamProvider"/> class.
    /// </summary>
    /// <param name="targetDirectory">The directory to store uploaded files.</param>
    /// <param name="httpContextAccessor">The HttpContextAccessor to access the current HttpContext.</param>
    public CustomMultipartFormDataStreamProvider(string targetDirectory, IHttpContextAccessor httpContextAccessor)
    {
        if (string.IsNullOrEmpty(targetDirectory))
        {
            throw new ArgumentNullException(nameof(targetDirectory));
        }

        _targetDirectory = targetDirectory;
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));

        if (!Directory.Exists(_targetDirectory))
        {
            Directory.CreateDirectory(_targetDirectory);
        }
    }

    /// <summary>
    /// Reads the multipart body asynchronously.
    /// </summary>
    /// <returns>A list of file names.</returns>
    public async Task<List<string>> ReadMultipartBodyAsync()
    {
        var request = _httpContextAccessor.HttpContext.Request;

        if (!MultipartRequestHelper.IsMultipartContentType(request.ContentType))
        {
            throw new InvalidDataException($"Expected a multipart request, but got '{request.ContentType}'");
        }

        var boundary = MultipartRequestHelper.GetBoundary(
            MediaTypeHeaderValue.Parse(request.ContentType),
            100 // 100 characters limit
        );

        var reader = new MultipartReader(boundary, request.Body);
        MultipartSection? section;
        var fileNames = new List<string>();

        while ((section = await reader.ReadNextSectionAsync()) != null)
        {
            var disposition = section.ContentDisposition;

            if (disposition != null)
            {
                ContentDispositionHeaderValue? contentDispositionHeader;
                if (ContentDispositionHeaderValue.TryParse(disposition, out contentDispositionHeader))
                {
                    if (contentDispositionHeader.DispositionType.Equals("form-data") &&
                        contentDispositionHeader.FileName.HasValue)
                    {
                        var fileName = Path.GetFileName(contentDispositionHeader.FileName.ToString());
                        var filePath = Path.Combine(_targetDirectory, fileName);

                        using (var targetStream = File.Create(filePath))
                        {
                            await section.Body.CopyToAsync(targetStream);
                        }

                        fileNames.Add(fileName);
                    }
                }
            }
        }

        return fileNames;
    }

    private static class MultipartRequestHelper
    {
        /// <summary>
        /// Determines if the content type is a multipart request.
        /// </summary>
        /// <param name="contentType">The content type.</param>
        /// <returns>True if the content type is a multipart request, otherwise false.</returns>
        public static bool IsMultipartContentType(string? contentType)
        {
            return !string.IsNullOrEmpty(contentType)
                   && contentType.IndexOf("multipart/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Gets the boundary from the multipart request.
        /// </summary>
        /// <param name="contentType">The content type.</param>
        /// <param name="lengthLimit">The length limit.</param>
        /// <returns>The boundary.</returns>
        public static string GetBoundary(MediaTypeHeaderValue contentType, int lengthLimit)
        {
            var boundary = HeaderUtilities.RemoveQuotes(contentType.Boundary).ToString();

            if (string.IsNullOrEmpty(boundary))
            {
                throw new InvalidDataException("Missing content-type boundary.");
            }

            if (boundary.Length > lengthLimit)
            {
                throw new InvalidDataException($"Multipart boundary length limit {lengthLimit} exceeded.");
            }

            return boundary;
        }
    }
}
