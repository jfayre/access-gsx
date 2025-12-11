using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.FlightSimulator.SimConnect;
using Microsoft.Win32;
using DavyKager;
using System.Windows.Controls;

namespace AccessGSX
{
    public partial class MainWindow : Window
    {
        private const int WM_USER_SIMCONNECT = 0x0402;

        private const string SubKey = @"Software\Fsdreamteam\";
        private const string ValueName = "root";
        private const string GsxPackageFolder = @"MSFS\fsdreamteam-gsx-pro";
        private const string GsxPackageFolderHtml = @"html_ui\InGamePanels\FSDT_GSX_Panel";
        private const string GsxTooltipFileName = "tooltip";
        private const string GsxMenuFileName = "menu";

        private SimConnect? _simConnect;
        private bool _menuOpen;
        private bool _couatlStarted;
        private bool _shuttingDown;
        private string? _toolTipPath;
        private string? _menuPath;
        private string? _fsdtRoot;
        private int _highlightedIndex = -1;
        private string _menuTitle = "GSX Menu";
        private HwndSource? _hwndSource;
        private readonly UserSettings _settings = UserSettings.Load();

        private readonly List<MenuOption> _menuOptions = new();

        private enum DataRequestId
        {
            RequestRemote,
            RequestCouatlStarted,
        }

        private enum DataDefineId
        {
            CouatlStarted,
            MenuOpen,
            MenuChoice,
            RemoteControl,
        }

        private enum GroupId
        {
            MainGroup,
        }

        private enum EventId
        {
            ExternalSystemSet,
            ExternalSystemToggle,
        }

        private struct DoubleValue
        {
            public double Value;
        }

        private sealed record MenuOption(string DisplayKey, string Text, int Choice);

        public MainWindow()
        {
            InitializeComponent();
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            AppendLog("GSX Remote Control ready. Press F5 to open the menu. Use number keys or A–E to choose.");
            AppendLog("Waiting for Microsoft Flight Simulator / SimConnect...");
            SpeakMenuCheckBox.IsChecked = _settings.SpeakMenu;
            SpeakTooltipCheckBox.IsChecked = _settings.SpeakTooltip;
            if (_settings.SpeakMenu)
                TryLoadTolkOrDisable(SpeakMenuCheckBox);
            if (_settings.SpeakTooltip)
                TryLoadTolkOrDisable(SpeakTooltipCheckBox);
            StatusBox.Focus();
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            _hwndSource = (HwndSource)PresentationSource.FromVisual(this)!;
            _hwndSource.AddHook(WndProc);
            TryConnect();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_USER_SIMCONNECT && _simConnect != null)
            {
                try
                {
                    _simConnect.ReceiveMessage();
                }
                catch (COMException ex)
                {
                    AppendLog($"SimConnect receive failed (is the simulator running?): {ex.Message}");
                }
                handled = true;
            }

            return IntPtr.Zero;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            _shuttingDown = true;
            SetRemoteControl(0);
            Disconnect();
            base.OnClosing(e);
        }

        private void TryConnect()
        {
            try
            {
                var handle = new WindowInteropHelper(this).Handle;
                _simConnect = new SimConnect("GSX_Remote", handle, WM_USER_SIMCONNECT, null, 0);
                HookSimConnectEvents();
                StatusBox.Text = "Status: Connected to Microsoft Flight Simulator";
                AppendLog("Connected to Flight Simulator via SimConnect.");
            }
            catch (COMException ex)
            {
                StatusBox.Text = "Status: Can't open SimConnect. Is MSFS running?";
                AppendLog($"SimConnect unavailable: {ex.Message}");
            }
            catch (Exception ex)
            {
                StatusBox.Text = "Status: SimConnect initialization failed.";
                AppendLog($"SimConnect failed to initialize: {ex.Message}");
            }
        }

        private void Disconnect()
        {
            try
            {
                _simConnect?.Dispose();
            }
            catch
            {
                // ignore; we're shutting down
            }
            finally
            {
                _simConnect = null;
            }
        }

        private void HookSimConnectEvents()
        {
            if (_simConnect == null)
                return;

            _simConnect.OnRecvOpen += OnSimConnectOpen;
            _simConnect.OnRecvQuit += OnSimConnectQuit;
            _simConnect.OnRecvException += OnSimConnectException;
            _simConnect.OnRecvEvent += OnSimConnectEvent;
            _simConnect.OnRecvSimobjectData += OnSimConnectSimObjectData;
        }

