using System.Windows;
using DMT.Setup.Services;

namespace DMT.Setup;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 2 && e.Args[0] == "--verify-package")
        {
            object result; var code = 0;
            try { result = new { Files = LocalServiceDeployment.VerifyPayload(AppContext.BaseDirectory), Valid = true }; }
            catch (Exception failure) { result = new { Valid = false, Error = failure.Message }; code = 1; }
            using var output = new System.IO.FileStream(e.Args[1], System.IO.FileMode.CreateNew, System.IO.FileAccess.Write);
            System.Text.Json.JsonSerializer.Serialize(output, result);
            output.Flush(true); Shutdown(code); return;
        }

        if (e.Args.Length == 1 && e.Args[0] == "--manage")
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            if (!new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, "--manage")
                    { UseShellExecute = true, Verb = "runas", WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden }); }
                catch (System.ComponentModel.Win32Exception) { }
                Shutdown(); return;
            }
            var manager = new ManagementWindow(); MainWindow = manager; manager.Show(); return;
        }

        if (e.Args.Length >= 2 && string.Equals(e.Args[0], "--elevated-install", StringComparison.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var exitCode = await ElevatedInstallationWorker.RunAsync(e.Args[1]);
            Shutdown(exitCode);
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}
