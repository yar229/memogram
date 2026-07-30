namespace Memogram.Configs;

public interface IValidableConfig
{
    void Validate();

    static abstract string SectionName { get; }
}
