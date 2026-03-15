using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using ClipGuard.Core;
using ClipGuard.Crypto;

namespace ClipGuard;

public sealed class AppEntry : Application
{
    public AppServices Services { get; } = new(new AesGcmEncryptionService(), new PasswordService());

    [STAThread]
    public static void Main()
    {
        var app = new AppEntry();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/styles/Theme.xaml", UriKind.Absolute) });
        app.Run(new MainWindow());
    }
}

public sealed class MainWindow : Window
{
    private readonly HotkeyService _hotkeys = new();
    private readonly MainViewModel _viewModel;
    private int _copyHotkeyId;
    private int _pasteHotkeyId;
    private readonly PasswordBox _logPasswordBox = new();
    private readonly TextBox _logBox = new();
    private readonly TextBlock _logStatusText = new();
    private readonly ComboBox _copyModifier = new();
    private readonly TextBox _copyKey = new();
    private readonly ComboBox _pasteModifier = new();
    private readonly TextBox _pasteKey = new();
    private readonly PasswordBox _currentPassword = new();
    private readonly PasswordBox _newPassword = new();
    private readonly PasswordBox _confirmPassword = new();
    private readonly TextBlock _settingsStatusText = new();
    private readonly StackPanel _currentPasswordRow = new();

    public MainWindow()
    {
        Title = "Clipboard Guardian";
        Width = 700;
        Height = 480;
        MinWidth = 660;
        MinHeight = 440;
        WindowStyle = WindowStyle.SingleBorderWindow;
        Background = (System.Windows.Media.Brush)Application.Current.FindResource("AppBackgroundBrush");

        var services = ((AppEntry)Application.Current).Services;
        _viewModel = new MainViewModel(services, new ForegroundAppService(), new DialogService());
        DataContext = _viewModel;
        _viewModel.HotkeysChanged += OnHotkeysChanged;
        _viewModel.SettingsRequested += OnSettingsRequested;

        Content = BuildLayout();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private UIElement BuildLayout()
    {
        var root = new Grid { Margin = new Thickness(16), Background = (System.Windows.Media.Brush)FindResource("AppBackgroundBrush") };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock { Text = "Clipboard Guardian", FontSize = 22, FontWeight = FontWeights.SemiBold });
        var navRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var homeButton = new Button { Content = "Главная", Style = (Style)FindResource("NavButtonStyle") };
        homeButton.SetBinding(Button.CommandProperty, new Binding("ShowHomeCommand"));
        var settingsButton = new Button { Content = "Настройки", Style = (Style)FindResource("NavButtonStyle") };
        settingsButton.SetBinding(Button.CommandProperty, new Binding("ShowSettingsCommand"));
        navRow.Children.Add(homeButton);
        navRow.Children.Add(settingsButton);

        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.Children.Add(header);
        Grid.SetColumn(navRow, 1);
        headerGrid.Children.Add(navRow);
        root.Children.Add(headerGrid);

        var subtitle = new TextBlock
        {
            Text = "Защита показывает подтверждение при копировании и вставке текста.",
            Foreground = (System.Windows.Media.Brush)FindResource("SecondaryTextBrush"),
            Margin = new Thickness(0, 6, 0, 12)
        };
        Grid.SetRow(subtitle, 1);
        root.Children.Add(subtitle);

        var contentGrid = new Grid();
        Grid.SetRow(contentGrid, 2);
        root.Children.Add(contentGrid);

        var homePanel = BuildHomePanel();
        homePanel.SetBinding(UIElement.VisibilityProperty, new Binding("IsSettingsView") { Converter = new InverseBoolToVisibilityConverter() });
        contentGrid.Children.Add(homePanel);

        var settingsPanel = BuildSettingsPanel();
        settingsPanel.SetBinding(UIElement.VisibilityProperty, new Binding("IsSettingsView") { Converter = new BoolToVisibilityConverter() });
        contentGrid.Children.Add(settingsPanel);

        return root;
    }

