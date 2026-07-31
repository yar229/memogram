using Microsoft.AspNetCore.StaticFiles;
using System.Net.Mime;

namespace Memogram.Services.MimeTypeDetectors;

public class FileExtensionMimeTypeDetector : IMimeTypeDetector
{
    private readonly FileExtensionContentTypeProvider _provider = new FileExtensionContentTypeProvider();

    public string Detect(string? filename, Stream? stream) 
        => _provider.TryGetContentType(filename, out string mimeType)
            ? mimeType
            : MediaTypeNames.Application.Octet;
}
