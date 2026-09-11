using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DMT.Setup.Models;
using DMT.Setup.Services;
using Microsoft.Win32;

namespace DMT.Setup;

public partial class MainWindow : Window
{
    private readonly LocalizationService _localization = new();
    private readonly PortInspectionService _portInspector = new();
    private readonly ListenerDetailService _listenerDetailService = new();
    private readonly NetworkInterfaceService _networkInterfaceService = new();
    private readonly ElevatedInstallationService _elevatedInstallationService = new();
    private IReadOnlyList<ListeningEndpoint> _listeners = [];
    private IReadOnlyList<NetworkInterfaceInfo> _networkInterfaces = [];
    private readonly ObservableCollection<EndpointRow> _visibleListeners = new();
    private bool _networkVisible;
    private bool _accessVisible;
    private bool _httpsVisible;
    private bool _adminVisible;
    private bool _summaryVisible;
    private bool _installVisible;
    private bool _installationRunning;
    private ListenerDetails? _selectedDetails;
    private bool _portSelectionInitialized;

    public MainWindow()
    {
        InitializeComponent();
        _localization.Initialize();
        _localization.PropertyChanged += Localization_PropertyChanged;
        DataContext = _localization;
        PortsGrid.ItemsSource = _visibleListeners;
        UpdateLocalizedNetworkUi();
        StateChanged += MainWindow_StateChanged;
        UpdateWindowStateButton();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximizeRestore();
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeRestore_Click(object sender, RoutedEventArgs e) => ToggleMaximizeRestore();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximizeRestore()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e) => UpdateWindowStateButton();

    private void UpdateWindowStateButton()
    {
        if (MaximizeRestoreButton is null) return;

        var maximized = WindowState == WindowState.Maximized;
        MaximizeRestoreButton.ToolTip = maximized
            ? _localization["setup.window.restore"]
            : _localization["setup.window.maximize"];
    }

    private void Badge_Click(object sender, RoutedEventArgs e) => _localization.UnlockLanguage("tlh", select: true);

    private async void Begin_Click(object sender, RoutedEventArgs e)
    {
        if (_installationRunning) return;

        if (_installVisible)
        {
            ShowSummaryPanel();
            return;
        }

        if (_summaryVisible)
        {
            await StartInstallationFoundationAsync();
            return;
        }

        if (_adminVisible)
        {
            if (!ValidateAdminSelection()) return;
            ShowSummaryPanel();
            return;
        }

        if (_httpsVisible)
        {
            ShowAdminPanel();
            return;
        }

        if (_accessVisible)
        {
            if (!ValidateAccessSelection()) return;
            ShowHttpsPanel();
            return;
        }

        if (!_networkVisible)
        {
            ShowNetworkPanel();
            return;
        }

        if (!ValidateSelectedPort()) return;
        ShowAccessPanel();
    }

    private void WelcomeNav_Click(object sender, RoutedEventArgs e) => ShowWelcomePanel();
    private void NetworkNav_Click(object sender, RoutedEventArgs e) => ShowNetworkPanel();
    private void AccessNav_Click(object sender, RoutedEventArgs e) { if (AccessNav.IsEnabled) ShowAccessPanel(); }
    private void HttpsNav_Click(object sender, RoutedEventArgs e) { if (HttpsNav.IsEnabled) ShowHttpsPanel(); }
    private void AdminNav_Click(object sender, RoutedEventArgs e) { if (AdminNav.IsEnabled) ShowAdminPanel(); }
    private void SummaryNav_Click(object sender, RoutedEventArgs e) { if (SummaryNav.IsEnabled) ShowSummaryPanel(); }

