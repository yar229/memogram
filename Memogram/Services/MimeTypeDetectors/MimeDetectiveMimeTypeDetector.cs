using Memogram.Services.Telegram;
using Microsoft.Extensions.Logging;
using MimeDetective;
using MimeDetective.Storage;
using SerilogTimings;
using System.Collections.Immutable;
using System.Net.Mime;

namespace Memogram.Services.MimeTypeDetectors;

public class MimeDetectiveMimeTypeDetector : IMimeTypeDetector
{
    private readonly ILogger<MimeDetectiveMimeTypeDetector> _logger;
    private IContentInspector _contentInspector;
    private readonly ImmutableArray<MimeDetective.Storage.Definition> _definitions;
    public MimeDetectiveMimeTypeDetector(ILogger<MimeDetectiveMimeTypeDetector> logger)
    {
        _logger = logger;
        using (var op = Operation.Time("Loading MimeDetective definitions"))
        {
            _definitions = MimeDetective.Definitions.DefaultDefinitions.All();
            _contentInspector = new ContentInspectorBuilder { Definitions = _definitions }.Build();
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

        var matchedType = _definitions
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