    private Border BuildHomePanel()
    {
        var panel = new Border { Style = (Style)FindResource("CardBorderStyle"), Background = (System.Windows.Media.Brush)FindResource("SurfaceBrush") };
        var stack = new StackPanel();

        var statusCard = new Border { Style = (Style)FindResource("CardBorderStyle"), Margin = new Thickness(0, 0, 0, 12) };
        var statusStack = new StackPanel();
        var statusTitle = new TextBlock { Text = "Статус: защита активна", FontSize = 14, FontWeight = FontWeights.SemiBold };
        statusTitle.SetBinding(TextBlock.TextProperty, new Binding("StatusMessage") { Converter = new StatusPrefixConverter() });
        var statusHint = new TextBlock
        {
            Text = "Приложение контролирует текст при копировании и вставке.",
            Foreground = (System.Windows.Media.Brush)FindResource("SecondaryTextBrush"),
            Margin = new Thickness(0, 4, 0, 0)
        };
        statusStack.Children.Add(statusTitle);
        statusStack.Children.Add(statusHint);
        statusCard.Child = statusStack;

        stack.Children.Add(statusCard);

        var logTitle = new TextBlock { Text = "История обращений", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 6) };
        stack.Children.Add(logTitle);

        stack.Children.Add(new TextBlock
        {
            Text = "Введите пароль, чтобы открыть лог",
            Foreground = (System.Windows.Media.Brush)FindResource("SecondaryTextBrush"),
            Margin = new Thickness(0, 0, 0, 4)
        });

        var logRow = new StackPanel { Orientation = Orientation.Horizontal };
        _logPasswordBox.Width = 220;
        _logPasswordBox.Margin = new Thickness(0, 0, 10, 0);
        var loadLogButton = new Button { Content = "Открыть лог", Style = (Style)FindResource("PrimaryButtonStyle"), Width = 160 };
        loadLogButton.Click += async (_, _) => await _viewModel.LoadLogAsync(_logPasswordBox.Password);
        logRow.Children.Add(_logPasswordBox);
        logRow.Children.Add(loadLogButton);
        stack.Children.Add(logRow);

        _logStatusText.Foreground = (System.Windows.Media.Brush)FindResource("SecondaryTextBrush");
        _logStatusText.Margin = new Thickness(0, 6, 0, 6);
        _logStatusText.SetBinding(TextBlock.TextProperty, new Binding("LogStatus"));
        stack.Children.Add(_logStatusText);

        _logBox.IsReadOnly = true;
        _logBox.AcceptsReturn = true;
        _logBox.TextWrapping = TextWrapping.Wrap;
        _logBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _logBox.Height = 220;
        _logBox.SetBinding(TextBox.TextProperty, new Binding("LogContent"));
        stack.Children.Add(_logBox);

