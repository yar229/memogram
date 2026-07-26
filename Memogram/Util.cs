namespace Memogram;

public static class Util
{
    public static string ExtractMemoUidFromName(string name)
    {
        var parts = name.Split('/');
        if (parts.Length != 2 || parts[0] != "memos" || string.IsNullOrEmpty(parts[1]))
        {
            throw new ArgumentException($"Invalid memo name: {name}");
        }
        return parts[1];
    }
}