    private void ShowNetworkPanel()
    {
        _networkVisible = true;
        _accessVisible = false;
        _httpsVisible = false;
        _adminVisible = false;
        _summaryVisible = false;
        _installVisible = false;
        InstallPanel.Visibility = Visibility.Collapsed;
        AdminPanel.Visibility = Visibility.Collapsed;
        SummaryPanel.Visibility = Visibility.Collapsed;
        WelcomePanel.Visibility = Visibility.Collapsed;
        AccessPanel.Visibility = Visibility.Collapsed;
        HttpsPanel.Visibility = Visibility.Collapsed;
        NetworkPanel.Visibility = Visibility.Visible;
        WelcomeNav.Background = System.Windows.Media.Brushes.Transparent;
        AccessNav.Background = System.Windows.Media.Brushes.Transparent;
        HttpsNav.Background = System.Windows.Media.Brushes.Transparent;
        AdminNav.Background = System.Windows.Media.Brushes.Transparent;
        SummaryNav.Background = System.Windows.Media.Brushes.Transparent;
        InstallNav.Background = System.Windows.Media.Brushes.Transparent;
        NetworkNav.Background = (System.Windows.Media.Brush)FindResource("PeachBrush");
        BackButton.Visibility = Visibility.Visible;
        BeginButtonText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("[setup.network.continue]"));
        RefreshPorts();
    }

    private void ShowWelcomePanel()
    {
        _networkVisible = false;
        _accessVisible = false;
        _httpsVisible = false;
        _adminVisible = false;
        _summaryVisible = false;
        _installVisible = false;
        InstallPanel.Visibility = Visibility.Collapsed;
        AdminPanel.Visibility = Visibility.Collapsed;
        SummaryPanel.Visibility = Visibility.Collapsed;
        NetworkPanel.Visibility = Visibility.Collapsed;
        AccessPanel.Visibility = Visibility.Collapsed;
        HttpsPanel.Visibility = Visibility.Collapsed;
        WelcomePanel.Visibility = Visibility.Visible;
        NetworkNav.Background = System.Windows.Media.Brushes.Transparent;
        AccessNav.Background = System.Windows.Media.Brushes.Transparent;
        HttpsNav.Background = System.Windows.Media.Brushes.Transparent;
        AdminNav.Background = System.Windows.Media.Brushes.Transparent;
        SummaryNav.Background = System.Windows.Media.Brushes.Transparent;
        InstallNav.Background = System.Windows.Media.Brushes.Transparent;
        WelcomeNav.Background = (System.Windows.Media.Brush)FindResource("PeachBrush");
        BackButton.Visibility = Visibility.Collapsed;
        BeginButtonText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("[setup.welcome.begin]"));
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_installVisible && !_installationRunning)
        {
            ShowSummaryPanel();
            return;
        }

        if (_summaryVisible)
        {
            ShowAdminPanel();
            return;
        }

        if (_adminVisible)
        {
            ShowHttpsPanel();
            return;
        }

        if (_httpsVisible)
        {
            ShowAccessPanel();
            return;
        }

        if (_accessVisible)
        {
            ShowNetworkPanel();
            return;
        }

        ShowWelcomePanel();
    }

    private void ShowAccessPanel()
    {
        _networkVisible = false;
        _accessVisible = true;
        _httpsVisible = false;
        _adminVisible = false;
        _summaryVisible = false;
        _installVisible = false;
        InstallPanel.Visibility = Visibility.Collapsed;
        AdminPanel.Visibility = Visibility.Collapsed;
        SummaryPanel.Visibility = Visibility.Collapsed;
        WelcomePanel.Visibility = Visibility.Collapsed;
        NetworkPanel.Visibility = Visibility.Collapsed;
        AccessPanel.Visibility = Visibility.Visible;
        HttpsPanel.Visibility = Visibility.Collapsed;
        AccessNav.IsEnabled = true;
        WelcomeNav.Background = System.Windows.Media.Brushes.Transparent;
        NetworkNav.Background = System.Windows.Media.Brushes.Transparent;
        AccessNav.Background = (System.Windows.Media.Brush)FindResource("PeachBrush");
        HttpsNav.Background = System.Windows.Media.Brushes.Transparent;
        AdminNav.Background = System.Windows.Media.Brushes.Transparent;
        SummaryNav.Background = System.Windows.Media.Brushes.Transparent;
        InstallNav.Background = System.Windows.Media.Brushes.Transparent;
        BackButton.Visibility = Visibility.Visible;
        BeginButtonText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("[setup.network.continue]"));
        LoadNetworkInterfaces();
        ValidateAccessSelection();
        UpdateAccessCardState();
    }

    private void ShowHttpsPanel()
    {
        _networkVisible = false;
        _accessVisible = false;
        _httpsVisible = true;
        _adminVisible = false;
        _summaryVisible = false;
        _installVisible = false;
        InstallPanel.Visibility = Visibility.Collapsed;
        AdminPanel.Visibility = Visibility.Collapsed;
        SummaryPanel.Visibility = Visibility.Collapsed;
        WelcomePanel.Visibility = Visibility.Collapsed;
        NetworkPanel.Visibility = Visibility.Collapsed;
        AccessPanel.Visibility = Visibility.Collapsed;
        HttpsPanel.Visibility = Visibility.Visible;
        HttpsNav.IsEnabled = true;
        WelcomeNav.Background = System.Windows.Media.Brushes.Transparent;
        NetworkNav.Background = System.Windows.Media.Brushes.Transparent;
        AccessNav.Background = System.Windows.Media.Brushes.Transparent;
        AdminNav.Background = System.Windows.Media.Brushes.Transparent;
        SummaryNav.Background = System.Windows.Media.Brushes.Transparent;
        InstallNav.Background = System.Windows.Media.Brushes.Transparent;
        HttpsNav.Background = (System.Windows.Media.Brush)FindResource("PeachBrush");
        BackButton.Visibility = Visibility.Visible;
        BeginButtonText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("[setup.network.continue]"));
        BeginButton.IsEnabled = true;
        UpdateHttpsCardState();
    }

    private void ShowAdminPanel()
    {
        _networkVisible = false;
        _accessVisible = false;
        _httpsVisible = false;
        _adminVisible = true;
        _summaryVisible = false;
        _installVisible = false;
        InstallPanel.Visibility = Visibility.Collapsed;
        WelcomePanel.Visibility = Visibility.Collapsed;
        NetworkPanel.Visibility = Visibility.Collapsed;
        AccessPanel.Visibility = Visibility.Collapsed;
        HttpsPanel.Visibility = Visibility.Collapsed;
        SummaryPanel.Visibility = Visibility.Collapsed;
        AdminPanel.Visibility = Visibility.Visible;
        AdminNav.IsEnabled = true;
        WelcomeNav.Background = NetworkNav.Background = AccessNav.Background = HttpsNav.Background = SummaryNav.Background = InstallNav.Background = System.Windows.Media.Brushes.Transparent;
        AdminNav.Background = (System.Windows.Media.Brush)FindResource("PeachBrush");
        BackButton.Visibility = Visibility.Visible;
        BeginButtonText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("[setup.network.continue]"));
        ValidateAdminSelection();
    }

    private void ShowSummaryPanel()
    {
        if (!ValidateAdminSelection()) return;
        _networkVisible = false;
        _accessVisible = false;
        _httpsVisible = false;
        _adminVisible = false;
        _summaryVisible = true;
        _installVisible = false;
        InstallPanel.Visibility = Visibility.Collapsed;
        WelcomePanel.Visibility = NetworkPanel.Visibility = AccessPanel.Visibility = HttpsPanel.Visibility = AdminPanel.Visibility = Visibility.Collapsed;
        SummaryPanel.Visibility = Visibility.Visible;
        SummaryNav.IsEnabled = true;
        WelcomeNav.Background = NetworkNav.Background = AccessNav.Background = HttpsNav.Background = AdminNav.Background = InstallNav.Background = System.Windows.Media.Brushes.Transparent;
        SummaryNav.Background = (System.Windows.Media.Brush)FindResource("PeachBrush");
        BackButton.Visibility = Visibility.Visible;
        BeginButtonText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("[setup.summary.install]"));
        BeginButton.IsEnabled = true;
        RenderSummary();
    }

    private async Task StartInstallationFoundationAsync()
    {
        if (!int.TryParse(SelectedPortBox.Text.Trim(), out var httpsPort) || !ValidateAdminSelection())
            return;

        ShowInstallationPanel();
        _installationRunning = true;
        InstallProgressBar.Value = 5;
        InstallStatusText.Text = _localization["setup.install.status.requestingUac"];
        BeginButton.IsEnabled = false;
        BackButton.Visibility = Visibility.Collapsed;

        var selectedInterface = AccessInterfaceBox.SelectedItem as NetworkInterfaceInfo;
        var plan = new InstallationPlan
        {
            HttpsPort = httpsPort,
            AccessMode = AccessLocalRadio.IsChecked == true ? "local" : AccessLanRadio.IsChecked == true ? "lan" : "custom",
            AccessAddress = AccessCustomRadio.IsChecked == true
                ? selectedInterface?.IPv4Addresses.FirstOrDefault() ?? selectedInterface?.IPv6Addresses.FirstOrDefault()
                : null,
            HttpsMode = HttpsInternalRadio.IsChecked == true ? "internal" : "existing",
            AdminUsername = AdminUsernameBox.Text.Trim(),
            AdminDisplayName = string.IsNullOrWhiteSpace(AdminDisplayNameBox.Text) ? null : AdminDisplayNameBox.Text.Trim(),
            AdminPassword = AdminPasswordBox.Password
        };

        var result = await _elevatedInstallationService.RunAsync(plan, progress =>
        {
            Dispatcher.Invoke(() =>
            {
                InstallProgressBar.Value = progress.Percent;
                InstallStatusText.Text = _localization[progress.StatusKey];
            });
        });

        _installationRunning = false;

        if (result.Success)
        {
            InstallProgressBar.Value = 100;
            InstallStatusText.Text = _localization["setup.install.status.foundationComplete"];
            BeginButtonText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("[setup.install.returnSummary]"));
            BeginButton.IsEnabled = true;
            BackButton.Visibility = Visibility.Collapsed;
            return;
        }

        InstallProgressBar.Value = 0;
        InstallStatusText.Text = _localization[result.ErrorKey ?? "setup.install.error.communication"];
        BeginButtonText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("[setup.install.returnSummary]"));
        BeginButton.IsEnabled = true;
        BackButton.Visibility = Visibility.Collapsed;
    }

    private void ShowInstallationPanel()
    {
        _networkVisible = false;
        _accessVisible = false;
        _httpsVisible = false;
        _adminVisible = false;
        _summaryVisible = false;
        _installVisible = true;

        WelcomePanel.Visibility = NetworkPanel.Visibility = AccessPanel.Visibility = HttpsPanel.Visibility = AdminPanel.Visibility = SummaryPanel.Visibility = Visibility.Collapsed;
        InstallPanel.Visibility = Visibility.Visible;
        InstallNav.IsEnabled = true;

        WelcomeNav.Background = NetworkNav.Background = AccessNav.Background = HttpsNav.Background = AdminNav.Background = SummaryNav.Background = System.Windows.Media.Brushes.Transparent;
        InstallNav.Background = (System.Windows.Media.Brush)FindResource("PeachBrush");

        InstallProgressBar.Value = 0;
        InstallStatusText.Text = _localization["setup.install.status.ready"];
        BackButton.Visibility = Visibility.Collapsed;
        BeginButtonText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("[setup.install.returnSummary]"));
    }

    private void AdminField_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) ValidateAdminSelection();
    }

    private bool ValidateAdminSelection()
    {
        if (BeginButton is null || AdminUsernameBox is null || AdminPasswordBox is null || AdminConfirmPasswordBox is null || AdminValidationText is null) return false;
        var username = AdminUsernameBox.Text.Trim();
        var password = AdminPasswordBox.Password;
        string key;
        bool valid;
        if (string.IsNullOrWhiteSpace(username)) { key = "setup.admin.validation.username"; valid = false; }
        else if (password.Length < 12) { key = "setup.admin.validation.length"; valid = false; }
        else if (!string.Equals(password, AdminConfirmPasswordBox.Password, StringComparison.Ordinal)) { key = "setup.admin.validation.match"; valid = false; }
        else { key = "setup.admin.validation.ok"; valid = true; }
        AdminValidationText.Text = _localization[key];
        AdminValidationText.Foreground = valid
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(48, 122, 76))
            : (System.Windows.Media.Brush)FindResource("InkSoftBrush");
        BeginButton.IsEnabled = valid;
        return valid;
    }

    private void RenderSummary()
    {
        if (SummaryPortText is null) return;
        SummaryPortText.Text = SelectedPortBox.Text.Trim();
        SummaryAccessText.Text = AccessLocalRadio.IsChecked == true ? _localization["setup.access.local.title"]
            : AccessLanRadio.IsChecked == true ? _localization["setup.access.lan.title"]
            : string.Format(_localization["setup.summary.customAccess"], (AccessInterfaceBox.SelectedItem as NetworkInterfaceInfo)?.DisplayLabel ?? "");
        SummaryHttpsText.Text = HttpsInternalRadio.IsChecked == true ? _localization["setup.https.internal.title"] : _localization["setup.https.existing.title"];
        var displayName = AdminDisplayNameBox.Text.Trim();
        SummaryAdminText.Text = string.IsNullOrWhiteSpace(displayName) || displayName.Equals(AdminUsernameBox.Text.Trim(), StringComparison.OrdinalIgnoreCase)
            ? AdminUsernameBox.Text.Trim()
            : string.Format(_localization["setup.summary.adminWithName"], AdminUsernameBox.Text.Trim(), displayName);
    }

    private void HttpsMode_Checked(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) UpdateHttpsCardState();
    }

    private void UpdateAccessCardState()
    {
        SetChoiceCard(AccessLocalCard, AccessLocalRadio?.IsChecked == true);
        SetChoiceCard(AccessLanCard, AccessLanRadio?.IsChecked == true);
        SetChoiceCard(AccessCustomCard, AccessCustomRadio?.IsChecked == true);
    }

    private void UpdateHttpsCardState()
    {
        SetChoiceCard(HttpsInternalCard, HttpsInternalRadio?.IsChecked == true);
        SetChoiceCard(HttpsExistingCard, HttpsExistingRadio?.IsChecked == true);
    }

    private void SetChoiceCard(Border? card, bool selected)
    {
        if (card is null) return;
        card.Background = selected
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 244, 236))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 252, 249));
        card.BorderBrush = selected
            ? (System.Windows.Media.Brush)FindResource("OrangeBrush")
            : (System.Windows.Media.Brush)FindResource("BorderBrush");
        card.BorderThickness = selected ? new Thickness(1.5) : new Thickness(1);
    }

    private void LoadNetworkInterfaces()
    {
        try
        {
            _networkInterfaces = _networkInterfaceService.Inspect();
        }
        catch
        {
            _networkInterfaces = [];
        }

        AccessInterfaceBox.ItemsSource = _networkInterfaces;
        if (AccessInterfaceBox.SelectedIndex < 0 && _networkInterfaces.Count > 0)
            AccessInterfaceBox.SelectedIndex = 0;

        UpdateAccessInterfaceState();
    }

    private void AccessMode_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdateAccessInterfaceState();
        ValidateAccessSelection();
        UpdateAccessCardState();
    }

    private void AccessInterface_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) ValidateAccessSelection();
    }

    private void UpdateAccessInterfaceState()
    {
        if (AccessInterfaceBox is null || AccessInterfaceEmptyText is null || AccessCustomRadio is null) return;

        var custom = AccessCustomRadio.IsChecked == true;
        AccessInterfaceBox.IsEnabled = custom && _networkInterfaces.Count > 0;
        AccessInterfaceEmptyText.Visibility = custom && _networkInterfaces.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private bool ValidateAccessSelection()
    {
        if (BeginButton is null) return false;

        var valid = AccessLocalRadio?.IsChecked == true
            || AccessLanRadio?.IsChecked == true
            || (AccessCustomRadio?.IsChecked == true && AccessInterfaceBox?.SelectedItem is NetworkInterfaceInfo);

        BeginButton.IsEnabled = valid;
        return valid;
    }

    private void RefreshPorts_Click(object sender, RoutedEventArgs e) => RefreshPorts();

    private void ExportPorts_Click(object sender, RoutedEventArgs e)
    {
        if (_listeners.Count == 0)
            RefreshPorts();

        var dialog = new SaveFileDialog
        {
            Title = _localization["setup.network.export"],
            Filter = _localization["setup.network.exportFilter"],
            DefaultExt = ".csv",
            AddExtension = true,
            FileName = $"DMT-Port-Report-{Environment.MachineName}-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var generated = DateTimeOffset.Now;
            var sb = new StringBuilder();

            // Report metadata is written once, separately from the detected listener table.
            sb.AppendLine(string.Join(",", new[] { "DMT Port Report", "" }.Select(CsvField)));
            sb.AppendLine(string.Join(",", new[] { "Computer", Environment.MachineName }.Select(CsvField)));
            sb.AppendLine(string.Join(",", new[] { "Generated", generated.ToString("yyyy-MM-dd HH:mm:ss zzz") }.Select(CsvField)));
            sb.AppendLine();
            sb.AppendLine("Protocol,Address,Port,Exposure,PID,Process,Service,Executable,Description,Product,Company,Publisher,Signed,DMTKnowledge,DMTInfo");

            foreach (var endpoint in _listeners.OrderBy(x => x.Port).ThenBy(x => x.Address, StringComparer.OrdinalIgnoreCase))
            {
                var details = _listenerDetailService.Inspect(endpoint);
                var values = new[]
                {
                    endpoint.Protocol,
                    endpoint.Address,
                    endpoint.Port.ToString(),
                    ExposureLabel(endpoint.Exposure),
                    endpoint.ProcessId.ToString(),
                    endpoint.ProcessName,
                    endpoint.Services,
                    details.ExecutablePath,
                    details.FileDescription,
                    details.ProductName,
                    details.CompanyName,
                    details.Publisher,
                    details.IsSigned?.ToString() ?? "",
                    _localization[details.KnowledgeTitleKey],
                    _localization[details.KnowledgeBodyKey]
                };
                sb.AppendLine(string.Join(",", values.Select(CsvField)));
            }

            File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                string.Format(_localization["setup.network.exportError"], ex.Message),
                _localization["setup.network.exportErrorTitle"],
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static string CsvField(string? value)
    {
        value ??= "";
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private void RefreshPorts()
    {
        try
        {
            _listeners = _portInspector.Inspect();

            if (!_portSelectionInitialized)
            {
                var suggestedPort = PortInspectionService.SuggestPort(_listeners);
                SelectedPortBox.Text = suggestedPort > 0 ? suggestedPort.ToString() : "";
                _portSelectionInitialized = true;
            }

            ApplyFilter();
            ValidateSelectedPort();
        }
        catch (Exception ex)
        {
            _listeners = [];
            ListenerStatusText.Text = ex.Message;
            ApplyFilter();
            ValidateSelectedPort();
        }
    }

    private void SelectedPort_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded) ValidateSelectedPort();
    }

    private bool ValidateSelectedPort()
    {
        if (SelectedPortBox is null || SelectedPortStatusText is null || BeginButton is null)
            return false;

        var raw = SelectedPortBox.Text.Trim();

        if (!int.TryParse(raw, out var port) || port is < 1 or > 65535)
        {
            SelectedPortStatusText.Text = _localization["setup.network.portInvalid"];
            SelectedPortStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(166, 61, 47));
            BeginButton.IsEnabled = false;
            return false;
        }

        if (_listeners.Any(x => x.Port == port))
        {
            SelectedPortStatusText.Text = _localization["setup.network.portInUse"];
            SelectedPortStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(166, 61, 47));
            BeginButton.IsEnabled = false;
            return false;
        }

        SelectedPortStatusText.Text = _localization["setup.network.portAvailable"];
        SelectedPortStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(72, 118, 90));
        BeginButton.IsEnabled = true;
        return true;
    }

    private void PortFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded) ApplyFilter();
    }

    private void ApplyFilter()
    {
        var selected = (PortsGrid?.SelectedItem as EndpointRow)?.Source;
        var q = PortFilterBox?.Text?.Trim() ?? "";
        var filtered = string.IsNullOrWhiteSpace(q)
            ? _listeners
            : _listeners.Where(x =>
                x.Port.ToString().Contains(q, StringComparison.OrdinalIgnoreCase) ||
                x.ProcessId.ToString().Contains(q, StringComparison.OrdinalIgnoreCase) ||
                x.Address.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                x.ProcessName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                x.Services.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

        _visibleListeners.Clear();
        foreach (var x in filtered)
            _visibleListeners.Add(new EndpointRow(x, ExposureLabel(x.Exposure)));

        if (selected is not null)
        {
            var restored = _visibleListeners.FirstOrDefault(x => SameEndpoint(x.Source, selected));
            if (restored is not null && PortsGrid is not null) PortsGrid.SelectedItem = restored;
            else ClearListenerDetails();
        }

        var local = _listeners.Count(x => x.IsLoopback);
        var all = _listeners.Count(x => x.IsAny);
        var specific = _listeners.Count - local - all;
        ListenerStatusText.Text = string.Format(_localization["setup.network.status"], _listeners.Count, local, all, specific);
    }

    private static bool SameEndpoint(ListeningEndpoint a, ListeningEndpoint b) =>
        a.Port == b.Port && a.ProcessId == b.ProcessId && a.Address.Equals(b.Address, StringComparison.OrdinalIgnoreCase);

    private string ExposureLabel(string exposure) => exposure switch
    {
        "local" => _localization["setup.network.local"],
        "all" => _localization["setup.network.all"],
        _ => _localization["setup.network.specific"]
    };

    private void PortsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PortsGrid.SelectedItem is not EndpointRow row)
        {
            ClearListenerDetails();
            return;
        }

        _selectedDetails = _listenerDetailService.Inspect(row.Source);
        RenderListenerDetails();
    }

    private void ClearListenerDetails()
    {
        _selectedDetails = null;
        if (ListenerDetailsCard is not null) ListenerDetailsCard.Visibility = Visibility.Collapsed;
    }

    private void RenderListenerDetails()
    {
        if (_selectedDetails is null || ListenerDetailsCard is null) return;

        var d = _selectedDetails;
        var ep = d.Endpoint;
        ListenerDetailsCard.Visibility = Visibility.Visible;
        DetailEndpointText.Text = $"{ep.Protocol} {ep.Address}:{ep.Port}";
        DetailExposureText.Text = ExposureLabel(ep.Exposure);
        DetailProcessText.Text = string.IsNullOrWhiteSpace(ep.ProcessName)
            ? $"PID {ep.ProcessId}"
            : $"{ep.ProcessName}  ·  PID {ep.ProcessId}";
        DetailServiceText.Text = ValueOrUnavailable(ep.Services);
        DetailPathText.Text = ValueOrUnavailable(d.ExecutablePath);

        var descriptionParts = new[] { d.FileDescription, d.ProductName }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        DetailDescriptionText.Text = ValueOrUnavailable(string.Join(" · ", descriptionParts));

        var companyParts = new[] { d.CompanyName, d.Publisher }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        DetailCompanyText.Text = ValueOrUnavailable(string.Join(" · ", companyParts));
        DetailSignatureText.Text = d.IsSigned switch
        {
            true => string.IsNullOrWhiteSpace(d.Publisher)
                ? _localization["setup.network.signature.signed"]
                : string.Format(_localization["setup.network.signature.signedBy"], d.Publisher),
            false => _localization["setup.network.signature.unsigned"],
            _ => _localization["setup.network.unavailable"]
        };

        KnowledgeTitleText.Text = _localization[d.KnowledgeTitleKey];
        KnowledgeBodyText.Text = _localization[d.KnowledgeBodyKey];
    }

    private string ValueOrUnavailable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? _localization["setup.network.unavailable"] : value;

    private void Localization_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "Item[]" or nameof(LocalizationService.SelectedLanguage))
        {
            UpdateLocalizedNetworkUi();
            UpdateWindowStateButton();
            if (_networkVisible)
            {
                ApplyFilter();
                ValidateSelectedPort();
            }
            if (_accessVisible)
            {
                UpdateAccessInterfaceState();
                ValidateAccessSelection();
                UpdateAccessCardState();
            }
            if (_httpsVisible) UpdateHttpsCardState();
            if (_adminVisible) ValidateAdminSelection();
            if (_summaryVisible) RenderSummary();
            if (_selectedDetails is not null) RenderListenerDetails();
        }
    }

    private void UpdateLocalizedNetworkUi()
    {
        if (PortColumn is null) return;
        PortColumn.Header = _localization["setup.network.port"];
        BindingColumn.Header = _localization["setup.network.binding"];
        PidColumn.Header = _localization["setup.network.pid"];
        ProcessColumn.Header = _localization["setup.network.process"];
        ServiceColumn.Header = _localization["setup.network.service"];
        ExposureColumn.Header = _localization["setup.network.exposure"];
    }


    private sealed class EndpointRow
    {
        public EndpointRow(ListeningEndpoint x, string exposureDisplay)
        {
            Source = x;
            Port = x.Port;
            Address = x.Address;
            ProcessId = x.ProcessId;
            ProcessName = x.ProcessName;
            Services = x.Services;
            ProcessPath = x.ProcessPath;
            ExposureDisplay = exposureDisplay;
        }

        public ListeningEndpoint Source { get; }
        public int Port { get; }
        public string Address { get; }
        public int ProcessId { get; }
        public string ProcessName { get; }
        public string Services { get; }
        public string ProcessPath { get; }
        public string ExposureDisplay { get; }
    }
}
