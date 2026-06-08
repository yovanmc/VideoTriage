using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VideoTriage.App.Models;
using VideoTriage.App.Services;
using VideoTriage.Core.Models;

namespace VideoTriage.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _store;
    private double _candidateBppThreshold;
    private DeleteMode _deleteMode;
    private bool _deepVerify;
    private bool _embedPoster;
    private double _minimumFreeGigabytes;
    private bool _dryRun;
    private bool _confirmPermanentDelete;

    public SettingsViewModel(ISettingsStore store)
    {
        _store = store;
        var settings = store.Load();
        _candidateBppThreshold = settings.CandidateBppThreshold;
        _deleteMode = settings.DeleteMode;
        _deepVerify = settings.DeepVerify;
        _embedPoster = settings.EmbedPoster;
        _minimumFreeGigabytes = settings.MinimumFreeGigabytes;
        _dryRun = settings.DryRun;
        SaveCommand = new RelayCommand(Save, () => CanSave);
    }

    public IReadOnlyList<DeleteMode> DeleteModes { get; } = Enum.GetValues<DeleteMode>();
    public IRelayCommand SaveCommand { get; }

    public double CandidateBppThreshold
    {
        get => _candidateBppThreshold;
        set => SetValidatedProperty(ref _candidateBppThreshold, value);
    }

    public DeleteMode DeleteMode
    {
        get => _deleteMode;
        set => SetValidatedProperty(ref _deleteMode, value);
    }

    public bool DeepVerify
    {
        get => _deepVerify;
        set => SetValidatedProperty(ref _deepVerify, value);
    }

    public bool EmbedPoster
    {
        get => _embedPoster;
        set => SetValidatedProperty(ref _embedPoster, value);
    }

    public double MinimumFreeGigabytes
    {
        get => _minimumFreeGigabytes;
        set => SetValidatedProperty(ref _minimumFreeGigabytes, value);
    }

    public bool DryRun
    {
        get => _dryRun;
        set => SetValidatedProperty(ref _dryRun, value);
    }

    public bool ConfirmPermanentDelete
    {
        get => _confirmPermanentDelete;
        set => SetValidatedProperty(ref _confirmPermanentDelete, value);
    }

    public string? ValidationMessage
    {
        get
        {
            if (CandidateBppThreshold is <= 0 or > 1)
                return "Candidate BPP threshold must be greater than 0 and at most 1.";
            if (MinimumFreeGigabytes < 1)
                return "Minimum free space must be at least 1 GB.";
            if (DeleteMode == DeleteMode.Permanent && !ConfirmPermanentDelete)
                return "Confirm permanent deletion before saving or starting.";
            return null;
        }
    }

    public bool CanSave => ValidationMessage is null;
    public bool CanRun => CanSave;

    public TriageOptions ToTriageOptions() => CurrentSettings().ToTriageOptions();

    private void Save() => _store.Save(CurrentSettings());

    private AppSettings CurrentSettings() => new()
    {
        CandidateBppThreshold = CandidateBppThreshold,
        DeleteMode = DeleteMode,
        DeepVerify = DeepVerify,
        EmbedPoster = EmbedPoster,
        MinimumFreeGigabytes = MinimumFreeGigabytes,
        DryRun = DryRun
    };

    private void SetValidatedProperty<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName))
            return;

        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanRun));
        SaveCommand.NotifyCanExecuteChanged();
    }
}
