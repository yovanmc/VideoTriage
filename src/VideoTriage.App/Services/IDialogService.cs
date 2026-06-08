namespace VideoTriage.App.Services;

public interface IDialogService
{
    string? ChooseFolder(string? initialFolder);
    void OpenDirectory(string path);
}
