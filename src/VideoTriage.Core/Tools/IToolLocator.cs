namespace VideoTriage.Core.Tools;

public interface IToolLocator
{
    string? FindOnPath(string executableName);
    ToolLocation RequireOnPath(string executableName);
}
