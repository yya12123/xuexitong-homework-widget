using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace XxtWidget
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\XuexitongHomeworkWidget", out created))
            {
                if (!created) return;
                string dir = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
                Log.FilePath = Path.Combine(dir, "app.log");
                Log.Write("start");
                try
                {
                    RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
                    Application app = new Application();
                    app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                    app.DispatcherUnhandledException += App_DispatcherUnhandledException;
                    AppConfig config = ConfigStore.Load(Path.Combine(dir, "config.json"));
                    WidgetWindow window = new WidgetWindow(dir, config);
                    app.MainWindow = window;
                    app.Run(window);
                }
                catch (Exception ex)
                {
                    Log.Write("fatal: " + ex);
                    MessageBox.Show("学习通作业悬浮窗启动失败：" + Environment.NewLine + ex.Message, "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private static void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Write("unhandled: " + e.Exception);
            e.Handled = true;
        }
    }
}
