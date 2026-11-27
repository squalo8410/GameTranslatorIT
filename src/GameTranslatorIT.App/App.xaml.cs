using System.Windows;
using GameTranslatorIT.App.Services;

namespace GameTranslatorIT.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        UpdateCommitService.CommitPendingUpdate();
        base.OnStartup(e);
    }
}
