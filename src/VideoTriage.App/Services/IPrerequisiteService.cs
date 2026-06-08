namespace VideoTriage.App.Services;

public interface IPrerequisiteService
{
    IReadOnlyList<ToolPrerequisiteStatus> Check();
}