        panel.Child = stack;
        return panel;
    }

    private Border BuildSettingsPanel()
    {
        var panel = new Border { Style = (Style)FindResource("CardBorderStyle"), Background = (System.Windows.Media.Brush)FindResource("SurfaceBrush") };
        var dock = new DockPanel();
        panel.Child = dock;

        _copyModifier.ItemsSource = new[] { "Alt", "Ctrl", "Shift", "Win" };
        _pasteModifier.ItemsSource = new[] { "Alt", "Ctrl", "Shift", "Win" };

        var footerGrid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _settingsStatusText.Foreground = (System.Windows.Media.Brush)FindResource("SecondaryTextBrush");
        _settingsStatusText.VerticalAlignment = VerticalAlignment.Center;
        _settingsStatusText.SetBinding(TextBlock.TextProperty, new Binding("SettingsStatus"));
        footerGrid.Children.Add(_settingsStatusText);

        var saveButton = new Button { Content = "Сохранить", Style = (Style)FindResource("PrimaryButtonStyle"), Width = 160 };
        saveButton.Click += async (_, _) => await SaveSettingsAsync();
        Grid.SetColumn(saveButton, 1);
        footerGrid.Children.Add(saveButton);

        DockPanel.SetDock(footerGrid, Dock.Bottom);
        dock.Children.Add(footerGrid);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        dock.Children.Add(scroll);

        var content = new StackPanel();
        scroll.Content = content;

        var keysTitle = new TextBlock { Text = "Горячие клавиши", FontSize = 14, FontWeight = FontWeights.SemiBold };
        content.Children.Add(keysTitle);

        var copyRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        copyRow.Children.Add(new TextBlock { Text = "Копирование", Width = 120, VerticalAlignment = VerticalAlignment.Center });
        _copyModifier.Width = 120;
        _copyModifier.Margin = new Thickness(0, 0, 10, 0);
        _copyKey.Width = 90;
        copyRow.Children.Add(_copyModifier);
        copyRow.Children.Add(_copyKey);
        content.Children.Add(copyRow);

        var pasteRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        pasteRow.Children.Add(new TextBlock { Text = "Вставка", Width = 120, VerticalAlignment = VerticalAlignment.Center });
        _pasteModifier.Width = 120;
        _pasteModifier.Margin = new Thickness(0, 0, 10, 0);
        _pasteKey.Width = 90;
        pasteRow.Children.Add(_pasteModifier);
        pasteRow.Children.Add(_pasteKey);
        content.Children.Add(pasteRow);

        var passwordTitle = new TextBlock { Text = "Пароль", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 6) };
        content.Children.Add(passwordTitle);

        _currentPasswordRow.Children.Clear();
        _currentPasswordRow.Children.Add(new TextBlock { Text = "Текущий пароль", Foreground = (System.Windows.Media.Brush)FindResource("SecondaryTextBrush") });
        _currentPasswordRow.Children.Add(_currentPassword);
        content.Children.Add(_currentPasswordRow);

        content.Children.Add(new TextBlock { Text = "Новый пароль", Foreground = (System.Windows.Media.Brush)FindResource("SecondaryTextBrush") });
        content.Children.Add(_newPassword);
        content.Children.Add(new TextBlock { Text = "Повторите пароль", Foreground = (System.Windows.Media.Brush)FindResource("SecondaryTextBrush") });
        content.Children.Add(_confirmPassword);

        return panel;
    }

    private async Task SaveSettingsAsync()
    {
        var copy = new HotkeyDefinition { Modifiers = GetModifier(_copyModifier), Key = NormalizeKey(_copyKey.Text) };
        var paste = new HotkeyDefinition { Modifiers = GetModifier(_pasteModifier), Key = NormalizeKey(_pasteKey.Text) };
        await _viewModel.ApplySettingsAsync(copy, paste, _currentPassword.Password, _newPassword.Password, _confirmPassword.Password);
    }

    private void LoadSettingsView()
    {
        SetCombo(_copyModifier, _viewModel.Settings.CopyHotkey.Modifiers);
        _copyKey.Text = _viewModel.Settings.CopyHotkey.Key;
        SetCombo(_pasteModifier, _viewModel.Settings.PasteHotkey.Modifiers);
        _pasteKey.Text = _viewModel.Settings.PasteHotkey.Key;
        var hasPassword = _viewModel.Settings.Password is not null;
        _currentPasswordRow.Visibility = hasPassword ? Visibility.Visible : Visibility.Collapsed;
        _currentPassword.Password = string.Empty;
        _newPassword.Password = string.Empty;
        _confirmPassword.Password = string.Empty;
        _viewModel.SettingsStatus = hasPassword ? string.Empty : "пароль не задан";
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
        _hotkeys.Attach(this);
        RegisterHotkeys();
        LoadSettingsView();
        _viewModel.ShowHome();
    }

    private void RegisterHotkeys()
    {
        _copyHotkeyId = _hotkeys.Register(((AppEntry)Application.Current).Services.Settings.CopyHotkey, () =>
        {
            _ = Dispatcher.InvokeAsync(() => _viewModel.HandleCopyAsync(this));
        });

        _pasteHotkeyId = _hotkeys.Register(((AppEntry)Application.Current).Services.Settings.PasteHotkey, () =>
        {
            _ = Dispatcher.InvokeAsync(() => _viewModel.HandlePasteAsync(this));
        });
    }

    private void OnHotkeysChanged()
    {
        _hotkeys.Unregister(_copyHotkeyId);
        _hotkeys.Unregister(_pasteHotkeyId);
        RegisterHotkeys();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.HotkeysChanged -= OnHotkeysChanged;
        _viewModel.SettingsRequested -= OnSettingsRequested;
        _hotkeys.Unregister(_copyHotkeyId);
        _hotkeys.Unregister(_pasteHotkeyId);
        _hotkeys.Dispose();
    }

    private void OnSettingsRequested()
    {
        LoadSettingsView();
    }

    private static void SetCombo(ComboBox combo, HotkeyModifiers modifiers)
    {
        var name = modifiers switch
        {
            HotkeyModifiers.Alt => "Alt",
            HotkeyModifiers.Control => "Ctrl",
            HotkeyModifiers.Shift => "Shift",
            HotkeyModifiers.Win => "Win",
            _ => "Alt"
        };

        combo.SelectedItem = name;
    }

    private static HotkeyModifiers GetModifier(ComboBox combo)
    {
        var name = combo.SelectedItem?.ToString();
        return name switch
        {
            "Ctrl" => HotkeyModifiers.Control,
            "Shift" => HotkeyModifiers.Shift,
            "Win" => HotkeyModifiers.Win,
            _ => HotkeyModifiers.Alt
        };
    }

    private static string NormalizeKey(string? key)
    {
        var trimmed = (key ?? "N").Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? "N" : trimmed.ToUpperInvariant();
    }
}

