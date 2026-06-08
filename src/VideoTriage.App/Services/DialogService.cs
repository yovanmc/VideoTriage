using Microsoft.Win32;

namespace VideoTriage.App.Services;

public sealed class DialogService : IDialogService
{
    public string? ChooseFolder(string? initialFolder)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose a folder containing videos",
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(initialFolder))
            dialog.InitialDirectory = initialFolder;

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
