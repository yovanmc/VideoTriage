using System.Diagnostics;
using Microsoft.Win32;

namespace VideoTriage.App.Services;

public sealed class ExplorerLauncher(
    Func<ProcessStartInfo, Process?>? processStarter = null) : IExplorerLauncher
{
    private readonly Func<ProcessStartInfo, Process?> _start =
        processStarter ?? Process.Start;

    public void Open(string path)
    {
        using var process = _start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}

public sealed class DialogService(IExplorerLauncher explorer) : IDialogService
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

    public void OpenDirectory(string path) => explorer.Open(path);
}