public sealed class HotkeyService : IDisposable
{
    private readonly Dictionary<int, Action> _handlers = new();
    private HwndSource? _source;
    private int _currentId = 1000;

    public void Attach(Window window)
    {
        var helper = new WindowInteropHelper(window);
        _source = HwndSource.FromHwnd(helper.Handle);
        _source.AddHook(WndProc);
    }

    public int Register(HotkeyDefinition definition, Action callback)
    {
        if (_source is null)
        {
            throw new InvalidOperationException("Hotkey service not attached to a window.");
        }

        var id = _currentId++;
        var modifiers = (uint)definition.Modifiers;
        var key = (uint)KeyInterop.VirtualKeyFromKey(ParseKey(definition.Key));

        if (!RegisterHotKey(_source.Handle, id, modifiers, key))
        {
            throw new InvalidOperationException("Failed to register hotkey.");
        }

        _handlers[id] = callback;
        return id;
    }

    public void Unregister(int id)
    {
        if (_source is null)
        {
            return;
        }

        _handlers.Remove(id);
        UnregisterHotKey(_source.Handle, id);
    }

    public void Dispose()
    {
        if (_source is null)
        {
            return;
        }

        foreach (var id in _handlers.Keys)
        {
            UnregisterHotKey(_source.Handle, id);
        }

        _handlers.Clear();
        _source.RemoveHook(WndProc);
    }

