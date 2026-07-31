using Memogram.Services.Telegram;
using Microsoft.Extensions.Logging;
using MimeDetective;
using SerilogTimings;
using System.Net.Mime;

namespace Memogram.Services.MimeTypeDetectors;

public class MimeDetectiveMimeTypeDetector : IMimeTypeDetector
{
    private readonly ILogger<TelegramService> _logger;
    private IContentInspector _contentInspector;

    public MimeDetectiveMimeTypeDetector(ILogger<TelegramService> logger)
    {
        _logger = logger;
        using (var op = Operation.Time("Loading MimeDetective definitions"))
        {
            _contentInspector = new ContentInspectorBuilder { Definitions = MimeDetective.Definitions.DefaultDefinitions.All() }.Build();
        }
    }
    public string Detect(string? filepath, Stream? stream)
    {
        if (!string.IsNullOrEmpty(filepath))
            return Detect(filepath);
        else if (null != stream)
            return Detect(stream);

        return MediaTypeNames.Application.Octet;
    }

    public string Detect(string filepath)
    {
        string? extension = Path.GetExtension(filepath);
        if (string.IsNullOrEmpty(extension))
            return MediaTypeNames.Application.Octet;

        var cleanExt = extension.TrimStart('.').ToLower();

        var matchedType = MimeDetective.Definitions.DefaultDefinitions.All()
            .FirstOrDefault(d => d.File.Extensions.Contains(cleanExt));

        return matchedType?.File.MimeType ?? MediaTypeNames.Application.Octet;
    }

    public string Detect(Stream stream)
    {
        var bestMatch = _contentInspector.Inspect(stream).ByMimeType().FirstOrDefault();
        var contentType = null != bestMatch && !string.IsNullOrEmpty(bestMatch.MimeType)
            ? bestMatch.MimeType
            : MediaTypeNames.Application.Octet;

        return contentType;
    }

}