using System.Windows;

namespace ScrollSearch.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        if (args.Contains("--self-test"))
        {
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var output = args.SkipWhile(a => a != "--output").Skip(1).FirstOrDefault() ?? Path.Combine(Path.GetTempPath(), "ScrollScreenSearch-acceptance");
            app.Startup += async (_, _) => { var code = await Acceptance.Run(output); app.Shutdown(code); };
            return app.Run();
        }
        app.DispatcherUnhandledException += (_, e) =>
        {
            MessageBox.Show("发生错误：" + e.Exception.Message + "\n应用将退出；已保存的历史保留在本机。", "屏幕找字器");
            app.Shutdown(1); e.Handled = true;
        };
        app.MainWindow = new MainWindow();
        return app.Run(app.MainWindow);
    }
}