    private static Key ParseKey(string key)
    {
        return Enum.TryParse<Key>(key, true, out var parsed) ? parsed : Key.C;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int wmHotkey = 0x0312;
        if (msg == wmHotkey)
        {
            var id = wParam.ToInt32();
            if (_handlers.TryGetValue(id, out var action))
            {
                action();
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

public sealed class DialogService
{
    public bool ConfirmAccess(Window owner, string appName, string operation, string preview)
    {
        var dialog = new AccessRequestWindow(appName, operation, preview)
        {
            Owner = owner
        };

        return dialog.ShowDialog() == true;
    }

    public string? PromptPassword(Window owner, string title)
    {
        var dialog = new PasswordWindow(title)
        {
            Owner = owner
        };

        return dialog.ShowDialog() == true ? dialog.Password : null;
    }

    public string? PromptContent(Window owner)
    {
        var dialog = new CopyWindow
        {
            Owner = owner
        };

        return dialog.ShowDialog() == true ? dialog.Content : null;
    }

    
}

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly AppServices _services;
    private readonly ForegroundAppService _foregroundApp;
    private readonly DialogService _dialogs;

    private string _statusMessage = "защита активна";
    private string _hotkeyCopy = "Alt + N";
    private string _hotkeyPaste = "Alt + M";
    private bool _hasData;
    private bool _isSettingsView;
    private string _logContent = string.Empty;
    private string _logStatus = "Введите пароль, чтобы открыть лог.";
    private string _settingsStatus = string.Empty;
    private string? _sessionPassword;

    public MainViewModel(AppServices services, ForegroundAppService foregroundApp, DialogService dialogs)
    {
        _services = services;
        _foregroundApp = foregroundApp;
        _dialogs = dialogs;

        ShowHomeCommand = new RelayCommand(ShowHome);
        ShowSettingsCommand = new RelayCommand(ShowSettings);
    }

    public AppSettings Settings => _services.Settings;

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public string HotkeyCopy
    {
        get => _hotkeyCopy;
        set => SetField(ref _hotkeyCopy, value);
    }

    public string HotkeyPaste
    {
        get => _hotkeyPaste;
        set => SetField(ref _hotkeyPaste, value);
    }

    public bool HasData
    {
        get => _hasData;
        set => SetField(ref _hasData, value);
    }

    public bool IsSettingsView
    {
        get => _isSettingsView;
        set => SetField(ref _isSettingsView, value);
    }

    public string LogContent
    {
        get => _logContent;
        set => SetField(ref _logContent, value);
    }

    public string LogStatus
    {
        get => _logStatus;
        set => SetField(ref _logStatus, value);
    }

    public string SettingsStatus
    {
        get => _settingsStatus;
        set => SetField(ref _settingsStatus, value);
    }

    public ICommand ShowHomeCommand { get; }
    public ICommand ShowSettingsCommand { get; }

    public event Action? HotkeysChanged;
    public event Action? SettingsRequested;
    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task InitializeAsync()
    {
        await _services.InitializeAsync();
        UpdateHotkeys();
        HasData = await _services.Vault.HasDataAsync();
    }

    public void ShowHome()
    {
        IsSettingsView = false;
    }

    public void ShowSettings()
    {
        IsSettingsView = true;
        SettingsRequested?.Invoke();
    }

    public async Task HandleCopyAsync(Window owner)
    {
        var app = _foregroundApp.CaptureActive();
        var allowed = _dialogs.ConfirmAccess(owner, app.Name, "копирование", string.Empty);
        if (!allowed)
        {
            StatusMessage = "доступ к буферу отклонен";
            return;
        }

        if (string.IsNullOrWhiteSpace(_sessionPassword))
        {
            StatusMessage = "установите пароль в настройках или введите при вставке";
            return;
        }

        var content = _dialogs.PromptContent(owner);
        if (string.IsNullOrWhiteSpace(content))
        {
            StatusMessage = "ничего не скопировано";
            return;
        }

        await _services.Vault.SaveAsync(content, _sessionPassword);
        HasData = true;
        StatusMessage = "данные сохранены";
    }

    public async Task HandlePasteAsync(Window owner)
    {
        var app = _foregroundApp.CaptureActive();
        var allowed = _dialogs.ConfirmAccess(owner, app.Name, "вставку", string.Empty);
        if (!allowed)
        {
            StatusMessage = "доступ к буферу отклонен";
            return;
        }

        var password = _dialogs.PromptPassword(owner, "Введите пароль для доступа");
        if (password is null)
        {
            StatusMessage = "операция отменена";
            return;
        }

        if (!VerifyPassword(password))
        {
            StatusMessage = "неверный пароль";
            return;
        }

        _sessionPassword = password;
        var payload = await _services.Vault.LoadAsync(password);
        if (payload is null)
        {
            StatusMessage = "буфер пуст";
            return;
        }

        var previewWindow = new PasteWindow(payload.Content)
        {
            Owner = owner
        };

        previewWindow.ShowDialog();
        StatusMessage = "данные выданы приложению";
    }

    public async Task LoadLogAsync(string password)
    {
        var trimmed = password.Trim();
        if (_services.Settings.Password is null)
        {
            LogStatus = "пароль не задан. Установите его в настройках.";
            LogContent = string.Empty;
            return;
        }

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            LogStatus = "введите пароль";
            LogContent = string.Empty;
            return;
        }

        if (!VerifyPassword(trimmed))
        {
            LogStatus = "неверный пароль";
            LogContent = string.Empty;
            return;
        }

        _sessionPassword = trimmed;
        if (!File.Exists(_services.Paths.LogPath))
        {
            LogStatus = "лог пуст";
            LogContent = string.Empty;
            return;
        }

        var raw = await File.ReadAllTextAsync(_services.Paths.LogPath);
        if (string.IsNullOrWhiteSpace(raw))
        {
            LogStatus = "лог пуст";
            LogContent = string.Empty;
            return;
        }

        var lines = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var formatted = new List<string>();
        foreach (var line in lines)
        {
            try
            {
                var entry = JsonSerializer.Deserialize<LogEntry>(line, options);
                if (entry is null)
                {
                    formatted.Add(line);
                    continue;
                }

                var time = entry.Timestamp.ToLocalTime().ToString("HH:mm:ss");
                var content = entry.Content ?? string.Empty;
                if (LogContentCodec.TryDecode(entry.Content, trimmed, _services.Encryption, out var decoded, out var wasEncrypted))
                {
                    content = decoded;
                }
                else if (wasEncrypted)
                {
                    content = "[ошибка расшифровки]";
                }

                var contentPreview = content.Length > 80 ? content[..80] + "..." : content;
                formatted.Add($"{time} {entry.Operation} — {contentPreview}");
            }
            catch
            {
                formatted.Add(line);
            }
        }

        LogContent = string.Join(Environment.NewLine, formatted);
        LogStatus = "лог загружен";
    }

    public async Task ApplySettingsAsync(HotkeyDefinition copyHotkey, HotkeyDefinition pasteHotkey, string currentPassword, string newPassword, string confirmPassword)
    {
        _services.Settings.CopyHotkey = copyHotkey;
        _services.Settings.PasteHotkey = pasteHotkey;
        var trimmedCurrent = currentPassword.Trim();
        var trimmedNew = newPassword.Trim();
        var trimmedConfirm = confirmPassword.Trim();
        var hadPassword = _services.Settings.Password is not null;
        var originalPassword = _services.Settings.Password;

        if (!TryUpdatePassword(trimmedCurrent, trimmedNew, trimmedConfirm, out var message))
        {
            SettingsStatus = message;
            return;
        }

        var passwordUpdated = !string.IsNullOrWhiteSpace(trimmedNew);
        if (passwordUpdated)
        {
            var oldPassword = hadPassword ? trimmedCurrent : string.Empty;
            var reencrypted = await _services.ReencryptLogAsync(oldPassword, trimmedNew);
            if (!reencrypted)
            {
                _services.Settings.Password = originalPassword;
                SettingsStatus = "не удалось перешифровать лог, пароль не изменен";
                await _services.SaveSettingsAsync();
                UpdateHotkeys();
                HotkeysChanged?.Invoke();
                return;
            }
        }

        await _services.SaveSettingsAsync();
        UpdateHotkeys();
        HotkeysChanged?.Invoke();
        if (passwordUpdated)
        {
            _sessionPassword = trimmedNew;
        }
        SettingsStatus = string.IsNullOrWhiteSpace(message) ? "настройки сохранены" : message;
    }

    public void UpdateHotkeys()
    {
        HotkeyCopy = FormatHotkey(_services.Settings.CopyHotkey);
        HotkeyPaste = FormatHotkey(_services.Settings.PasteHotkey);
    }

    private bool TryUpdatePassword(string currentPassword, string newPassword, string confirmPassword, out string message)
    {
        var hasExisting = _services.Settings.Password is not null;
        var trimmedNew = newPassword.Trim();
        var trimmedConfirm = confirmPassword.Trim();
        var trimmedCurrent = currentPassword.Trim();

        if (string.IsNullOrWhiteSpace(trimmedNew) && string.IsNullOrWhiteSpace(trimmedConfirm) && string.IsNullOrWhiteSpace(trimmedCurrent))
        {
            message = string.Empty;
            return true;
        }

        if (string.IsNullOrWhiteSpace(trimmedNew) || string.IsNullOrWhiteSpace(trimmedConfirm))
        {
            message = "введите новый пароль и подтверждение";
            return false;
        }

        if (!string.Equals(trimmedNew, trimmedConfirm, StringComparison.Ordinal))
        {
            message = "пароли не совпадают";
            return false;
        }

        if (hasExisting)
        {
            if (string.IsNullOrWhiteSpace(trimmedCurrent))
            {
                message = "введите текущий пароль";
                return false;
            }

            if (!VerifyPassword(trimmedCurrent))
            {
                message = "текущий пароль неверный";
                return false;
            }
        }

        _services.Settings.Password = _services.Passwords.CreateRecord(trimmedNew);
        message = "пароль обновлен";
        return true;
    }

    private string? EnsurePassword(Window owner)
    {
        if (_services.Settings.Password is null)
        {
            return null;
        }

        var existing = _dialogs.PromptPassword(owner, "Введите пароль для доступа");
        if (existing is null)
        {
            return null;
        }

        return VerifyPassword(existing) ? existing : null;
    }

    private bool VerifyPassword(string password)
    {
        var record = _services.Settings.Password;
        return record is not null && _services.Passwords.Verify(password, record);
    }

    private static string FormatHotkey(HotkeyDefinition definition)
    {
        var modifiers = definition.Modifiers == HotkeyModifiers.None
            ? string.Empty
            : definition.Modifiers.ToString().Replace(",", " +");
        return string.IsNullOrWhiteSpace(modifiers)
            ? definition.Key
            : $"{modifiers} + {definition.Key}";
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;

    public RelayCommand(Action execute)
    {
        _execute = execute;
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => _execute();

    public event EventHandler? CanExecuteChanged;
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is Visibility visibility && visibility == Visibility.Visible;
    }
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is Visibility visibility && visibility != Visibility.Visible;
    }
}

public sealed class StatusPrefixConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string;
        return string.IsNullOrWhiteSpace(text) ? "Статус: неизвестно" : $"Статус: {text}";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

public sealed class AccessRequestWindow : Window
{
    public AccessRequestWindow(string appName, string operation, string preview)
    {
        Title = "Clipboard Guardian";
        Height = 360;
        Width = 620;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.SingleBorderWindow;
        Background = (System.Windows.Media.Brush)Application.Current.FindResource("AppBackgroundBrush");

        var border = new Border { Margin = new Thickness(12), Style = (Style)Application.Current.FindResource("CardBorderStyle") };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "Clipboard Guardian", FontSize = 18, FontWeight = FontWeights.SemiBold });
        var operationText = operation.Contains("коп", StringComparison.OrdinalIgnoreCase)
            ? "Обнаружено копирование в буфер обмена. Разрешить сохранить данные?"
            : "Обнаружена вставка из буфера обмена. Разрешить доступ?";
        stack.Children.Add(new TextBlock { Text = operationText, Margin = new Thickness(0, 6, 0, 12), Foreground = (System.Windows.Media.Brush)FindResource("SecondaryTextBrush") });

        var infoBorder = new Border { Background = (System.Windows.Media.Brush)FindResource("ClipGuardSlateBrush"), CornerRadius = new CornerRadius(2), Padding = new Thickness(12), MinHeight = 160 };
        var infoStack = new StackPanel();
        infoStack.Children.Add(new TextBlock { Text = $"Тип операции: {operation}", Margin = new Thickness(0, 4, 0, 6), Foreground = (System.Windows.Media.Brush)FindResource("SecondaryTextBrush") });
        var previewBox = new TextBox
        {
            Text = preview,
            IsReadOnly = true,
            AcceptsReturn = true,
            Height = 150,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        previewBox.Visibility = string.IsNullOrWhiteSpace(preview) ? Visibility.Collapsed : Visibility.Visible;
        infoStack.Children.Add(previewBox);
        infoBorder.Child = infoStack;
        stack.Children.Add(infoBorder);

        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var deny = new Button { Content = "Запретить", Width = 160, Style = (Style)FindResource("SecondaryButtonStyle"), Margin = new Thickness(0, 0, 10, 0) };
        deny.Click += (_, _) => DialogResult = false;
        var allow = new Button { Content = "Разрешить", Width = 160, Style = (Style)FindResource("PrimaryButtonStyle") };
        allow.Click += (_, _) => DialogResult = true;
        buttonRow.Children.Add(deny);
        buttonRow.Children.Add(allow);
        stack.Children.Add(buttonRow);

        border.Child = stack;
        base.Content = border;
    }
}

public sealed class PasswordWindow : Window
{
    private readonly PasswordBox _passwordBox = new();

