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
    private readonly IContentInspector _contentInspector;
    private readonly IReadOnlyDictionary<string, string> _extensionToMimeType;

    public MimeDetectiveMimeTypeDetector(ILogger<MimeDetectiveMimeTypeDetector> logger)
    {
        _logger = logger;
        using (var op = Operation.Time("Loading MimeDetective definitions"))
        {
            var definitions = MimeDetective.Definitions.DefaultDefinitions.All();
            _contentInspector = new ContentInspectorBuilder { Definitions = definitions }.Build();
            _extensionToMimeType = BuildExtensionMap(definitions);
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

        var cleanExt = extension.TrimStart('.');

        return _extensionToMimeType.TryGetValue(cleanExt, out var mimeType)
            ? mimeType
            : MediaTypeNames.Application.Octet;
    }

    public string Detect(Stream stream)
    {
        var bestMatch = _contentInspector.Inspect(stream).ByMimeType().FirstOrDefault();
        var contentType = null != bestMatch && !string.IsNullOrEmpty(bestMatch.MimeType)
            ? bestMatch.MimeType
            : MediaTypeNames.Application.Octet;

        return contentType;
    }

    private static IReadOnlyDictionary<string, string> BuildExtensionMap(ImmutableArray<Definition> definitions)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions)
        {
            if (string.IsNullOrEmpty(definition.File.MimeType))
                continue;

            foreach (var extension in definition.File.Extensions)
                map.TryAdd(extension, definition.File.MimeType);
        }
        return map;
    }
}
