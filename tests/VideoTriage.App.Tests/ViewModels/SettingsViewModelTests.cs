using System.ComponentModel;
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
    public void ValidChange_Persists()
    {
        var store = new FakeSettingsStore();
        var vm = new SettingsViewModel(store) { DryRun = true };

        store.Saved.ShouldNotBeNull();
        store.Saved.DryRun.ShouldBeTrue();
    }

    [Fact]
    public void ValidChange_PersistsImmediately()
    {
        var store = new CountingSettingsStore();
        var vm = new SettingsViewModel(store);
        var before = store.SaveCount;
        vm.MinimumFreeGigabytes = 7;
        store.SaveCount.ShouldBeGreaterThan(before);
        store.Last!.MinimumFreeGigabytes.ShouldBe(7);
    }

    [Fact]
    public void InvalidChange_DoesNotPersist_AndFlagsFieldError()
    {
        var store = new CountingSettingsStore();
        var vm = new SettingsViewModel(store);
        var before = store.SaveCount;
        vm.CandidateBppThreshold = 5; // > 1, invalid
        ((INotifyDataErrorInfo)vm).HasErrors.ShouldBeTrue();
        ((INotifyDataErrorInfo)vm).GetErrors(nameof(vm.CandidateBppThreshold))
            .Cast<string>().ShouldNotBeEmpty();
        store.SaveCount.ShouldBe(before);
    }

    [Fact]
    public void ConfirmPermanentDelete_NotPersisted()
    {
        var store = new CountingSettingsStore();
        var vm = new SettingsViewModel(store) { DeleteMode = VideoTriage.Core.Models.DeleteMode.Permanent };
        vm.ConfirmPermanentDelete = true;
        // Confirm flag is a session gate; persisted settings never carry it.
        // (AppSettings has no such field — assert persistence still works without throwing.)
        vm.MinimumFreeGigabytes = 3;
        store.Last.ShouldNotBeNull();
    }

    private sealed class CountingSettingsStore : ISettingsStore
    {
        public int SaveCount { get; private set; }
        public AppSettings? Last { get; private set; }
        public AppSettings Load() => new();
        public void Save(AppSettings settings) { SaveCount++; Last = settings; }
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        public AppSettings Loaded { get; set; } = new();
        public AppSettings? Saved { get; private set; }
        public AppSettings Load() => Loaded;
        public void Save(AppSettings settings) => Saved = settings;
    }
}