    public PasswordWindow(string title)
    {
        Title = "Clipboard Guardian";
        Height = 300;
        Width = 460;
        MinHeight = 280;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.SingleBorderWindow;
        Background = (System.Windows.Media.Brush)Application.Current.FindResource("AppBackgroundBrush");

        var border = new Border { Margin = new Thickness(16), Style = (Style)Application.Current.FindResource("CardBorderStyle") };
        var dock = new DockPanel();
        border.Child = dock;

        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = "Отмена", Width = 150, Style = (Style)FindResource("SecondaryButtonStyle"), Margin = new Thickness(0, 0, 10, 0) };
        cancel.Click += (_, _) => DialogResult = false;
        var confirm = new Button { Content = "Продолжить", Width = 170, Style = (Style)FindResource("PrimaryButtonStyle") };
        confirm.Click += (_, _) => DialogResult = true;
        buttonRow.Children.Add(cancel);
        buttonRow.Children.Add(confirm);
        DockPanel.SetDock(buttonRow, Dock.Bottom);
        dock.Children.Add(buttonRow);

        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.Bold });
        content.Children.Add(new TextBlock { Text = "Пароль используется для расшифровки буфера", Margin = new Thickness(0, 6, 0, 12), Foreground = (System.Windows.Media.Brush)FindResource("SecondaryTextBrush") });
        content.Children.Add(_passwordBox);
        dock.Children.Add(content);

        base.Content = border;
    }

    public string Password => _passwordBox.Password;
}

