namespace Memogram.Services.MimeTypeDetectors;

public interface IMimeTypeDetector
{
    string Detect(string? filename, Stream? stream);
}
