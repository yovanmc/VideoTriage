using Shouldly;
using VideoTriage.App.Models;
using VideoTriage.App.Services;
using VideoTriage.App.ViewModels;
using VideoTriage.Core.Models;

namespace VideoTriage.App.Tests.ViewModels;

public sealed class SettingsViewModelTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1.1)]
    public void CanSave_InvalidThreshold_ReturnsFalse(double threshold)
    {
        var vm = new SettingsViewModel(new FakeSettingsStore())
        {
            CandidateBppThreshold = threshold
        };

        vm.CanSave.ShouldBeFalse();
        vm.ValidationMessage.ShouldNotBeNull().ShouldContain("threshold");
        vm.SaveCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void CanSave_InsufficientFreeSpace_ReturnsFalse()
    {
        var vm = new SettingsViewModel(new FakeSettingsStore())
        {
            MinimumFreeGigabytes = 0.5
        };

        vm.CanSave.ShouldBeFalse();
        vm.ValidationMessage.ShouldNotBeNull().ShouldContain("free space");
    }

    [Fact]
    public void CanSave_PermanentDeleteWithoutConfirmation_ReturnsFalse()
    {
        var vm = new SettingsViewModel(new FakeSettingsStore())
        {
            DeleteMode = DeleteMode.Permanent,
            ConfirmPermanentDelete = false
        };

        vm.CanSave.ShouldBeFalse();
        vm.CanRun.ShouldBeFalse();
    }

    [Fact]
    public void LoadedPermanentDelete_RequiresFreshSessionConfirmation()
    {
        var store = new FakeSettingsStore
        {
            Loaded = new AppSettings { DeleteMode = DeleteMode.Permanent }
        };

        var vm = new SettingsViewModel(store);

        vm.ConfirmPermanentDelete.ShouldBeFalse();
        vm.CanRun.ShouldBeFalse();
    }

    [Fact]
    public void SaveCommand_ValidSettings_Persists()
    {
        var store = new FakeSettingsStore();
        var vm = new SettingsViewModel(store) { DryRun = true };

        vm.SaveCommand.Execute(null);

        store.Saved.ShouldNotBeNull();
        store.Saved.DryRun.ShouldBeTrue();
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        public AppSettings Loaded { get; set; } = new();
        public AppSettings? Saved { get; private set; }
        public AppSettings Load() => Loaded;
        public void Save(AppSettings settings) => Saved = settings;
    }
}
