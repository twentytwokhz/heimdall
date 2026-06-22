namespace Heimdall.Application;

/// <summary>Resolves policy implementations by element name; unknown elements fail loud.</summary>
public interface IPolicyRegistry
{
    IPolicy Resolve(string elementName);
    bool IsSupported(string elementName);
}
