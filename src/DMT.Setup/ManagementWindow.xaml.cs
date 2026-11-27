using System.ComponentModel;
using System.IO;
using System.ServiceProcess;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using DMT.Inventory;
using DMT.Setup.Services;
using Microsoft.Win32;

namespace DMT.Setup;

public partial class ManagementWindow : Window
{
    private readonly LocalizationService _strings = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _busy;
    private int? _httpsPort;
    private readonly string _preferences = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DMT", "management.json");

    public ManagementWindow()
    {
        InitializeComponent(); _strings.Initialize(); DataContext = _strings;
        var installedDb = LocalServiceDeployment.DatabasePath;
        var legacyDb = Path.Combine(LocalServiceDeployment.DataDirectory, "data", "dmt.sqlite");
        if (!File.Exists(installedDb) && File.Exists(legacyDb)) installedDb = legacyDb;
        DatabaseBox.Text = File.Exists(installedDb) ? installedDb : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DMT", "Development", "dmt.sqlite");
        var configurationPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DMT", "config", "installation.json");
        try
        {
            if (File.Exists(configurationPath))
            {
                using var configuration = JsonDocument.Parse(File.ReadAllText(configurationPath));
                if (configuration.RootElement.TryGetProperty("SourceDirectory", out var source)) SourceBox.Text = source.GetString();
                if (configuration.RootElement.TryGetProperty("HttpsPort", out var port)) _httpsPort = port.GetInt32();
                if (configuration.RootElement.TryGetProperty("Database", out var configuredDatabase)) DatabaseBox.Text = configuredDatabase.GetString();
            }
            if (File.Exists(_preferences))
            {
                var preferences = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(_preferences));
                if (preferences != null) { DatabaseBox.Text = preferences.Database; SourceBox.Text = preferences.Source; }
            }
        }
        catch (Exception e) { ResultBox.Text = e.Message; }
        RefreshStatus(); _timer.Tick += (_, _) => { if (!_busy) RefreshStatus(); }; _timer.Start();
        Closed += (_, _) => _timer.Stop();
        Closing += (_, e) => { if (_busy) { e.Cancel = true; OperationText.Text = _strings["manage.wait"]; } };
    }

    private void RefreshStatus()
    {
        try
        {
            using var service = new ServiceController("DMT");
            var status = service.Status;
            ServiceStatusText.Text = _strings["manage.service"] + ": " + _strings["manage.status." + status];
            StartServiceButton.IsEnabled = !_busy && status == ServiceControllerStatus.Stopped;
            StopServiceButton.IsEnabled = RestartServiceButton.IsEnabled = !_busy && status == ServiceControllerStatus.Running;
        }
        catch (InvalidOperationException e) when (e.InnerException is Win32Exception { NativeErrorCode: 1060 })
        {
            ServiceStatusText.Text = _strings["manage.notInstalled"];
            StartServiceButton.IsEnabled = RestartServiceButton.IsEnabled = StopServiceButton.IsEnabled = false;
        }
        catch (Exception e)
        {
            ServiceStatusText.Text = _strings["manage.error"] + ": " + e.Message;
            StartServiceButton.IsEnabled = RestartServiceButton.IsEnabled = StopServiceButton.IsEnabled = false;
        }
    }

    private async Task ControlAsync(string action)
    {
        if (_busy) return;
        SetBusy(true); OperationText.Text = _strings["manage.wait"];
        try
        {
            await Task.Run(() =>
            {
                // Only the DMT service owned by our installation can be controlled.
                var manifest = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DMT", "installation-manifest.json");
                var recorded = JsonSerializer.Deserialize<Models.InstallationManifest>(File.ReadAllText(manifest), new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });
                if (recorded?.Resources.Any(r => r.Type == "service" && r.Identifier == "DMT" && r.ManagedByDmt) != true)
                    throw new InvalidOperationException("unmanaged_service");
                using var service = new ServiceController("DMT");
                if (action is "stop" or "restart")
                {
                    if (service.Status != ServiceControllerStatus.Stopped)
                    { service.Stop(); service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(45)); }
                }
                if (action is "start" or "restart")
                {
                    service.Refresh();
                    if (service.Status != ServiceControllerStatus.Running)
                    { service.Start(); service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(45)); }
                }
            });
            OperationText.Text = _strings["manage.done"];
        }
        catch (Exception e) { OperationText.Text = _strings["manage.error"]; ResultBox.Text = e.Message; }
        finally { SetBusy(false); RefreshStatus(); }
    }

    private void SetBusy(bool value)
    {
        _busy = value; AcquireButton.IsEnabled = SourceBox.IsEnabled = DatabaseBox.IsEnabled = !value;
        if (value) StartServiceButton.IsEnabled = RestartServiceButton.IsEnabled = StopServiceButton.IsEnabled = false;
    }
    private async void Start_Click(object sender, RoutedEventArgs e) => await ControlAsync("start");
    private async void Restart_Click(object sender, RoutedEventArgs e) => await ControlAsync("restart");
    private async void Stop_Click(object sender, RoutedEventArgs e) => await ControlAsync("stop");
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshStatus();
    private void Dashboard_Click(object sender, RoutedEventArgs e)
    {
        if (_httpsPort is not (>= 1 and <= 65535)) { OperationText.Text = _strings["manage.notInstalled"]; return; }
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://localhost:" + _httpsPort) { UseShellExecute = true });
    }
    private async void ResetAdmin_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DMT", "config", "admin.json");
            var current = JsonSerializer.Deserialize<LocalAdminCredential>(File.ReadAllText(path)) ?? throw new InvalidDataException("admin_configuration_missing");
            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = _strings["manage.resetBody"], TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = current.Username, Margin = new Thickness(0, 12, 0, 6) });
            var password = new System.Windows.Controls.PasswordBox { MaxLength = 1024, Padding = new Thickness(6), Margin = new Thickness(0, 6, 0, 6) };
            var confirm = new System.Windows.Controls.PasswordBox { MaxLength = 1024, Padding = new Thickness(6), Margin = new Thickness(0, 6, 0, 6) };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = _strings["setup.admin.password"] }); panel.Children.Add(password);
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = _strings["setup.admin.confirmPassword"] }); panel.Children.Add(confirm);
            var error = new System.Windows.Controls.TextBlock { TextWrapping = TextWrapping.Wrap };
            var save = new System.Windows.Controls.Button { Content = _strings["manage.save"], Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 10, 0, 0) };
            panel.Children.Add(error); panel.Children.Add(save);
            var dialog = new Window { Owner = this, Title = _strings["manage.reset"], Content = panel, Width = 460, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            save.Click += (_, _) =>
            {
                if (password.Password.Length < LocalAdminCredential.MinimumPasswordLength || password.Password != confirm.Password) { error.Text = _strings["manage.passwordMismatch"]; return; }
                try
                {
                    var updated = LocalAdminCredential.Create(current.Username, password.Password);
                    var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    File.WriteAllText(temporary, JsonSerializer.Serialize(updated)); File.Move(temporary, path, true);
                    password.Clear(); confirm.Clear(); dialog.DialogResult = true;
                }
                catch (Exception failure) { error.Text = failure.Message; }
            };
            if (dialog.ShowDialog() == true)
            {
                using var service = new ServiceController("DMT");
                if (service.Status == ServiceControllerStatus.Running) await ControlAsync("restart");
                else OperationText.Text = _strings["manage.passwordReset"];
            }
        }
        catch (Exception error) { OperationText.Text = _strings["manage.error"]; ResultBox.Text = error.Message; }
    }
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var dialog = new OpenFileDialog { Filter = "SQLite|*.sqlite;*.sqlite3;*.db", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) DatabaseBox.Text = dialog.FileName;
    }
    private async void Acquire_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var source = SourceBox.Text.Trim(); var database = DatabaseBox.Text.Trim();
        if (!File.Exists(database) || !Directory.Exists(source)) { OperationText.Text = _strings["manage.pathsInvalid"]; return; }
        SetBusy(true); ResultBox.Clear(); OperationText.Text = _strings["manage.phase.import"];
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_preferences)!);
            File.WriteAllText(_preferences, JsonSerializer.Serialize(new Preferences(database, source)));
            var journals = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DMT", "Acquisitions");
            var progress = new Progress<(string Phase, int Count)>(p => OperationText.Text = _strings["manage.phase." + p.Phase] + (p.Count > 0 ? " · " + p.Count : ""));
            var result = await Task.Run(() => AcquisitionWorkflow.Run(database, source, "local", "default", journals,
                (phase, count) => ((IProgress<(string, int)>)progress).Report((phase, count)),
                reconcileConflicts: true, pruneSupersededConflicts: true));
            var partial = result.Import.Rejected > 0 || result.Issues.Count > 0 || result.Archive?.Blocked > 0 || result.Archive == null ||
                result.Compression?.Months.Any(m => m.Status != "archived") == true || result.Compression?.UnrecognizedFiles.Count > 0;
            OperationText.Text = _strings[partial ? "manage.partial" : "manage.done"];
            ResultBox.Text = string.Format(_strings["manage.result"], result.Import.Imported, result.Import.Duplicates,
                result.Import.Rejected, result.Archive?.Moved ?? 0, result.Archive?.Blocked ?? 0, result.EvidenceDirectory);
        }
        catch (Exception ex) { OperationText.Text = _strings["manage.error"]; ResultBox.Text = ex.Message; }
        finally { SetBusy(false); RefreshStatus(); }
    }
    private sealed record Preferences(string Database, string Source);
}
