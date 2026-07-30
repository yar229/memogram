namespace Memogram.Configs;

public class LocalStorageConfig : IValidableConfig
{
    public static string SectionName => "LocalStorage";

    public string Filename { get; set; } = "data.txt";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Filename))
            Filename = "data.txt";

        if (!File.Exists(Filename))
            File.WriteAllText(Filename, string.Empty);

        Filename = Path.GetFullPath(Filename);
    }
}