public sealed class CopyWindow : Window
{
    private readonly TextBox _contentBox = new();

    public CopyWindow()
    {
        Title = "Clipboard Guardian";
        Height = 360;
        Width = 520;
        MinHeight = 340;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.SingleBorderWindow;
        Background = (System.Windows.Media.Brush)Application.Current.FindResource("AppBackgroundBrush");

        _contentBox.AcceptsReturn = true;
        _contentBox.Height = 140;
        _contentBox.TextWrapping = TextWrapping.Wrap;
        _contentBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _contentBox.AllowDrop = true;
        _contentBox.Drop += OnDropText;
        _contentBox.DragOver += OnDragOverText;

        var border = new Border { Margin = new Thickness(16), Style = (Style)Application.Current.FindResource("CardBorderStyle") };
        var dock = new DockPanel();
        border.Child = dock;

        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = "Отмена", Width = 150, Style = (Style)FindResource("SecondaryButtonStyle"), Margin = new Thickness(0, 0, 10, 0) };
        cancel.Click += (_, _) => DialogResult = false;
        var confirm = new Button { Content = "Сохранить", Width = 170, Style = (Style)FindResource("PrimaryButtonStyle") };
        confirm.Click += (_, _) => DialogResult = true;
        buttonRow.Children.Add(cancel);
        buttonRow.Children.Add(confirm);
        DockPanel.SetDock(buttonRow, Dock.Bottom);
        dock.Children.Add(buttonRow);

        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = "Ручной ввод", FontSize = 18, FontWeight = FontWeights.Bold });
        content.Children.Add(new TextBlock { Text = "Введите данные, которые нужно сохранить", Margin = new Thickness(0, 6, 0, 12), Foreground = (System.Windows.Media.Brush)FindResource("SecondaryTextBrush") });
        content.Children.Add(_contentBox);
        dock.Children.Add(content);

        base.Content = border;
    }

    public string Content => _contentBox.Text;

    private void OnDragOverText(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.UnicodeText) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDropText(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.UnicodeText))
        {
            _contentBox.Text = e.Data.GetData(DataFormats.UnicodeText)?.ToString() ?? string.Empty;
            _contentBox.SelectAll();
        }
    }
}

