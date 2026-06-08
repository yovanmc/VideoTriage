using VideoTriage.App.Models;

namespace VideoTriage.App.Services;

public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
}