        private void OnSimConnectOpen(SimConnect sender, SIMCONNECT_RECV_OPEN data)
        {
            AppendLog("SimConnect channel opened.");
            DefineSimVars();
            MapEvents();
            RequestSimVars();
            CloseToolbarPanel();
            ResolveFsdtPaths();
            HideMenu();
            SetRemoteControl(1);
        }

        private void OnSimConnectQuit(SimConnect sender, SIMCONNECT_RECV data)
        {
            AppendLog("Simulator has closed the connection.");
            StatusBox.Text = "Status: Simulator disconnected";
            if (!_shuttingDown)
            {
                HideMenu();
            }
            Disconnect();
        }

        private void OnSimConnectException(SimConnect sender, SIMCONNECT_RECV_EXCEPTION data)
        {
            AppendLog($"SimConnect exception: {data.dwException}");
        }

        private void OnSimConnectEvent(SimConnect sender, SIMCONNECT_RECV_EVENT data)
        {
            switch ((EventId)data.uEventID)
            {
                case EventId.ExternalSystemToggle:
                    HandleToggleEvent(data.dwData);
                    break;
                case EventId.ExternalSystemSet:
                    ReloadAndShowToolTip(data.dwData);
                    break;
                default:
                    break;
            }
        }

        private void HandleToggleEvent(uint value)
        {
            switch (value)
            {
                case 1:
                    if (!_menuOpen)
                        ReloadMenu();
                    else
                        HideMenu();
                    break;
                case 2:
                    HideMenu();
                    break;
                case 3:
                    CloseWithChoice(-1);
                    break;
                case 4:
                    AppendLog("GSX toolbar panel has been closed.");
                    break;
                default:
                    break;
            }
        }

        private void OnSimConnectSimObjectData(SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA data)
        {
            switch ((DataRequestId)data.dwRequestID)
            {
                case DataRequestId.RequestCouatlStarted:
                {
                    var value = (DoubleValue)data.dwData[0];
                    _couatlStarted = value.Value != 0;
                    AppendLog($"REQUEST_COUATL_STARTED received, value = {value.Value}");
                    UpdateStatusLabel();
                    break;
                }
                case DataRequestId.RequestRemote:
                {
                    var value = (DoubleValue)data.dwData[0];
                    AppendLog($"REQUEST_REMOTE received, value = {value.Value}");
                    if (Math.Abs(value.Value) < double.Epsilon)
                        SetRemoteControl(1);
                    break;
                }
            }
        }

        private void DefineSimVars()
        {
            if (_simConnect == null)
                return;

            _simConnect.AddToDataDefinition(DataDefineId.CouatlStarted, "L:FSDT_GSX_COUATL_STARTED", "number", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DataDefineId.MenuOpen, "L:FSDT_GSX_MENU_OPEN", "number", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DataDefineId.MenuChoice, "L:FSDT_GSX_MENU_CHOICE", "number", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DataDefineId.RemoteControl, "L:FSDT_GSX_SET_REMOTECONTROL", "number", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, SimConnect.SIMCONNECT_UNUSED);

            _simConnect.RegisterDataDefineStruct<DoubleValue>(DataDefineId.CouatlStarted);
            _simConnect.RegisterDataDefineStruct<DoubleValue>(DataDefineId.MenuOpen);
            _simConnect.RegisterDataDefineStruct<DoubleValue>(DataDefineId.MenuChoice);
            _simConnect.RegisterDataDefineStruct<DoubleValue>(DataDefineId.RemoteControl);
        }

        private void MapEvents()
        {
            if (_simConnect == null)
                return;

            _simConnect.MapClientEventToSimEvent(EventId.ExternalSystemSet, "EXTERNAL_SYSTEM_SET");
            _simConnect.MapClientEventToSimEvent(EventId.ExternalSystemToggle, "EXTERNAL_SYSTEM_TOGGLE");
            _simConnect.AddClientEventToNotificationGroup(GroupId.MainGroup, EventId.ExternalSystemSet, false);
            _simConnect.AddClientEventToNotificationGroup(GroupId.MainGroup, EventId.ExternalSystemToggle, false);
            _simConnect.SetNotificationGroupPriority(GroupId.MainGroup, SimConnect.SIMCONNECT_GROUP_PRIORITY_HIGHEST);
        }

        private void RequestSimVars()
        {
            if (_simConnect == null)
                return;

            _simConnect.RequestDataOnSimObject(DataRequestId.RequestRemote, DataDefineId.RemoteControl, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD.VISUAL_FRAME, SIMCONNECT_DATA_REQUEST_FLAG.CHANGED, 0, 0, 0);
            _simConnect.RequestDataOnSimObject(DataRequestId.RequestCouatlStarted, DataDefineId.CouatlStarted, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_PERIOD.VISUAL_FRAME, SIMCONNECT_DATA_REQUEST_FLAG.CHANGED, 0, 0, 0);
        }