public sealed class PasteWindow : Window
{
    private readonly TextBox _contentBox;
    private Point _dragStart;
    private bool _dragArmed;

    public PasteWindow(string content)
    {
        Title = "Clipboard Guardian";
        Height = 300;
        Width = 480;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.SingleBorderWindow;
        Background = (System.Windows.Media.Brush)Application.Current.FindResource("AppBackgroundBrush");

        _contentBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            Height = 120,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Text = content
        };
        _contentBox.PreviewMouseLeftButtonDown += OnDragStart;
        _contentBox.PreviewMouseLeftButtonUp += OnDragEnd;
        _contentBox.PreviewMouseMove += OnDragMove;

        var border = new Border { Margin = new Thickness(16), Style = (Style)Application.Current.FindResource("CardBorderStyle") };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "Данные для вставки", FontSize = 18, FontWeight = FontWeights.Bold });
        stack.Children.Add(new TextBlock { Text = "Содержимое доступно только после подтверждения", Margin = new Thickness(0, 6, 0, 12), Foreground = (System.Windows.Media.Brush)FindResource("SecondaryTextBrush") });
        stack.Children.Add(_contentBox);

        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var close = new Button { Content = "Готово", Width = 160, Style = (Style)FindResource("PrimaryButtonStyle") };
        close.Click += (_, _) => DialogResult = true;
        buttonRow.Children.Add(close);
        stack.Children.Add(buttonRow);

        border.Child = stack;
        base.Content = border;
    }

    private void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragArmed = true;
    }

    private void OnDragEnd(object sender, MouseButtonEventArgs e)
    {
        _dragArmed = false;
    }

    private void OnDragMove(object sender, MouseEventArgs e)
    {
        if (!_dragArmed || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(null);
        if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragArmed = false;
        var text = string.IsNullOrWhiteSpace(_contentBox.SelectedText) ? _contentBox.Text : _contentBox.SelectedText;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var data = new DataObject(DataFormats.UnicodeText, text);
        DragDrop.DoDragDrop(_contentBox, data, DragDropEffects.Copy);
    }
}
