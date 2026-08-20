using System.Threading;
using System.Windows;

namespace CDRIconExtractor.App;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\ByBeiluoguo.CDRIconExtractor.SingleInstance";
    private Mutex? _singleInstanceMutex;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        _ownsMutex = createdNew;

        if (!createdNew)
        {
            MessageBox.Show("CorelDRAW官方图标提取器已经在运行，请勿重复打开。", "CorelDRAW官方图标提取器", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(0);
            return;
        }

        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex)
        {
            try { _singleInstanceMutex?.ReleaseMutex(); }
            catch (ApplicationException) { }
        }

        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
        base.OnExit(e);
    }
}