        private void CloseToolbarPanel()
        {
            if (_simConnect == null)
                return;

            _simConnect.MapClientEventToSimEvent(EventId.ExternalSystemToggle, "EXTERNAL_SYSTEM_TOGGLE");
            _simConnect.TransmitClientEvent(0, EventId.ExternalSystemToggle, 4, GroupId.MainGroup, SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
        }

        private void ReloadMenu()
        {
            if (string.IsNullOrWhiteSpace(_menuPath))
            {
                AppendLog("Menu file path is not set. Make sure GSX is installed.");
                return;
            }

            List<string> lines;
            try
            {
                lines = new List<string>(File.ReadAllLines(_menuPath, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to read GSX menu file: {ex.Message}");
                return;
            }

            if (lines.Count == 0)
            {
                AppendLog("Menu file is empty.");
                return;
            }

            _menuOptions.Clear();
            _menuTitle = lines[0];

            for (int i = 1; i < lines.Count; i++)
            {
                int displayNumber = i == 10 ? 0 : i;
                int choice = displayNumber == 0 ? 9 : displayNumber - 1;
                _menuOptions.Add(new MenuOption(displayNumber.ToString(), lines[i], choice));
            }

            _menuOptions.Add(new MenuOption("A", "Customize Airport positions...", 10));
            _menuOptions.Add(new MenuOption("B", "Customize Airplane...", 11));
            _menuOptions.Add(new MenuOption("C", "GSX Settings...", 12));
            _menuOptions.Add(new MenuOption("D", "Restart GSX", 13));
            _menuOptions.Add(new MenuOption("E", "Reload Simbrief", 14));

            _menuOpen = true;
            _highlightedIndex = _menuOptions.Count > 0 ? 0 : -1;
            RenderMenu(_menuTitle);
            MenuBox.Focus();
        }

        private void RenderMenu(string title)
        {
            _menuTitle = title;
            var builder = new StringBuilder();
            builder.AppendLine(title);
            // builder.AppendLine();

            for (int i = 0; i < _menuOptions.Count; i++)
            {
                var option = _menuOptions[i];
                string prefix = i == _highlightedIndex ? "> " : "  ";
                builder.Append(prefix)
                    .Append(option.DisplayKey.PadLeft(2))
                    .Append(" - ")
                    .AppendLine(option.Text);
            }

            builder.AppendLine();
            builder.AppendLine("Press number keys or A–E to choose an item.");

            MenuBox.Text = builder.ToString();
            SpeakMenuContent();
        }


        private void HideMenu(bool timedOut = false)
        {
            _menuOpen = false;
            MenuBox.Text = timedOut
                ? "[GSX Menu] Timeout. Press F5 to re-open."
                : "GSX Menu hidden. Press F5 to open it.";

            if (_couatlStarted)
                AppendLog("Press F5 to open the GSX menu.");
            else
                AppendLog("Couatl engine has not started yet.");
        }

        private void CloseWithChoice(int choice)
        {
            AppendLog($"GSX closeWithChoice({choice})");

            if (choice == 14) // Reopen menu after "Reload Simbrief"
            {
                SetMenuOpenVar(1);
                _ = Task.Run(async () =>
                {
                    await Task.Delay(500).ConfigureAwait(false);
                    Dispatcher.Invoke(() =>
                    {
                        SetMenuChoiceVar(choice);
                        _menuOpen = false;
                    });
                });
            }
            else
            {
                HideMenu(choice < 0);
                SetMenuChoiceVar(choice);
            }
        }

        private void ReloadAndShowToolTip(uint data)
        {
            if (string.IsNullOrWhiteSpace(_toolTipPath))
            {
                AppendLog("Tooltip file path is not set.");
                return;
            }

            try
            {
                var lines = File.ReadAllLines(_toolTipPath, Encoding.UTF8);
                AppendLog($"GSX tooltip received: timeout {data}s");
                ToolTipBox.Text = string.Join(Environment.NewLine, lines);
                SpeakTooltipIfEnabled(lines);
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to read tooltip file: {ex.Message}");
            }
        }

        private void SetRemoteControl(int value) => SendVariable(DataDefineId.RemoteControl, value);

        private void SetMenuOpenVar(int value) => SendVariable(DataDefineId.MenuOpen, value);

        private void SetMenuChoiceVar(int value) => SendVariable(DataDefineId.MenuChoice, value);

        private void SendVariable(DataDefineId definition, double value)
        {
            if (_simConnect == null)
                return;

            try
            {
                _simConnect.SetDataOnSimObject(definition, SimConnect.SIMCONNECT_OBJECT_ID_USER, SIMCONNECT_DATA_SET_FLAG.DEFAULT, new DoubleValue { Value = value });
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to set {definition}: {ex.Message}");
            }
        }

        private void ResolveFsdtPaths()
        {
            _fsdtRoot = ReadRegistryValue(SubKey, ValueName);
            if (string.IsNullOrWhiteSpace(_fsdtRoot))
            {
                AppendLog("Failed to read FSDT root from registry. GSX may not be installed.");
                return;
            }

            _toolTipPath = Path.Combine(_fsdtRoot, GsxPackageFolder, GsxPackageFolderHtml, GsxTooltipFileName);
            _menuPath = Path.Combine(_fsdtRoot, GsxPackageFolder, GsxPackageFolderHtml, GsxMenuFileName);
            AppendLog($"FSDT root located at: {_fsdtRoot}");
        }

        private static string? ReadRegistryValue(string subKey, string valueName)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(subKey);
                return key?.GetValue(valueName)?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private void UpdateStatusLabel()
        {
            var sb = new StringBuilder();
            sb.Append("Status: Connected");
            sb.Append(_couatlStarted ? " | Couatl started" : " | Couatl not started");
            StatusBox.Text = sb.ToString();
        }

        private void AppendLog(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            StatusBox.AppendText($"{DateTime.Now:HH:mm:ss} {text}{Environment.NewLine}");
            StatusBox.ScrollToEnd();
        }

        private void OnMenuPreviewKeyDown(object sender, KeyEventArgs e) => HandleKey(e);

        private void OnKeyDown(object sender, KeyEventArgs e) => HandleKey(e);

        private void HandleKey(KeyEventArgs e)
        {
            if (e.Key == Key.F5)
            {
                SetMenuOpenVar(1);
                return;
            }

            if (!_menuOpen)
                return;

            HandleChoiceKey(e);
        }

        private void HandleChoiceKey(KeyEventArgs e)
        {
            int choice = -999;

            if (e.Key >= Key.D0 && e.Key <= Key.D9)
            {
                int number = e.Key - Key.D0;
                choice = number == 0 ? 9 : number - 1;
            }
            else if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
            {
                int number = e.Key - Key.NumPad0;
                choice = number == 0 ? 9 : number - 1;
            }
            else if (e.Key >= Key.A && e.Key <= Key.E)
            {
                choice = (e.Key - Key.A) + 10;
            }

            if (choice != -999)
            {
                e.Handled = true;
                CloseWithChoice(choice);
            }
        }

        private void OnSpeakMenuToggled(object sender, RoutedEventArgs e)
        {
            if (SpeakMenuCheckBox.IsChecked == true)
            {
                TryLoadTolkOrDisable(SpeakMenuCheckBox);
            }
            _settings.SpeakMenu = SpeakMenuCheckBox.IsChecked == true;
            _settings.Save();
        }

        private void OnSpeakTooltipToggled(object sender, RoutedEventArgs e)
        {
            if (SpeakTooltipCheckBox.IsChecked == true)
            {
                TryLoadTolkOrDisable(SpeakTooltipCheckBox);
            }
            _settings.SpeakTooltip = SpeakTooltipCheckBox.IsChecked == true;
            _settings.Save();
        }

        private void SpeakMenuContent()
        {
            if (SpeakMenuCheckBox.IsChecked != true)
                return;

            var text = MenuBox.Text;
            if (string.IsNullOrWhiteSpace(text))
                return;

            try
            {
                Tolk.Output(text, true);
            }
            catch (Exception ex)
            {
                AppendLog($"Tolk speak failed: {ex.Message}");
                SpeakMenuCheckBox.IsChecked = false;
            }
        }

        private void SpeakTooltipIfEnabled(IEnumerable<string> lines)
        {
            if (SpeakTooltipCheckBox.IsChecked != true)
                return;

            var text = string.Join(Environment.NewLine, lines);
            if (string.IsNullOrWhiteSpace(text))
                return;

            try
            {
                Tolk.Output(text, true);
            }
            catch (Exception ex)
            {
                AppendLog($"Tolk speak tooltip failed: {ex.Message}");
                SpeakTooltipCheckBox.IsChecked = false;
            }
        }

        private void TryLoadTolkOrDisable(CheckBox sourceCheckBox)
        {
            try
            {
                Tolk.Load();
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to load Tolk: {ex.Message}");
                sourceCheckBox.IsChecked = false;
            }
        }
    }
}
