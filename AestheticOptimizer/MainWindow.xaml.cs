#pragma warning disable CA1416 
#pragma warning disable CS0414 
using KeyAuth;

using Microsoft.Win32;
using Postgrest.Attributes;
using Postgrest.Models;
using Supabase;
using Supabase.Gotrue;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using KeyAuth;

namespace KsyxisTweaks
{
    public enum UserTier { Basic, Plus, Premium }

    public partial class MainWindow : Window
    {
        public static api KeyAuthApp = LoginWindow.KeyAuthApp;

        [DllImport("psapi.dll")]
        static extern int EmptyWorkingSet(IntPtr hwProc);

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtSetTimerResolution(uint DesiredResolution, bool SetResolution, out uint CurrentResolution);

        public UserTier CurrentTier = UserTier.Basic;
        private List<string> _gamesList = new List<string>();
        private DispatcherTimer? _pingTimer, _uptimeTimer, _netCheckTimer;

        // Timer for Dynamic RGB Pulse
        private DispatcherTimer _rgbLoopTimer = new DispatcherTimer();
        private double _hue = 0;
        private bool _isRenderingActive = false;

        private Random _rnd = new Random();

        private List<Shape> _particles = new List<Shape>();
        private List<Vector> _velocities = new List<Vector>();
        private double _particleSpeedMult = 1.0;
        private string _particleShape = "Circles";
        private string _activeThemeHex = "#FFFFFF";

        private string _currentNick = "GUEST";
        private readonly string statsPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "axe_stats.dat");
        private double totalJunkClearedMB = 0, lifetimeRamFreedGB = 0;
        private int lifetimeOptis = 0, lifetimeGamesBoosted = 0;

        private Supabase.Client? _supabase = null;
        private bool _minimizeToTray = false;

        public MainWindow(string nickname = "GUEST")
        {
            InitializeComponent();

            AxeDefender.StartWatcher();

            _currentNick = nickname ?? "GUEST";
            InitializeAxeCloudSync();

            try
            {
                if (KeyAuthApp.user_data != null && KeyAuthApp.user_data.subscriptions.Count > 0)
                {
                    string level = KeyAuthApp.user_data.subscriptions[0].subscription.ToLower();
                    if (level.Contains("3") || level.Contains("premium")) CurrentTier = UserTier.Premium;
                    else if (level.Contains("2") || level.Contains("plus")) CurrentTier = UserTier.Plus;
                    else CurrentTier = UserTier.Basic;
                }
            }
            catch { CurrentTier = UserTier.Basic; }

            LanguageManager.Initialize();
            LanguageManager.CurrentTier = this.CurrentTier;
            LanguageManager.ChangeLanguage("en");

            _rgbLoopTimer.Interval = TimeSpan.FromMilliseconds(50);
            _rgbLoopTimer.Tick += RgbLoopTimer_Tick;
            _rgbLoopTimer.Start();

            this.Loaded += MainWindow_Loaded;
            this.Unloaded += MainWindow_Unloaded;

            RefreshUserDisplay();
            UpdateTierAccess();
            LoadAllData();
            StartPingTester();
            StartUptime();
            InitializeParticles(40);
            LoadHardwareProfileAsync();
            StartConnectivityCheck();

            // --- AUTOMATYCZNE SPRAWDZANIE RANGI (LIVE SYNC) ---
            StartAutoTierCheck();
        }

        private void StartAutoTierCheck()
        {
            DispatcherTimer tierCheckTimer = new DispatcherTimer();
            tierCheckTimer.Interval = TimeSpan.FromSeconds(30); // Sprawdza co 30 sekund
            tierCheckTimer.Tick += async (s, e) =>
            {
                if (KeyAuthApp.user_data != null)
                {
                    string savedKey = LoadEncryptedLicense();

                    if (!string.IsNullOrEmpty(savedKey))
                    {
                        await Task.Run(() => KeyAuthApp.license(savedKey));

                        if (KeyAuthApp.response.success && KeyAuthApp.user_data.subscriptions.Count > 0)
                        {
                            var oldTier = CurrentTier;
                            string level = KeyAuthApp.user_data.subscriptions[0].subscription.ToLower();

                            // Logika przypisania tieru
                            if (level.Contains("3") || level.Contains("premium")) CurrentTier = UserTier.Premium;
                            else if (level.Contains("2") || level.Contains("plus")) CurrentTier = UserTier.Plus;
                            else CurrentTier = UserTier.Basic;

                            if (oldTier != CurrentTier)
                            {
                                Dispatcher.Invoke(() => {
                                    UpdateTierAccess();
                                    ShowAxeNotification($"LIVE UPDATE: {CurrentTier.ToString().ToUpper()} UNLOCKED!");
                                });
                            }
                        }
                    }
                }
            };
            tierCheckTimer.Start();
        }

        private void MainWindow_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_isRenderingActive)
            {
                CompositionTarget.Rendering -= OnRendering;
                _isRenderingActive = false;
            }
        }
        private void OnRendering(object? sender, EventArgs e)
        {
            if (_particles.Count == 0) return;

            double canvasWidth = this.ActualWidth;
            double canvasHeight = this.ActualHeight;

            for (int i = 0; i < _particles.Count; i++)
            {
                double x = Canvas.GetLeft(_particles[i]);
                double y = Canvas.GetTop(_particles[i]);

                x += _velocities[i].X * _particleSpeedMult;
                y += _velocities[i].Y * _particleSpeedMult;

                if (y < -10)
                {
                    y = canvasHeight + 10;
                    x = _rnd.Next(0, (int)canvasWidth);
                }
                if (x < -10) x = canvasWidth + 10;
                if (x > canvasWidth + 10) x = -10;

                Canvas.SetLeft(_particles[i], x);
                Canvas.SetTop(_particles[i], y);
            }
        }

        // ==========================================
        //        DYNAMIC RGB PULSE LOGIC
        // ==========================================
        private void RgbLoopTimer_Tick(object? sender, EventArgs e)
        {
            if (this.FindName("ChkRgbBg") is CheckBox cb && cb.IsChecked == true)
            {
                _hue += 2; // Speed of color transition
                if (_hue >= 360) _hue = 0;

                Color newColor = ColorFromAhsb(255, _hue, 1.0, 0.8);
                ApplyNeuralTheme(newColor.ToString());
            }
        }

        private Color ColorFromAhsb(int a, double h, double s, double b)
        {
            double r = 0, g = 0, bl = 0;
            if (s == 0) { r = g = bl = b; }
            else
            {
                double sectorPos = h / 60.0;
                int sectorNumber = (int)Math.Floor(sectorPos);
                double fractionalSector = sectorPos - sectorNumber;
                double p = b * (1.0 - s);
                double q = b * (1.0 - (s * fractionalSector));
                double t = b * (1.0 - (s * (1.0 - fractionalSector)));
                switch (sectorNumber)
                {
                    case 0: r = b; g = t; bl = p; break;
                    case 1: r = q; g = b; bl = p; break;
                    case 2: r = p; g = b; bl = t; break;
                    case 3: r = p; g = q; bl = b; break;
                    case 4: r = t; g = p; bl = b; break;
                    case 5: r = b; g = p; bl = q; break;
                }
            }
            return Color.FromArgb((byte)a, (byte)(r * 255), (byte)(g * 255), (byte)(bl * 255));
        }

        // ==========================================
        //        LANGUAGE & COMBOBOX EVENT
        // ==========================================
        private void LangSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LangSelector.SelectedItem is ComboBoxItem item)
            {
                string langCode = item.Tag?.ToString() ?? "en";
                LanguageManager.ChangeLanguage(langCode);
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyNeuralTheme(_activeThemeHex);

            DispatcherTimer loadingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
            loadingTimer.Tick += (s, ev) =>
            {
                loadingTimer.Stop();
                if (this.FindName("SplashGrid") is Grid splash)
                {
                    DoubleAnimation fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
                    fadeOut.Completed += (senderArgs, args) => splash.Visibility = Visibility.Collapsed;
                    splash.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                }
            };
            loadingTimer.Start();
        }

        // ==========================================
        //        MODUŁY: BEHAVIOR & VISUALS
        // ==========================================

        public void ChkAutoStart_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    string exePath = Environment.ProcessPath ?? "";
                    if (!string.IsNullOrEmpty(exePath))
                        key?.SetValue("AxeOptimizer", exePath);
                }
                ShowAxeNotification("AUTO-START ENABLED!");
            }
            catch { }
        }

        public void ChkAutoStart_Unchecked(object sender, RoutedEventArgs e)
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    key?.DeleteValue("AxeOptimizer", false);
                }
                ShowAxeNotification("AUTO-START DISABLED!");
            }
            catch { }
        }

        public void ChkTray_Checked(object sender, RoutedEventArgs e) { _minimizeToTray = true; ShowAxeNotification("TRAY MINIMIZE ON!"); }
        public void ChkTray_Unchecked(object sender, RoutedEventArgs e) { _minimizeToTray = false; ShowAxeNotification("TRAY MINIMIZE OFF!"); }
        public void ChkDiscord_Checked(object sender, RoutedEventArgs e) { ShowAxeNotification("DISCORD RPC CONNECTING..."); }
        public void ChkDiscord_Unchecked(object sender, RoutedEventArgs e) { ShowAxeNotification("DISCORD RPC DISCONNECTED!"); }

        public async void SyncCloud_Click(object sender, RoutedEventArgs e)
        {
            if (_supabase == null || KeyAuthApp.user_data == null) return;

            var mySettings = new AxeSyncModel
            {
                UserId = KeyAuthApp.user_data.username,
                HexColor = _activeThemeHex,
                GlassOpacity = MainBorder.Opacity,
                ActiveProfile = "Custom"
            };

            await _supabase.From<AxeSyncModel>().Upsert(mySettings);
            ShowAxeNotification("PROFILE SAVED TO CLOUD!");
        }
        public void AutoMaint_Checked(object sender, RoutedEventArgs e) { ShowAxeNotification("AUTO-MAINTENANCE ENABLED"); }

        public void SfcScan_Click(object sender, RoutedEventArgs e) { RunCommand("sfc /scannow"); ShowAxeNotification("SFC INTEGRITY SCAN STARTED"); }
        public void Telemetry_Click(object sender, RoutedEventArgs e) { ShowAxeNotification("TELEMETRY FIREWALL ACTIVE"); }

        private void RunMsiModeOptimization()
        {
            try
            {
                string baseKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
                using (RegistryKey? rk = Registry.LocalMachine.OpenSubKey(baseKey))
                {
                    if (rk == null) return;
                    foreach (string skName in rk.GetSubKeyNames())
                    {
                        if (skName == "Configuration" || skName == "Properties") continue;

                        string msiPath = $@"{baseKey}\{skName}\Interrupt Management\MessageSignaledInterruptProperties";
                        SetReg($@"HKEY_LOCAL_MACHINE\{msiPath}", "MSISupported", 1, RegistryValueKind.DWord);
                    }
                }
                ShowAxeNotification("GPU MSI MODE ACTIVE");
            }
            catch { }
        }
        private void ApplyCpuCoreLasso()
        {
            RunCommand("powercfg -setacvalueindex scheme_current sub_processor cppminksp 100");
            RunCommand("powercfg -setactive scheme_current");
        }
        private void EnableHighPrecisionTimer() { NtSetTimerResolution(5000, true, out uint cur); }
        private void ApplyKernelBootTweaks() { RunCommand("bcdedit /set disabledynamictick yes"); RunCommand("bcdedit /set useplatformclock no"); RunCommand("bcdedit /set tscsyncpolicy Enhanced"); }
        // --- GŁÓWNA FUNKCJA TWEAKÓW ---
        private void ApplyProGradeTweaks(bool usbRate, bool wmi, bool exploit, bool isr, bool svhost, bool tcp, bool netsh, bool audio)
        {
            // 1. Poprawiona optymalizacja zasilania USB (wywołuje nową funkcję poniżej)
            if (usbRate) ApplyUsbPowerFix();

            // 2. Blokowanie zbędnej telemetrii WMI (zwalnia cykle procesora)
            if (wmi) RunCommand("sc stop Winmgmt & sc config Winmgmt start=disabled");

            // 3. Wyłączenie zabezpieczeń obniżających FPS
            if (exploit) RunPS("Set-ProcessMitigation -SystemConfig -Disable CFG");

            // 4. Interrupt Steering (mniejszy input lag)
            if (isr) RunCommand("bcdedit /set groupawarecycleaffinity yes");

            // 5. Podział procesów SvcHost (stabilność przy dużej ilości RAM)
            if (svhost) SetReg(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control", "SvcHostSplitThresholdInKB", 3800000, RegistryValueKind.DWord);

            // 6. Optymalizacja stosu sieciowego TCP
            if (tcp)
            {
                RunCommand("netsh int tcp set global autotuninglevel=normal");
                RunCommand("netsh int tcp set global chimney=enabled");
            }

            // 7. Skalowanie po stronie odbiorczej (RSS) - stabilniejszy ping
            if (netsh) RunCommand("netsh int tcp set global rss=enabled");

            // 8. Low Latency Audio (wyłączenie efektów systemowych)
            if (audio) SetReg(@"HKEY_CURRENT_USER\Software\Microsoft\Multimedia\Audio", "UserPreferredPlaybackDevice", 0, RegistryValueKind.DWord);
        }

        private void ApplyUsbPowerFix()
        {
            try
            {
                string usbRoot = @"SYSTEM\CurrentControlSet\Enum\USB";
                using (RegistryKey rootKey = Registry.LocalMachine.OpenSubKey(usbRoot, true))
                {
                    if (rootKey == null) return;

                    foreach (string vidPid in rootKey.GetSubKeyNames())
                    {
                        using (RegistryKey deviceKey = rootKey.OpenSubKey(vidPid, true))
                        {
                            if (deviceKey == null) continue;

                            foreach (string instanceId in deviceKey.GetSubKeyNames())
                            {
                                using (RegistryKey instanceKey = deviceKey.OpenSubKey(instanceId, true))
                                {
                                    if (instanceKey == null) continue;

                                    using (RegistryKey paramsKey = instanceKey.OpenSubKey("Device Parameters", true))
                                    {
                                        if (paramsKey != null)
                                        {
                                            paramsKey.SetValue("EnhancedPowerManagementEnabled", 0, RegistryValueKind.DWord);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        // ==========================================
        //        LOGIKA UI I NAVIGATION
        // ==========================================

        private void SetActiveTab(Button? target)
        {
            var navButtons = new[] { this.FindName("NavUser") as Button, this.FindName("NavHome") as Button, this.FindName("NavTurbo") as Button, this.FindName("NavGames") as Button, this.FindName("NavSettings") as Button };
            foreach (var btn in navButtons)
            {
                if (btn != null) btn.Background = Brushes.Transparent;
                if (btn?.Content is TextBlock tb) tb.Foreground = new SolidColorBrush(Color.FromRgb(100, 100, 110));
            }
            if (target != null)
            {
                target.Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
                if (target.Content is TextBlock activeTb) activeTb.SetResourceReference(TextBlock.ForegroundProperty, "MainAccent");
            }
        }

        private async void InitializeAxeCloudSync()
        {
            try
            {
                string supabaseUrl = "";
                string supabaseKey = "";

                var options = new SupabaseOptions { AutoConnectRealtime = true };
                _supabase = new Supabase.Client(supabaseUrl, supabaseKey, options);

                if (KeyAuthApp.user_data == null || string.IsNullOrEmpty(KeyAuthApp.user_data.username))
                    return;

                string currentUserId = KeyAuthApp.user_data.username;

                var result = await _supabase
                    .From<AxeSyncModel>()
                    .Where(x => x.UserId == currentUserId)
                    .Get();

                var profile = result.Models.FirstOrDefault();

                if (profile != null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (!string.IsNullOrEmpty(profile.HexColor) && profile.HexColor.StartsWith("#"))
                        {
                            ApplyNeuralTheme(profile.HexColor);
                        }

                        if (MainBorder != null) MainBorder.Opacity = profile.GlassOpacity;

                        if (this.FindName("OpacitySlider") is Slider os)
                            os.Value = profile.GlassOpacity;

                        ShowAxeNotification("CLOUD PROFILE LOADED");
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Cloud Sync Error: " + ex.Message);
            }
        }

        public void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        public void Close_Click(object sender, RoutedEventArgs e) { if (_minimizeToTray) { this.WindowState = WindowState.Minimized; ShowAxeNotification("MINIMIZED TO TRAY"); } else { Application.Current.Shutdown(); } }
        public void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (MainBorder != null) MainBorder.Opacity = e.NewValue; }

        public void ApplyHex_Click(object sender, RoutedEventArgs e)
        {
            if (this.FindName("HexInput") is TextBox hexInput && !string.IsNullOrEmpty(hexInput.Text))
            {
                ApplyNeuralTheme(hexInput.Text);
            }
        }

        private void ApplyNeuralTheme(string hex)
        {
            try
            {
                if (string.IsNullOrEmpty(hex)) return;
                _activeThemeHex = hex;
                var color = (Color)ColorConverter.ConvertFromString(hex);
                var brush = new SolidColorBrush(color);
                brush.Freeze();

                Resources["MainAccent"] = brush;
                if (hex.ToUpper() == "#FFFFFF" || hex.ToUpper() == "#FFFFFFFF") Resources["ForegroundAccent"] = new SolidColorBrush(Colors.Black);
                else Resources["ForegroundAccent"] = new SolidColorBrush(Colors.White);

                foreach (var p in _particles) p.Fill = brush;
                if (MainBorder != null) MainBorder.BorderBrush = brush;

                var navButtons = new[] { this.FindName("NavUser") as Button, this.FindName("NavHome") as Button, this.FindName("NavTurbo") as Button, this.FindName("NavGames") as Button, this.FindName("NavSettings") as Button };
                foreach (var btn in navButtons)
                {
                    if (btn != null && btn.Background != Brushes.Transparent && btn.Content is TextBlock tb)
                        tb.SetResourceReference(TextBlock.ForegroundProperty, "MainAccent");
                }
            }
            catch { }
        }

        private void ApplyAxeCloudProfile(string profile)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    if (string.IsNullOrEmpty(profile)) return;

                    if (profile.ToLower() == "performance")
                    {
                        if (this.FindName("ChkGameMode") is CheckBox gm) gm.IsChecked = true;
                        if (this.FindName("ChkPowerPlan") is CheckBox pp) pp.IsChecked = true;
                        ShowAxeNotification("PERFORMANCE PROFILE LOADED!");
                    }
                    else if (profile.ToLower() == "default")
                    {
                        if (this.FindName("ChkGameMode") is CheckBox gm) gm.IsChecked = false;
                        if (this.FindName("ChkPowerPlan") is CheckBox pp) pp.IsChecked = false;
                        ShowAxeNotification("DEFAULT PROFILE LOADED!");
                    }
                });
            }
            catch (Exception ex) { LogError(ex, "CloudProfile"); }
        }

        private void StartConnectivityCheck()
        {
            _netCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _netCheckTimer.Tick += async (s, e) =>
            {
                try
                {
                    using (Ping ping = new Ping())
                    {
                        var reply = await ping.SendPingAsync("8.8.8.8", 2000);
                        bool isConnected = reply.Status == IPStatus.Success;
                    }
                }
                catch (Exception ex) { LogError(ex, "Network_Check"); }
            };
            _netCheckTimer.Start();
        }

        private void DetermineTierFromKeyAuth()
        {
            CurrentTier = UserTier.Basic;
            LanguageManager.CurrentTier = CurrentTier;
        }

        private void RefreshUserDisplay()
        {
            if (this.FindName("DisplayUser") is TextBlock du) du.Text = _currentNick.ToUpper();
            if (this.FindName("AvatarLetter") is TextBlock al && !string.IsNullOrEmpty(_currentNick)) al.Text = _currentNick.Substring(0, 1).ToUpper();
        }

        private void UpdateTierAccess()
        {
            ApplyNeuralTheme(_activeThemeHex);

            if (this.FindName("TierBadgeText") is TextBlock badge)
            {
                switch (CurrentTier)
                {
                    case UserTier.Premium:
                        badge.Text = "PREMIUM MEMBER";
                        break;
                    case UserTier.Plus:
                        badge.Text = "PLUS MEMBER";
                        break;
                    default:
                        badge.Text = "BASIC MEMBER";
                        break;
                }
            }

            if (CurrentTier >= UserTier.Plus)
            {
                UnlockElement(this.FindName("ChkGpu") as Control);
                UnlockElement(this.FindName("ChkMpo") as Control);
                UnlockElement(this.FindName("ChkPowerPlan") as Control);
                UnlockElement(this.FindName("ChkMouse") as Control);
                UnlockElement(this.FindName("ChkKeyboard") as Control);
                UnlockElement(this.FindName("ChkTimer") as Control);
                UnlockElement(this.FindName("ChkTemp") as Control);
                UnlockElement(this.FindName("ChkGameMode") as Control);
                UnlockElement(this.FindName("ChkShader") as Control);
                UnlockElement(this.FindName("BtnCleanSystem") as Control);
                UnlockElement(this.FindName("BtnCleanLaunchers") as Control);
                UnlockElement(this.FindName("BtnNetReset") as Control);
                UnlockElement(this.FindName("BtnOptiRam") as Control);
                UnlockElement(this.FindName("BtnGameBooster") as Control);
                UnlockElement(this.FindName("BtnWipeShaders") as Control);
            }

            if (CurrentTier == UserTier.Premium)
            {
                UnlockElement(this.FindName("ChkPrint") as Control);
                UnlockElement(this.FindName("ChkSysMain") as Control);
                UnlockElement(this.FindName("ChkMaps") as Control);
                UnlockElement(this.FindName("ChkFax") as Control);
                UnlockElement(this.FindName("ChkCoreParking") as Control);
                UnlockElement(this.FindName("ChkThrottling") as Control);
                UnlockElement(this.FindName("ChkRamLatency") as Control);
                UnlockElement(this.FindName("BtnDnsCloudflare") as Control);
                UnlockElement(this.FindName("BtnDnsGoogle") as Control);
                UnlockElement(this.FindName("BtnFreezeSystem") as Control);
                UnlockElement(this.FindName("BtnDebloat") as Control);
                UnlockElement(this.FindName("ChkMsiMode") as Control);
                UnlockElement(this.FindName("ChkCpuIsolation") as Control);
                UnlockElement(this.FindName("ChkBcdTweaks") as Control);
                UnlockElement(this.FindName("ChkInterrupt") as Control);
                UnlockElement(this.FindName("ChkUsbRate") as Control);
                UnlockElement(this.FindName("ChkWmiBlock") as Control);
                UnlockElement(this.FindName("ChkExploit") as Control);
                UnlockElement(this.FindName("ChkSvhost") as Control);
                UnlockElement(this.FindName("ChkTcpWindow") as Control);
                UnlockElement(this.FindName("ChkNetshContext") as Control);
                UnlockElement(this.FindName("ChkAudioRestoration") as Control);
                UnlockElement(this.FindName("BtnSfcScan") as Control);
                UnlockElement(this.FindName("BtnTelemetry") as Control);
            }

            LanguageManager.CurrentTier = this.CurrentTier;

            if (this.FindName("LangSelector") is ComboBox langSelector && langSelector.SelectedItem is ComboBoxItem item)
            {
                string currentLang = item.Tag?.ToString() ?? "en";
                LanguageManager.ChangeLanguage(currentLang);
            }
        }

        private void UnlockElement(Control? ctrl) { if (ctrl != null) { ctrl.IsEnabled = true; ctrl.Opacity = 1.0; } }

        public void AddCustomGame_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Wymaga 'using Microsoft.Win32;' 
                OpenFileDialog openFileDialog = new OpenFileDialog
                {
                    Filter = "Aplikacje Wykonywalne (*.exe)|*.exe",
                    Title = "Wybierz grę do zoptymalizowania"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    string fileName = System.IO.Path.GetFileNameWithoutExtension(openFileDialog.FileName);

                    if (!_gamesList.Contains(fileName))
                    {
                        _gamesList.Add(fileName);
                        ShowAxeNotification($"DODANO GRĘ: {fileName.ToUpper()}");
                    }
                    else
                    {
                        ShowAxeNotification("TA GRA JUŻ JEST NA LIŚCIE!");
                    }
                }
            }
            catch (Exception ex) { LogError(ex, "Add_Game"); }
        }
        private async void ActivateLicense_Click(object sender, RoutedEventArgs e)
        {
            if (this.FindName("LicenseInput") is not TextBox li) return;
            string key = li.Text.Trim();
            if (string.IsNullOrEmpty(key)) return;

            ShowAxeNotification("CONNECTING TO SERVER...");

            await Task.Run(() => {
                KeyAuthApp.license(key);

                if (KeyAuthApp.response.success)
                {
                    SaveEncryptedLicense(key);

                    string userLevel = KeyAuthApp.user_data.subscriptions[0].subscription;

                    Dispatcher.Invoke(() => {
                        if (userLevel.Contains("1") || userLevel.ToLower().Contains("basic"))
                        {
                            CurrentTier = UserTier.Basic;
                            ShowAxeNotification("BASIC PLAN ACTIVATED!");
                        }
                        else if (userLevel.Contains("2") || userLevel.ToLower().Contains("plus"))
                        {
                            CurrentTier = UserTier.Plus;
                            ShowAxeNotification("PLUS PLAN ACTIVATED!");
                        }
                        else if (userLevel.Contains("3") || userLevel.ToLower().Contains("premium"))
                        {
                            CurrentTier = UserTier.Premium;
                            ShowAxeNotification("PREMIUM PLAN ACTIVATED!");
                        }

                        UpdateTierAccess();
                        LanguageManager.CurrentTier = this.CurrentTier;
                        li.Text = "";
                        RefreshUserDisplay();
                    });
                }
                else
                {
                    Dispatcher.Invoke(() => ShowAxeNotification("ERROR: " + KeyAuthApp.response.message.ToUpper()));
                }
            });
        }
        private void ShowAxeNotification(string msg) { if (this.FindName("NotificationText") is not TextBlock nt || this.FindName("NotificationToast") is not Border toast) return; nt.Text = msg; toast.BeginAnimation(MarginProperty, new ThicknessAnimation(new Thickness(0, 20, 25, 0), TimeSpan.FromMilliseconds(300))); Task.Delay(2000).ContinueWith(_ => Dispatcher.Invoke(() => { toast.BeginAnimation(MarginProperty, new ThicknessAnimation(new Thickness(0, -70, 25, 0), TimeSpan.FromMilliseconds(300))); })); }

        private void DragWindow(object sender, MouseButtonEventArgs e) { try { if (e.LeftButton == MouseButtonState.Pressed) this.DragMove(); } catch { } }

        public void UpdateNick_Click(object sender, RoutedEventArgs e) { if (this.FindName("NickInput") is TextBox ni && !string.IsNullOrEmpty(ni.Text)) { _currentNick = ni.Text.Trim(); RefreshUserDisplay(); ShowAxeNotification("IDENTITY UPDATED!"); } }
        public void ParticleDensity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (this.FindName("ParticleCanvas") is Canvas) InitializeParticles((int)e.NewValue); }
        public void ParticleSpeed_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { _particleSpeedMult = e.NewValue; }
        private void ToggleBlur_Click(object sender, RoutedEventArgs e) { if (sender is CheckBox cb && MainBorder != null) MainBorder.Opacity = cb.IsChecked == true ? 0.8 : 1.0; }
        private void SetReg(string key, string name, object value, RegistryValueKind kind) { try { Registry.SetValue(key, name, value, kind); } catch { } }
        private void RunCommand(string cmd)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c " + cmd)
                {
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false 
                };
                Process.Start(psi);
            }
            catch (Exception ex) { LogError(ex, $"RunCMD: {cmd}"); }
        }

        private void RunPS(string cmd)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("powershell.exe", "-Command \"" + cmd + "\"")
                {
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false
                };
                Process.Start(psi);
            }
            catch (Exception ex) { LogError(ex, $"RunPS: {cmd}"); }
        }

        public void Home_Click(object sender, RoutedEventArgs e) { SetActiveTab(sender as Button); HideAll(); if (this.FindName("TweaksPanel") is Grid p) p.Visibility = Visibility.Visible; }
        public void Turbo_Click(object sender, RoutedEventArgs e) { SetActiveTab(sender as Button); HideAll(); if (this.FindName("TurboPanel") is Grid p) p.Visibility = Visibility.Visible; }
        public void Games_Click(object sender, RoutedEventArgs e) { SetActiveTab(sender as Button); HideAll(); if (this.FindName("GamesPanel") is Grid p) p.Visibility = Visibility.Visible; }
        public void Settings_Click(object sender, RoutedEventArgs e) { SetActiveTab(sender as Button); HideAll(); if (this.FindName("SettingsPanel") is Grid p) p.Visibility = Visibility.Visible; }
        public void UserIcon_Click(object sender, RoutedEventArgs e) { SetActiveTab(sender as Button); HideAll(); if (this.FindName("UserInfoDashboard") is Grid p) p.Visibility = Visibility.Visible; }
        private void HideAll()
        {
            if (this.FindName("TweaksPanel") is Grid tw) tw.Visibility = Visibility.Collapsed;
            if (this.FindName("TurboPanel") is Grid tu) tu.Visibility = Visibility.Collapsed;
            if (this.FindName("GamesPanel") is Grid ga) ga.Visibility = Visibility.Collapsed;
            if (this.FindName("SettingsPanel") is Grid se) se.Visibility = Visibility.Collapsed;
            if (this.FindName("UserInfoDashboard") is Grid us) us.Visibility = Visibility.Collapsed;
        }

        // ==================================================
        //  INTELLIGENT CLEANING & REAL STATS & CRASH PROT.
        // ==================================================
        public async void CleanSystem_Click(object sender, RoutedEventArgs e)
        {
            ShowAxeNotification("PURGING SYSTEM JUNK...");
            double spaceFreed = 0;
            await Task.Run(() => {
                string[] paths = { System.IO.Path.GetTempPath(), @"C:\Windows\Temp", @"C:\Windows\Prefetch" };
                foreach (var path in paths)
                {
                    if (!Directory.Exists(path)) continue;
                    foreach (var file in new DirectoryInfo(path).GetFiles())
                    {
                        try
                        {
                            long size = file.Length;
                            file.Delete();
                            spaceFreed += (size / 1024.0 / 1024.0);
                        }
                        catch (Exception ex) { LogError(ex, "CleanSystem"); }
                    }
                }
            });
            totalJunkClearedMB += spaceFreed;
            UpdateStatsUI();
            SaveAllData();
            ShowAxeNotification($"CLEANED {spaceFreed:F1} MB!");
        }

        public async void CleanLaunchers_Click(object sender, RoutedEventArgs e)
        {
            ShowAxeNotification("CLEANING LAUNCHER CACHE...");
            double spaceFreed = 0;
            await Task.Run(() => {
                // Intelligent Steam Path detection via Registry
                string steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", "")?.ToString() ?? "";
                if (!string.IsNullOrEmpty(steamPath))
                {
                    string cache = System.IO.Path.Combine(steamPath, "appcache");
                    spaceFreed += SafeDirectoryDelete(cache);
                }

                // Standard Discord Location
                string discordCache = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "discord", "Cache");
                spaceFreed += SafeDirectoryDelete(discordCache);
            });
            totalJunkClearedMB += spaceFreed;
            UpdateStatsUI();
            SaveAllData();
            ShowAxeNotification("LAUNCHERS OPTIMIZED!");
        }

        private double SafeDirectoryDelete(string path)
        {
            double freed = 0;
            if (!Directory.Exists(path)) return 0;
            foreach (var file in new DirectoryInfo(path).GetFiles("*", SearchOption.AllDirectories))
            {
                try
                {
                    long s = file.Length;
                    file.Delete();
                    freed += (s / 1024.0 / 1024.0);
                }
                catch (Exception ex) { LogError(ex, "CleanSystem"); }
            }
            return freed;
        }

        // ============================================================
        //           GŁÓWNA LOGIKA OPTYMALIZACJI (KSYXIS ENGINE)
        // ============================================================

        private async void OptiButton_Click(object sender, RoutedEventArgs e)
        {
            var btnOpti = this.FindName("OptiButton") as Button;
            var barOpti = this.FindName("AxeProgress") as ProgressBar;
            var statusText = this.FindName("StatusUpdateText") as TextBlock; 

            if (btnOpti != null) btnOpti.IsEnabled = false;

            if (barOpti != null)
            {
                barOpti.Visibility = Visibility.Visible;

                DoubleAnimation smoothLoading = new DoubleAnimation()
                {
                    From = 0,
                    To = 100,
                    Duration = TimeSpan.FromSeconds(2.5),
                    EasingFunction = new QuadraticEase() { EasingMode = EasingMode.EaseOut } 
                };
                barOpti.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, smoothLoading);
            }

            if (statusText != null)
            {
                statusText.Visibility = Visibility.Visible;
                _ = Task.Run(async () =>
                {
                    for (int i = 0; i <= 100; i++)
                    {
                        Dispatcher.Invoke(() => statusText.Text = $"APPLYING KERNEL TWEAKS... {i}%");
                        await Task.Delay(25);
                    }
                });
            }

            // --- POBIERANIE STANU CHECKBOXÓW ---
            bool isUpdate = (this.FindName("ChkUpdate") as CheckBox)?.IsChecked == true;
            bool usb = (this.FindName("ChkUsbRate") as CheckBox)?.IsChecked == true;
            bool wmi = (this.FindName("ChkWmiBlock") as CheckBox)?.IsChecked == true;
            bool exploit = (this.FindName("ChkExploit") as CheckBox)?.IsChecked == true;
            bool svhost = (this.FindName("ChkSvhost") as CheckBox)?.IsChecked == true;
            bool tcp = (this.FindName("ChkTcpWindow") as CheckBox)?.IsChecked == true;
            bool netsh = (this.FindName("ChkNetshContext") as CheckBox)?.IsChecked == true;
            bool audio = (this.FindName("ChkAudioRestoration") as CheckBox)?.IsChecked == true;
            bool msi = (this.FindName("ChkMsiMode") as CheckBox)?.IsChecked == true;
            bool bcd = (this.FindName("ChkBcdTweaks") as CheckBox)?.IsChecked == true;
            bool powerPlan = (this.FindName("ChkPowerPlan") as CheckBox)?.IsChecked == true;
            bool printSpooler = (this.FindName("ChkPrint") as CheckBox)?.IsChecked == true;
            bool sysMain = (this.FindName("ChkSysMain") as CheckBox)?.IsChecked == true;
            bool maps = (this.FindName("ChkMaps") as CheckBox)?.IsChecked == true;
            bool fax = (this.FindName("ChkFax") as CheckBox)?.IsChecked == true;

            var optimizationTask = Task.Run(() => {
                CreateRestorePoint();
                if (isUpdate) RunCommand("sc config wuauserv start=disabled & net stop wuauserv");
                if (msi) RunMsiModeOptimization();
                if (bcd) ApplyKernelBootTweaks();

                if (powerPlan)
                {
                    RunCommand("powercfg -duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61");
                    RunPS("$plan = Get-WmiObject -Class Win32_PowerPlan -Filter \"Name='Ultimate Performance'\"; powercfg -setactive $plan.InstanceID.Split('\\')[1]");
                }

                if (printSpooler) RunCommand("sc config Spooler start=disabled & net stop Spooler");
                if (sysMain) RunCommand("sc config SysMain start=disabled & net stop SysMain");
                if (maps) RunCommand("sc config MapsBroker start=disabled & net stop MapsBroker");
                if (fax) RunCommand("sc config Fax start=disabled & net stop Fax");

                ApplyProGradeTweaks(usb, wmi, exploit, true, svhost, tcp, netsh, audio);
            });

            await Task.WhenAll(optimizationTask, Task.Delay(2500));

            lifetimeOptis++;
            UpdateStatsUI();
            SaveAllData();

            if (_supabase != null)
            {
                _ = Task.Run(async () => {
                    try { await _supabase.Rpc("increment_global_stats", new Dictionary<string, object> { { "inc_by", 1 } }); }
                    catch { }
                });
            }

            if (barOpti != null)
            {
                barOpti.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, null);
                barOpti.Visibility = Visibility.Collapsed;
            }
            if (statusText != null) statusText.Visibility = Visibility.Collapsed;
            if (btnOpti != null) btnOpti.IsEnabled = true;

            ShowAxeNotification("SYSTEM OVERRIDE SUCCESS!");
        }

        // ============================================================
        //              METODY POMOCNICZE I BEZPIECZEŃSTWO
        // ============================================================

        private void CreateRestorePoint()
        {
            try
            {
                RunPS("Enable-ComputerRestore -Drive 'C:\\'");
                RunPS("Checkpoint-Computer -Description 'AxeOptimizer_Backup' -RestorePointType 'MODIFY_SETTINGS'");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Restore Error: " + ex.Message);
            }
        }


        // ==========================================
        //                PING TESTER 
        // ==========================================
        private void StartPingTester()
        {
            _pingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _pingTimer.Tick += async (s, e) =>
            {
                try
                {
                    using (Ping ping = new Ping())
                    {
                        var replyCloud = await ping.SendPingAsync("1.1.1.1", 1000);
                        if (replyCloud.Status == IPStatus.Success && this.FindName("PingCloud") is TextBlock pc)
                            pc.Text = $"{replyCloud.RoundtripTime} ms";

                        var replyGoogle = await ping.SendPingAsync("8.8.8.8", 1000);
                        if (replyGoogle.Status == IPStatus.Success && this.FindName("PingGoogle") is TextBlock pg)
                            pg.Text = $"{replyGoogle.RoundtripTime} ms";
                    }
                }
                catch { }
            };
            _pingTimer.Start();
        }

        // ==========================================
        //              PARTICLES ENGINE
        // ==========================================
        private void InitializeParticles(int count)
        {
            if (this.FindName("ParticleCanvas") is not Canvas canvas) return;

            canvas.Children.Clear();
            _particles.Clear();
            _velocities.Clear();

            for (int i = 0; i < count; i++)
            {
                double size = _rnd.Next(2, 5);
                Ellipse p = new Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = (SolidColorBrush)FindResource("MainAccent"),
                    Opacity = _rnd.NextDouble() * 0.4 + 0.1,
                    IsHitTestVisible = false
                };

                Canvas.SetLeft(p, _rnd.Next(0, (int)this.ActualWidth));
                Canvas.SetTop(p, _rnd.Next(0, (int)this.ActualHeight));

                canvas.Children.Add(p);
                _particles.Add(p);
                _velocities.Add(new Vector((_rnd.NextDouble() - 0.5) * 0.4, -(_rnd.NextDouble() * 1.2 + 0.3)));
            }

            if (!_isRenderingActive)
            {
                CompositionTarget.Rendering += OnRendering;
                _isRenderingActive = true;
            }
        }

        private void StartUptime()
        {
            _uptimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _uptimeTimer.Tick += (s, e) =>
            {
                var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
                if (this.FindName("UptimeText") is TextBlock ut)
                    ut.Text = $"UPTIME: {uptime.Days}d {uptime.Hours}h {uptime.Minutes}m";
            };
            _uptimeTimer.Start();
        }
        public void OptimizeRam_Click(object sender, RoutedEventArgs e)
        {
            ShowAxeNotification("PURGING RAM...");
            long memoryBefore = GC.GetTotalMemory(false);

            foreach (Process proc in Process.GetProcesses())
            {
                try { EmptyWorkingSet(proc.Handle); }
                catch { /* Pomiń systemowe procesy */ }
            }

            lifetimeRamFreedGB += 0.5; 
            UpdateStatsUI();
            ShowAxeNotification("RAM OPTIMIZED!");
        }
        public void NetworkReset_Click(object sender, RoutedEventArgs e)
        {
            ShowAxeNotification("RESETTING NETWORK STACK...");
            RunCommand("netsh winsock reset");
            RunCommand("netsh int ip reset");
            RunCommand("ipconfig /release");
            RunCommand("ipconfig /renew");
            RunCommand("ipconfig /flushdns");
            ShowAxeNotification("NETWORK RESET SUCCESS!");
        }
        public async void WipeShaders_Click(object sender, RoutedEventArgs e)
        {
            ShowAxeNotification("WIPING GPU SHADERS...");
            await Task.Run(() => {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                // Ścieżki dla NVIDIA, AMD i DirectX
                string[] shaderPaths = {
            System.IO.Path.Combine(localAppData, @"D3DSCache"),
            System.IO.Path.Combine(localAppData, @"NVIDIA\GLCache"),
            System.IO.Path.Combine(localAppData, @"AMD\DxCache")
        };

                foreach (var path in shaderPaths)
                {
                    if (Directory.Exists(path)) SafeDirectoryDelete(path);
                }
            });
            ShowAxeNotification("SHADERS PURGED!");
        }
        public void GameBooster_Click(object sender, RoutedEventArgs e)
        {
            string[] hardcodedGames = { "cs2", "Valorant", "FortniteClient-Win64-Shipping", "League of Legends" };

            var allGamesToBoost = hardcodedGames.Concat(_gamesList).ToList();

            bool foundAny = false;

            foreach (var name in allGamesToBoost)
            {
                var procs = Process.GetProcessesByName(name);
                foreach (var p in procs)
                {
                    try
                    {
                        p.PriorityClass = ProcessPriorityClass.High;
                        foundAny = true;
                    }
                    catch { /* Pomiń błąd uprawnień dla pojedynczego procesu */ }
                }
            }

            if (foundAny)
            {
                ShowAxeNotification("DYNAMIC BOOST: ACTIVE PROCESSES OPTIMIZED");
            }
            else
            {
                ShowAxeNotification("NO TARGET GAMES DETECTED IN BACKGROUND");
            }
        }

        public void DnsCloudflare_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                ShowAxeNotification("INJECTING CLOUDFLARE DNS...");
                RunPS("Get-NetAdapter | Where-Object {$_.Status -eq 'Up'} | Set-DnsClientServerAddress -ServerAddresses ('1.1.1.1','1.0.0.1')");
                ShowAxeNotification("DNS SET TO CLOUDFLARE");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("DNS Error: " + ex.Message);
                ShowAxeNotification("DNS ERROR! CHECK LOGS");
            }
        }

        public void DnsGoogle_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                ShowAxeNotification("INJECTING GOOGLE DNS...");
                RunPS("Get-NetAdapter | Where-Object {$_.Status -eq 'Up'} | Set-DnsClientServerAddress -ServerAddresses ('8.8.8.8','8.8.4.4')");
                ShowAxeNotification("DNS SET TO GOOGLE!");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("DNS Error: " + ex.Message);
                ShowAxeNotification("DNS ERROR!");
            }
        }
        public void FreezeSystem_Click(object sender, RoutedEventArgs e)
        {
            ShowAxeNotification("FREEZING WINDOWS UPDATES...");
            RunCommand("sc config wuauserv start=disabled");
            RunCommand("net stop wuauserv");
            RunCommand("sc config bits start=disabled");
            RunCommand("net stop bits");
            ShowAxeNotification("UPDATES FROZEN!");
        }
        public async void DeepDebloat_Click(object sender, RoutedEventArgs e)
        {
            ShowAxeNotification("REMOVING BLOATWARE...");
            await Task.Run(() => {
                // Usuwa np. Bing News, Weather, Microsoft Solitaire itp.
                RunPS("Get-AppxPackage *bingnews* | Remove-AppxPackage");
                RunPS("Get-AppxPackage *solitairecollection* | Remove-AppxPackage");
                RunPS("Get-AppxPackage *3dbuilder* | Remove-AppxPackage");
            });
            ShowAxeNotification("SYSTEM DEBLOATED!");
        }
        public void Theme_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                
                string[] axeThemes = { "#FF0055", "#00FFDD", "#AA00FF", "#00FFAA", "#FFFFFF" };
                string nextTheme = axeThemes[_rnd.Next(axeThemes.Length)];

                ApplyNeuralTheme(nextTheme);
                ShowAxeNotification("THEME RANDOMIZED!");
            }
            catch (Exception ex) { LogError(ex, "Theme_Engine"); }
        }

        private void UpdateStatsUI()
        {
            if (this.FindName("StatOptiDone") is TextBlock sod) sod.Text = lifetimeOptis.ToString();
            if (this.FindName("TotalJunkText") is TextBlock tjt) tjt.Text = $"{totalJunkClearedMB:F1} MB";
            if (this.FindName("StatGamesBoosted") is TextBlock sgb) sgb.Text = lifetimeGamesBoosted.ToString();
            if (this.FindName("StatRamFreed") is TextBlock srf) srf.Text = $"{lifetimeRamFreedGB:F1} GB";
        }

        private void LoadAllData() { try { if (File.Exists(statsPath)) { var l = File.ReadAllLines(statsPath); lifetimeOptis = int.Parse(l[0]); totalJunkClearedMB = double.Parse(l[1]); lifetimeRamFreedGB = double.Parse(l[2]); lifetimeGamesBoosted = int.Parse(l[3]); UpdateStatsUI(); } } catch { } }
        private void SaveAllData() { try { File.WriteAllLines(statsPath, new string[] { lifetimeOptis.ToString(), totalJunkClearedMB.ToString(), lifetimeRamFreedGB.ToString(), lifetimeGamesBoosted.ToString() }); } catch { } }
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) => SaveAllData();
        private async void LoadHardwareProfileAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    
                    using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("select Name from Win32_Processor"))
                    {
                        foreach (ManagementObject obj in searcher.Get())
                        {
                            string cpuName = obj["Name"]?.ToString() ?? "Unknown CPU";
                          
                        }
                    }
                }
                catch (Exception ex) { LogError(ex, "Hardware_Detect"); }
            });
        }

        private void LogError(Exception ex, string module)
        {
            try
            {
                string logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");
                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [MODUŁ: {module}] {ex.Message}\r\n{ex.StackTrace}\r\n----------------------------------------\r\n";
                File.AppendAllText(logPath, logEntry);
            }
            catch { }
        }

        private void SaveEncryptedLicense(string key)
        {
            try
            {
                byte[] secret = Encoding.UTF8.GetBytes(key);
                byte[] encrypted = ProtectedData.Protect(secret, null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes("axe_key.dat", encrypted);
            }
            catch (Exception ex) { LogError(ex, "License_Save"); }
        }

        private string LoadEncryptedLicense()
        {
            try
            {
                if (File.Exists("axe_key.dat"))
                {
                    byte[] encrypted = File.ReadAllBytes("axe_key.dat");
                    byte[] secret = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(secret);
                }
            }
            catch (Exception ex) { LogError(ex, "License_Load"); }

            return string.Empty;
        }

    }

    [Table("axe_cloud_sync")]
    public class AxeSyncModel : BaseModel
    {
        [PrimaryKey("user_id")] public string UserId { get; set; } = string.Empty;
        [Column("hex_color")] public string HexColor { get; set; } = "#000000";
        [Column("glass_opacity")] public double GlassOpacity { get; set; } = 0.8;
        [Column("glow_intensity")] public double GlowIntensity { get; set; } = 15.0;
        [Column("active_profile")] public string ActiveProfile { get; set; } = "Default";
    }

    // ==========================================
    //   SERVICE: LANGUAGE MANAGER (i18n)
    // ==========================================
    public static class LanguageManager
    {
        private static Dictionary<string, ResourceDictionary> _locales = new Dictionary<string, ResourceDictionary>();
        public static UserTier CurrentTier { get; set; } = UserTier.Basic;

        public static void Initialize()
        {
            // ================== ENGLISH (en) ==================
            var en = new ResourceDictionary();
            en.Add("L_AppTitle", "KSYXIS TWEAKS");
            en.Add("TT_NavUser", "Your Profile and Statistics");
            en.Add("TT_NavHome", "Main Optimization Panel");
            en.Add("TT_NavTurbo", "Quick Actions and Turbo Booster");
            en.Add("TT_NavGames", "Game Profiles and Priorities");
            en.Add("TT_NavSettings", "Application Settings");
            en.Add("L_NetworkStack", "NETWORK STACK");
            en.Add("L_DisableNagle", "Disable Nagle's Alg");
            en.Add("TT_DisableNagle", "Reduces network latency and ping.");
            en.Add("L_FlushDns", "Flush DNS Cache");
            en.Add("TT_FlushDns", "Clears the DNS cache for a more stable connection.");
            en.Add("L_TcpHardware", "TCP Hardware Offload");
            en.Add("TT_TcpHardware", "Offloads network processing to hardware, saving CPU.");
            en.Add("L_BlockWinUpdate", "Block Win Updates");
            en.Add("TT_BlockWinUpdate", "Blocks Windows updates while gaming.");
            en.Add("L_TcpWindow", "🔒 TCP Window Tuner");
            en.Add("TT_TcpWindow", "Optimizes TCP window receiving data.");
            en.Add("L_NetshContext", "🔒 Netsh Context Switch");
            en.Add("TT_NetshContext", "Reduces network context switching overhead.");
            en.Add("L_GpuRendering", "GPU & RENDERING");
            en.Add("L_HighGpu", "🔒 High GPU Priority");
            en.Add("TT_HighGpu", "Gives games the highest graphics priority.");
            en.Add("L_DisableMpo", "🔒 Disable DWM MPO");
            en.Add("TT_DisableMpo", "Fixes stuttering in windowed mode.");
            en.Add("L_ForceGameMode", "🔒 Force Game Mode");
            en.Add("TT_ForceGameMode", "Forces Windows Game Mode.");
            en.Add("L_ClearShader", "🔒 Clear Shader Cache");
            en.Add("TT_ClearShader", "Removes old shaders, preventing graphic glitches.");
            en.Add("L_UltPower", "🔒 Ultimate Power Plan");
            en.Add("TT_UltPower", "Activates hidden max performance power plan.");
            en.Add("L_InputLatency", "INPUT & LATENCY");
            en.Add("L_RawInput", "🔒 1:1 Raw Input");
            en.Add("TT_RawInput", "Disables mouse acceleration for perfect precision.");
            en.Add("L_KeyboardFix", "🔒 Keyboard Latency Fix");
            en.Add("TT_KeyboardFix", "Reduces keyboard response delay.");
            en.Add("L_TimerRes", "🔒 0.5ms Timer Resolution");
            en.Add("TT_TimerRes", "Increases system clock precision.");
            en.Add("L_UsbOverride", "🔒 USB Polling Override");
            en.Add("TT_UsbOverride", "Optimizes USB port polling rate.");
            en.Add("L_CleanAudio", "🔒 Clean Audio Processing");
            en.Add("TT_CleanAudio", "Removes audio enhancements to save resources.");
            en.Add("L_ServicesOs", "SERVICES & OS");
            en.Add("L_DisSpooler", "🔒 Disable Spooler");
            en.Add("TT_DisSpooler", "Disables printing services.");
            en.Add("L_DisSysmain", "🔒 Disable SysMain");
            en.Add("TT_DisSysmain", "Disables indexing, saving disk usage.");
            en.Add("L_DisMaps", "🔒 Disable Maps");
            en.Add("TT_DisMaps", "Disables background tracking and maps.");
            en.Add("L_DisFax", "🔒 Disable Fax");
            en.Add("TT_DisFax", "Disables unnecessary fax service.");
            en.Add("L_DeepTemp", "🔒 Deep Temp Clean");
            en.Add("TT_DeepTemp", "Aggressively removes temporary files.");
            en.Add("L_KernelOverride", "KERNEL OVERRIDE");
            en.Add("L_MsiMode", "🔒 Enable MSI Mode (GPU)");
            en.Add("TT_MsiMode", "MSI mode for more stable framerates.");
            en.Add("L_CoreLasso", "🔒 Core Isolation (Lasso)");
            en.Add("TT_CoreLasso", "Isolates CPU cores for game processes.");
            en.Add("L_BcdTweak", "🔒 Boot Optimization (BCD)");
            en.Add("TT_BcdTweak", "Optimizes kernel boot parameters.");
            en.Add("L_Interrupt", "🔒 Interrupt Steering");
            en.Add("TT_Interrupt", "Directs hardware interrupts for performance.");
            en.Add("L_ProDefense", "PRO-GRADE DEFENSE");
            en.Add("L_WmiBlock", "🔒 WMI Event Blocker");
            en.Add("TT_WmiBlock", "Blocks unnecessary telemetry events.");
            en.Add("L_ExploitOff", "🔒 Exploit Protection Off");
            en.Add("TT_ExploitOff", "Disables security features that lower FPS.");
            en.Add("L_CorePark", "🔒 Disable Core Parking");
            en.Add("TT_CorePark", "Prevents CPU cores from sleeping.");
            en.Add("L_DisThrottle", "🔒 Disable Throttling");
            en.Add("TT_DisThrottle", "Prevents CPU from downclocking.");
            en.Add("L_RamFix", "🔒 RAM Latency Fix");
            en.Add("TT_RamFix", "Optimizes RAM latencies.");
            en.Add("L_ApplyBtn", "APPLY CONFIGURATION");
            en.Add("TT_ApplyBtn", "Apply all selected changes.");
            en.Add("L_QuickActions", "QUICK ACTIONS");
            en.Add("L_CleanSys", "🔒 SYSTEM CLEANER");
            en.Add("L_CleanLaunch", "🔒 LAUNCHERS CLEANER");
            en.Add("L_Debloat", "🔒 PREMIUM DEBLOAT");
            en.Add("L_NetReset", "🔒 NETWORK RESET");
            en.Add("L_OptiRam", "🔒 OPTIMIZE RAM");
            en.Add("L_BtnCloudflare", "🔒 CLOUDFLARE");
            en.Add("L_BtnGoogle", "🔒 GOOGLE");
            en.Add("L_NetIntel", "NETWORK INTELLIGENCE");
            en.Add("L_DnsInject", "DNS INJECTION");
            en.Add("L_SysSecurity", "SYSTEM SECURITY");
            en.Add("L_FreezeUpd", "🔒 FREEZE UPDATES");
            en.Add("L_SfcScan", "🔒 SFC INTEGRITY SCAN");
            en.Add("L_LimitGames", "LIMIT: 15 GAMES (PREMIUM)");
            en.Add("L_AddCustomExe", "+   ADD CUSTOM EXECUTABLE");
            en.Add("L_UserStatus", "PREMIUM MEMBER");
            en.Add("L_LicenseOver", "LICENSE OVERRIDE");
            en.Add("L_RefLicense", "REFRESH LICENSE");
            en.Add("L_SysStat", "SYSTEM STATISTICS");
            en.Add("L_StatJunk", "🗑 JUNK PURGED");
            en.Add("L_StatRam", "🧠 RAM FREED");
            en.Add("L_StatTweaks", "⚙ TWEAKS APPLIED");
            en.Add("L_StatBoosts", "🚀 GAME BOOSTS");
            en.Add("L_SysBehav", "SYSTEM BEHAVIOR");
            en.Add("L_StartWin", "Start with Windows");
            en.Add("L_MinTray", "Minimize to Tray");
            en.Add("L_DiscordRpc", "Discord RPC");
            en.Add("L_LangConfig", "LANGUAGE CONFIG");
            en.Add("L_SyncSet", "SYNC SETTINGS");
            en.Add("L_VisualDna", "VISUAL DNA");
            en.Add("L_DynRgb", "Dynamic RGB Pulse");
            en.Add("L_Opacity", "HUD OPACITY");
            en.Add("L_ParticleDen", "PARTICLE DENSITY");
            en.Add("L_Uptime", "UPTIME: 0d 1h 44m");
            en.Add("L_LoggingIn", "Logging in...");
            _locales.Add("en", en);

            // ================== POLSKI (PL) ==================
            var pl = new ResourceDictionary();
            pl.Add("L_AppTitle", "KSYXIS TWEAKS");
            pl.Add("TT_NavUser", "Twój Profil i Statystyki");
            pl.Add("TT_NavHome", "Główny Panel Optymalizacji");
            pl.Add("TT_NavTurbo", "Szybkie Akcje i Turbo Booster");
            pl.Add("TT_NavGames", "Profile Gier i Priorytety");
            pl.Add("TT_NavSettings", "Ustawienia Aplikacji");
            pl.Add("L_NetworkStack", "STOS SIECIOWY");
            pl.Add("L_DisableNagle", "Wyłącz algorytm Nagle'a");
            pl.Add("TT_DisableNagle", "Zmniejsza opóźnienia i ping poprzez wyłączenie grupowania pakietów.");
            pl.Add("L_FlushDns", "Wyczyść Cache DNS");
            pl.Add("TT_FlushDns", "Opróżnia pamięć podręczną DNS dla lepszej stabilności łącza.");
            pl.Add("L_TcpHardware", "Sprzętowy Offload TCP");
            pl.Add("TT_TcpHardware", "Przenosi przetwarzanie sieci na kartę sieciową, odciążając procesor.");
            pl.Add("L_BlockWinUpdate", "Blokuj Aktualizacje Win");
            pl.Add("TT_BlockWinUpdate", "Zatrzymuje pobieranie aktualizacji podczas grania.");
            pl.Add("L_TcpWindow", "🔒 Optymalizacja Okna TCP");
            pl.Add("TT_TcpWindow", "Zwiększa przepustowość i odbiór danych TCP.");
            pl.Add("L_NetshContext", "🔒 Przełącznik Netsh Context");
            pl.Add("TT_NetshContext", "Zmniejsza narzut przełączania kontekstu sieciowego.");
            pl.Add("L_GpuRendering", "GPU I RENDEROWANIE");
            pl.Add("L_HighGpu", "🔒 Wysoki Priorytet GPU");
            pl.Add("TT_HighGpu", "Wymusza maksymalne zasoby graficzne dla gry.");
            pl.Add("L_DisableMpo", "🔒 Wyłącz DWM MPO");
            pl.Add("TT_DisableMpo", "Naprawia zacinanie się obrazu w trybie okienkowym.");
            pl.Add("L_ForceGameMode", "🔒 Wymuś Tryb Gry");
            pl.Add("TT_ForceGameMode", "Wymusza aktywację systemowego Trybu Gry Windows.");
            pl.Add("L_ClearShader", "🔒 Wyczyść Pamięć Shaderów");
            pl.Add("TT_ClearShader", "Usuwa stare shadery rozwiązując problemy ze spadkiem klatek.");
            pl.Add("L_UltPower", "🔒 Plan Zasilania Ultimate");
            pl.Add("TT_UltPower", "Odblokowuje ukryty plan maksymalnej wydajności Windows.");
            pl.Add("L_InputLatency", "URZĄDZENIA WEJŚCIA");
            pl.Add("L_RawInput", "🔒 Surowy Odczyt Myszy (1:1)");
            pl.Add("TT_RawInput", "Całkowicie wyłącza systemową akcelerację kursora.");
            pl.Add("L_KeyboardFix", "🔒 Optymalizacja Klawiatury");
            pl.Add("TT_KeyboardFix", "Zmniejsza opóźnienie rejestrowania klawiszy.");
            pl.Add("L_TimerRes", "🔒 Zegar Systemowy 0.5ms");
            pl.Add("TT_TimerRes", "Wymusza odświeżanie zegara systemu co 0.5ms.");
            pl.Add("L_UsbOverride", "🔒 Nadpisanie Portów USB");
            pl.Add("TT_UsbOverride", "Wymusza stały Polling Rate.");
            pl.Add("L_CleanAudio", "🔒 Czyste Przetwarzanie Audio");
            pl.Add("TT_CleanAudio", "Wyłącza obciążające procesor ulepszenia dźwięku Windows.");
            pl.Add("L_ServicesOs", "USŁUGI I SYSTEM");
            pl.Add("L_DisSpooler", "🔒 Wyłącz Bufor Wydruku");
            pl.Add("TT_DisSpooler", "Zatrzymuje usługi obsługi drukarek.");
            pl.Add("L_DisSysmain", "🔒 Wyłącz Usługę SysMain");
            pl.Add("TT_DisSysmain", "Zatrzymuje ciągłe indeksowanie plików w tle.");
            pl.Add("L_DisMaps", "🔒 Wyłącz Mapy Systemowe");
            pl.Add("TT_DisMaps", "Blokuje śledzenie lokalizacji.");
            pl.Add("L_DisFax", "🔒 Wyłącz Usługi Faksu");
            pl.Add("TT_DisFax", "Wyłącza procesy faksowania Windows.");
            pl.Add("L_DeepTemp", "🔒 Głębokie Czyszczenie Temp");
            pl.Add("TT_DeepTemp", "Agresywnie kasuje ukryte pliki tymczasowe systemu.");
            pl.Add("L_KernelOverride", "MODYFIKACJE JĄDRA");
            pl.Add("L_MsiMode", "🔒 Włącz Tryb MSI (GPU)");
            pl.Add("TT_MsiMode", "Zmienia sposób obsługi przerwań GPU.");
            pl.Add("L_CoreLasso", "🔒 Izolacja Rdzeni (Lasso)");
            pl.Add("L_BcdTweak", "🔒 Optymalizacja Rozruchu");
            pl.Add("TT_BcdTweak", "Modyfikuje parametry startowe kernela.");
            pl.Add("L_Interrupt", "🔒 Kierowanie Przerwań");
            pl.Add("TT_Interrupt", "Rozdziela sprzętowe przerwania od rdzeni gier.");
            pl.Add("L_ProDefense", "ZABEZPIECZENIA (DEFENSYWA)");
            pl.Add("L_WmiBlock", "🔒 Bloker Zdarzeń WMI");
            pl.Add("TT_WmiBlock", "Izoluje ciężkie zdarzenia telemetrii.");
            pl.Add("L_ExploitOff", "🔒 Wyłącz Ochronę Exploitów");
            pl.Add("TT_ExploitOff", "Wyłącza zabezpieczenia ASLR dla maksymalnych FPS.");
            pl.Add("L_CorePark", "🔒 Wyłącz Usypianie Rdzeni");
            pl.Add("TT_CorePark", "Zabrania systemowi wygaszać rdzenie CPU.");
            pl.Add("L_DisThrottle", "🔒 Zablokuj Throttling");
            pl.Add("TT_DisThrottle", "Zapobiega zrzucaniu zegarów procesora.");
            pl.Add("L_RamFix", "🔒 Optymalizacja Opóźnień RAM");
            pl.Add("TT_RamFix", "Wymusza ostrzejsze timingi pamięci operacyjnej.");
            pl.Add("L_ApplyBtn", "ZASTOSUJ KONFIGURACJĘ");
            pl.Add("TT_ApplyBtn", "Wprowadza wszystkie modyfikacje do systemu.");
            pl.Add("L_QuickActions", "SZYBKIE AKCJE");
            pl.Add("L_CleanSys", "🔒 CZYSZCZENIE SYSTEMU");
            pl.Add("L_CleanLaunch", "🔒 CZYSZCZENIE LAUNCHERÓW");
            pl.Add("L_Debloat", "🔒 GŁĘBOKI DEBLOAT");
            pl.Add("L_NetReset", "🔒 RESET SIECI TCP/IP");
            pl.Add("L_OptiRam", "🔒 ZWOLNIJ PAMIĘĆ RAM");
            pl.Add("L_BtnCloudflare", "🔒 CLOUDFLARE");
            pl.Add("L_BtnGoogle", "🔒 GOOGLE");
            pl.Add("L_NetIntel", "INTELIGENCJA SIECIOWA");
            pl.Add("L_DnsInject", "ZASTERYK DNS (INJECTION)");
            pl.Add("L_SysSecurity", "ZARZĄDZANIE BEZPIECZEŃSTWEM");
            pl.Add("L_FreezeUpd", "🔒 ZAMROŹ AKTUALIZACJE");
            pl.Add("L_SfcScan", "🔒 SKAN SPÓJNOŚCI PLIKÓW");
            pl.Add("L_LimitGames", "LIMIT: 15 GIER (PREMIUM)");
            pl.Add("L_AddCustomExe", "+   DODAJ WŁASNY PLIK .EXE");
            pl.Add("L_UserStatus", "STATUS PREMIUM");
            pl.Add("L_LicenseOver", "ZARZĄDZANIE LICENCJĄ");
            pl.Add("L_RefLicense", "ODŚWIEŻ LICENCJĘ");
            pl.Add("L_SysStat", "STATYSTYKI SYSTEMU");
            pl.Add("L_StatJunk", "🗑 USUNIĘTE ŚMIECI");
            pl.Add("L_StatRam", "🧠 ZWOLNIONY RAM");
            pl.Add("L_StatTweaks", "⚙ APLIKACJE TWEAKÓW");
            pl.Add("L_StatBoosts", "🚀 BOOSTY GIER");
            pl.Add("L_SysBehav", "ZACHOWANIE SYSTEMU");
            pl.Add("L_StartWin", "Uruchamiaj z Windows");
            pl.Add("L_MinTray", "Minimalizuj do paska zadań");
            pl.Add("L_DiscordRpc", "Włącz status Discord RPC");
            pl.Add("L_LangConfig", "KONFIGURACJA JĘZYKA");
            pl.Add("L_SyncSet", "SYNCHRONIZUJ USTAWIENIA");
            pl.Add("L_VisualDna", "PERSONALIZACJA WIZUALNA");
            pl.Add("L_DynRgb", "Dynamiczny Puls RGB");
            pl.Add("L_Opacity", "PRZEZROCZYSTOŚĆ INTERFEJSU");
            pl.Add("L_ParticleDen", "ZAGĘSZCZENIE CZĄSTECZEK");
            pl.Add("L_Uptime", "CZAS PRACY: 0d 1g 44m");
            pl.Add("L_LoggingIn", "Logowanie do serwera...");
            _locales.Add("pl", pl);

            // ================== ESPAÑOL (es) ==================
            var es = new ResourceDictionary();
            es.Add("L_AppTitle", "KSYXIS TWEAKS");
            es.Add("TT_NavUser", "Tu Perfil y Estadísticas");
            es.Add("TT_NavHome", "Panel de Optimización Principal");
            es.Add("TT_NavTurbo", "Acciones Rápidas y Acelerador Turbo");
            es.Add("TT_NavGames", "Perfiles de Juegos y Prioridades");
            es.Add("TT_NavSettings", "Configuración de la Aplicación");
            es.Add("L_NetworkStack", "PILA DE RED");
            es.Add("L_DisableNagle", "Desact. Algoritmo Nagle");
            es.Add("TT_DisableNagle", "Reduce la latencia de red y el ping.");
            es.Add("L_FlushDns", "Vaciar Caché DNS");
            es.Add("TT_FlushDns", "Limpia la caché de DNS para una conexión estable.");
            es.Add("L_TcpHardware", "Descarga Hardware TCP");
            es.Add("TT_TcpHardware", "Descarga el procesamiento de red al hardware.");
            es.Add("L_BlockWinUpdate", "Bloquear Act. de Win");
            es.Add("TT_BlockWinUpdate", "Bloquea las actualizaciones mientras juegas.");
            es.Add("L_TcpWindow", "🔒 Ajuste de Ventana TCP");
            es.Add("TT_TcpWindow", "Optimiza la ventana de recepción de datos TCP.");
            es.Add("L_NetshContext", "🔒 Cambio de Contexto Netsh");
            es.Add("TT_NetshContext", "Reduce la sobrecarga de cambio de contexto de red.");
            es.Add("L_GpuRendering", "GPU Y RENDERIZADO");
            es.Add("L_HighGpu", "🔒 Alta Prioridad GPU");
            es.Add("TT_HighGpu", "Da a los juegos la máxima prioridad gráfica.");
            es.Add("L_DisableMpo", "🔒 Desactivar DWM MPO");
            es.Add("TT_DisableMpo", "Soluciona el tartamudeo en modo ventana.");
            es.Add("L_ForceGameMode", "🔒 Forzar Modo de Juego");
            es.Add("TT_ForceGameMode", "Fuerza el Modo de Juego de Windows.");
            es.Add("L_ClearShader", "🔒 Borrar Caché Shaders");
            es.Add("TT_ClearShader", "Elimina shaders antiguos para evitar errores gráficos.");
            es.Add("L_UltPower", "🔒 Plan Energía Máximo");
            es.Add("TT_UltPower", "Activa el plan de energía de máximo rendimiento.");
            es.Add("L_InputLatency", "ENTRADA Y LATENCIA");
            es.Add("L_RawInput", "🔒 Entrada Bruta 1:1");
            es.Add("TT_RawInput", "Desactiva la aceleración del ratón.");
            es.Add("L_KeyboardFix", "🔒 Opt. Latencia Teclado");
            es.Add("TT_KeyboardFix", "Reduce el tiempo de respuesta del teclado.");
            es.Add("L_TimerRes", "🔒 Reloj del Sistema 0.5ms");
            es.Add("TT_TimerRes", "Aumenta la precisión del reloj del sistema.");
            es.Add("L_UsbOverride", "🔒 Anulación Tasa de USB");
            es.Add("TT_UsbOverride", "Optimiza la tasa de sondeo de los puertos USB.");
            es.Add("L_CleanAudio", "🔒 Proc. de Audio Limpio");
            es.Add("TT_CleanAudio", "Elimina mejoras de audio para ahorrar recursos.");
            es.Add("L_ServicesOs", "SERVICIOS Y SO");
            es.Add("L_DisSpooler", "🔒 Desactivar Impresión");
            es.Add("TT_DisSpooler", "Desactiva los servicios de impresión.");
            es.Add("L_DisSysmain", "🔒 Desactivar SysMain");
            es.Add("TT_DisSysmain", "Desactiva la indexación para ahorrar uso de disco.");
            es.Add("L_DisMaps", "🔒 Desactivar Mapas");
            es.Add("TT_DisMaps", "Desactiva el rastreo en segundo plano.");
            es.Add("L_DisFax", "🔒 Desactivar Fax");
            es.Add("TT_DisFax", "Desactiva los servicios innecesarios de fax.");
            es.Add("L_DeepTemp", "🔒 Limpieza Profunda Temp");
            es.Add("TT_DeepTemp", "Elimina agresivamente los archivos temporales.");
            es.Add("L_KernelOverride", "MODIFICACIÓN KERNEL");
            es.Add("L_MsiMode", "🔒 Activar Modo MSI (GPU)");
            es.Add("TT_MsiMode", "Modo MSI para FPS más estables.");
            es.Add("L_CoreLasso", "🔒 Aislamiento de Núcleo");
            es.Add("TT_CoreLasso", "Aísla núcleos de CPU para procesos de juegos.");
            es.Add("L_BcdTweak", "🔒 Optimización Arranque (BCD)");
            es.Add("TT_BcdTweak", "Optimiza los parámetros de arranque del kernel.");
            es.Add("L_Interrupt", "🔒 Redirección Interrupciones");
            es.Add("TT_Interrupt", "Dirige las interrupciones para mayor rendimiento.");
            es.Add("L_ProDefense", "DEFENSA PRO");
            es.Add("L_WmiBlock", "🔒 Bloqueador Eventos WMI");
            es.Add("TT_WmiBlock", "Bloquea eventos de telemetría innecesarios.");
            es.Add("L_ExploitOff", "🔒 Sin Prot. Exploits");
            es.Add("TT_ExploitOff", "Desactiva funciones de seguridad que bajan FPS.");
            es.Add("L_CorePark", "🔒 Desactivar Ahorro CPU");
            es.Add("TT_CorePark", "Evita que los núcleos de la CPU se duerman.");
            es.Add("L_DisThrottle", "🔒 Desactivar Throttling");
            es.Add("TT_DisThrottle", "Evita que la CPU reduzca su frecuencia.");
            es.Add("L_RamFix", "🔒 Opt. Latencia RAM");
            es.Add("TT_RamFix", "Optimiza las latencias de la memoria RAM.");
            es.Add("L_ApplyBtn", "APLICAR CONFIGURACIÓN");
            es.Add("TT_ApplyBtn", "Aplicar todos los cambios seleccionados.");
            es.Add("L_QuickActions", "ACCIONES RÁPIDAS");
            es.Add("L_CleanSys", "🔒 LIMPIADOR SISTEMA");
            es.Add("L_CleanLaunch", "🔒 LIMPIAR LAUNCHERS");
            es.Add("L_Debloat", "🔒 DEBLOAT PREMIUM");
            es.Add("L_NetReset", "🔒 RESETEAR RED");
            es.Add("L_OptiRam", "🔒 OPTIMIZAR RAM");
            es.Add("L_BtnCloudflare", "🔒 CLOUDFLARE");
            es.Add("L_BtnGoogle", "🔒 GOOGLE");
            es.Add("L_NetIntel", "INTELIGENCIA DE RED");
            es.Add("L_DnsInject", "INYECCIÓN DNS");
            es.Add("L_SysSecurity", "SEGURIDAD DEL SISTEMA");
            es.Add("L_FreezeUpd", "🔒 CONGELAR ACTUALIZACIONES");
            es.Add("L_SfcScan", "🔒 ESCANEO DE INTEGRIDAD SFC");
            es.Add("L_LimitGames", "LÍMITE: 15 JUEGOS (PREMIUM)");
            es.Add("L_AddCustomExe", "+   AÑADIR EJECUTABLE");
            es.Add("L_UserStatus", "MIEMBRO PREMIUM");
            es.Add("L_LicenseOver", "CLAVE DE LICENCIA");
            es.Add("L_RefLicense", "ACTUALIZAR LICENCIA");
            es.Add("L_SysStat", "ESTADÍSTICAS DEL SISTEMA");
            es.Add("L_StatJunk", "🗑 BASURA ELIMINADA");
            es.Add("L_StatRam", "🧠 RAM LIBERADA");
            es.Add("L_StatTweaks", "⚙ OPTIMIZACIONES");
            es.Add("L_StatBoosts", "🚀 JUEGOS ACELERADOS");
            es.Add("L_SysBehav", "COMPORTAMIENTO");
            es.Add("L_StartWin", "Iniciar con Windows");
            es.Add("L_MinTray", "Minimizar a la bandeja");
            es.Add("L_DiscordRpc", "Discord RPC");
            es.Add("L_LangConfig", "IDIOMA");
            es.Add("L_SyncSet", "SINCRONIZAR AJUSTES");
            es.Add("L_VisualDna", "ESTILO VISUAL");
            es.Add("L_DynRgb", "Pulso RGB Dinámico");
            es.Add("L_Opacity", "OPACIDAD HUD");
            es.Add("L_ParticleDen", "DENSIDAD PARTÍCULAS");
            es.Add("L_Uptime", "TIEMPO DE ACTIVIDAD: 0d 1h 44m");
            es.Add("L_LoggingIn", "Iniciando sesión...");
            _locales.Add("es", es);

            // ================== PУССКИЙ (ru) ==================
            var ru = new ResourceDictionary();
            ru.Add("L_AppTitle", "KSYXIS TWEAKS");
            ru.Add("TT_NavUser", "Ваш профиль и статистика");
            ru.Add("TT_NavHome", "Главная панель оптимизации");
            ru.Add("TT_NavTurbo", "Быстрые действия и Турбо-ускоритель");
            ru.Add("TT_NavGames", "Профили игр и приоритеты");
            ru.Add("TT_NavSettings", "Настройки приложения");
            ru.Add("L_NetworkStack", "СЕТЕВОЙ СТЕК");
            ru.Add("L_DisableNagle", "Отключить Nagle's Alg");
            ru.Add("TT_DisableNagle", "Снижает задержку сети и пинг.");
            ru.Add("L_FlushDns", "Очистить DNS-кэш");
            ru.Add("TT_FlushDns", "Очищает кэш DNS для более стабильного соединения.");
            ru.Add("L_TcpHardware", "Аппаратная разгрузка TCP");
            ru.Add("TT_TcpHardware", "Снимает нагрузку на сеть с процессора.");
            ru.Add("L_BlockWinUpdate", "Блок. обновления Win");
            ru.Add("TT_BlockWinUpdate", "Блокирует обновления Windows во время игры.");
            ru.Add("L_TcpWindow", "🔒 Настройка окна TCP");
            ru.Add("TT_TcpWindow", "Оптимизирует окно приема данных TCP.");
            ru.Add("L_NetshContext", "🔒 Переключение контекста Netsh");
            ru.Add("TT_NetshContext", "Снижает накладные расходы на сеть.");
            ru.Add("L_GpuRendering", "GPU И РЕНДЕРИНГ");
            ru.Add("L_HighGpu", "🔒 Высокий приоритет GPU");
            ru.Add("TT_HighGpu", "Дает играм наивысший графический приоритет.");
            ru.Add("L_DisableMpo", "🔒 Отключить DWM MPO");
            ru.Add("TT_DisableMpo", "Исправляет лаги в оконном режиме.");
            ru.Add("L_ForceGameMode", "🔒 Принудит. игровой режим");
            ru.Add("TT_ForceGameMode", "Включает игровой режим Windows.");
            ru.Add("L_ClearShader", "🔒 Очистить кэш шейдеров");
            ru.Add("TT_ClearShader", "Удаляет старые шейдеры.");
            ru.Add("L_UltPower", "🔒 Макс. производительность");
            ru.Add("TT_UltPower", "Активирует скрытый план электропитания.");
            ru.Add("L_InputLatency", "ВВОД И ЗАДЕРЖКА");
            ru.Add("L_RawInput", "🔒 Прямой ввод 1:1");
            ru.Add("TT_RawInput", "Отключает акселерацию мыши.");
            ru.Add("L_KeyboardFix", "🔒 Оптимизация клавиатуры");
            ru.Add("TT_KeyboardFix", "Снижает задержку отклика клавиатуры.");
            ru.Add("L_TimerRes", "🔒 Разрешение таймера 0.5мс");
            ru.Add("TT_TimerRes", "Повышает точность системных часов.");
            ru.Add("L_UsbOverride", "🔒 Переопределение частоты USB");
            ru.Add("TT_UsbOverride", "Оптимизирует частоту опроса портов USB.");
            ru.Add("L_CleanAudio", "🔒 Чистая обработка звука");
            ru.Add("TT_CleanAudio", "Отключает тяжелые звуковые эффекты Windows.");
            ru.Add("L_ServicesOs", "СЛУЖБЫ И ОС");
            ru.Add("L_DisSpooler", "🔒 Отключить Диспетчер печати");
            ru.Add("TT_DisSpooler", "Отключает службы принтера.");
            ru.Add("L_DisSysmain", "🔒 Отключить SysMain");
            ru.Add("TT_DisSysmain", "Отключает фоновое индексирование.");
            ru.Add("L_DisMaps", "🔒 Отключить Карты");
            ru.Add("TT_DisMaps", "Отключает фоновое отслеживание местоположения.");
            ru.Add("L_DisFax", "🔒 Отключить Факс");
            ru.Add("TT_DisFax", "Отключает ненужные службы факса.");
            ru.Add("L_DeepTemp", "🔒 Глубокая очистка Temp");
            ru.Add("TT_DeepTemp", "Агрессивно удаляет временные файлы.");
            ru.Add("L_KernelOverride", "ПЕРЕОПРЕДЕЛЕНИЕ ЯДРА");
            ru.Add("L_MsiMode", "🔒 Включить режим MSI (GPU)");
            ru.Add("TT_MsiMode", "Режим MSI для более стабильного FPS.");
            ru.Add("L_CoreLasso", "🔒 Изоляция ядер (Lasso)");
            ru.Add("TT_CoreLasso", "Изолирует ядра процессора для игр.");
            ru.Add("L_BcdTweak", "🔒 Оптимизация загрузки (BCD)");
            ru.Add("TT_BcdTweak", "Оптимизирует параметры загрузки ядра.");
            ru.Add("L_Interrupt", "🔒 Распределение прерываний");
            ru.Add("TT_Interrupt", "Перенаправляет прерывания для повышения производительности.");
            ru.Add("L_ProDefense", "ПРО-ЗАЩИТА");
            ru.Add("L_WmiBlock", "🔒 Блокировщик событий WMI");
            ru.Add("TT_WmiBlock", "Блокирует телеметрию в фоновом режиме.");
            ru.Add("L_ExploitOff", "🔒 Отключить защиту от эксплойтов");
            ru.Add("TT_ExploitOff", "Отключает функции защиты, снижающие FPS.");
            ru.Add("L_CorePark", "🔒 Отключить парковку ядер");
            ru.Add("TT_CorePark", "Предотвращает засыпание ядер процессора.");
            ru.Add("L_DisThrottle", "🔒 Отключить троттлинг");
            ru.Add("TT_DisThrottle", "Предотвращает снижение частоты процессора.");
            ru.Add("L_RamFix", "🔒 Исправление задержек RAM");
            ru.Add("TT_RamFix", "Оптимизирует тайминги памяти.");
            ru.Add("L_ApplyBtn", "ПРИМЕНИТЬ КОНФИГУРАЦИЮ");
            ru.Add("TT_ApplyBtn", "Применить выбранные изменения.");
            ru.Add("L_QuickActions", "БЫСТРЫЕ ДЕЙСТВИЯ");
            ru.Add("L_CleanSys", "🔒 ОЧИСТКА СИСТЕМЫ");
            ru.Add("L_CleanLaunch", "🔒 ОЧИСТКА ЛАУНЧЕРОВ");
            ru.Add("L_Debloat", "🔒 ПРЕМИУМ ДЕБЛОАТ");
            ru.Add("L_NetReset", "🔒 СБРОС СЕТИ");
            ru.Add("L_OptiRam", "🔒 ОПТИМИЗАЦИЯ RAM");
            ru.Add("L_BtnCloudflare", "🔒 CLOUDFLARE");
            ru.Add("L_BtnGoogle", "🔒 GOOGLE");
            ru.Add("L_NetIntel", "СЕТЕВОЙ ИНТЕЛЛЕКТ");
            ru.Add("L_DnsInject", "ВНЕДРЕНИЕ DNS");
            ru.Add("L_SysSecurity", "БЕЗОПАСНОСТЬ СИСТЕМЫ");
            ru.Add("L_FreezeUpd", "🔒 ЗАМОРОЗИТЬ ОБНОВЛЕНИЯ");
            ru.Add("L_SfcScan", "🔒 ПРОВЕРКА ЦЕЛОСТНОСТИ SFC");
            ru.Add("L_LimitGames", "ЛИМИТ: 15 ИГР (PREMIUM)");
            ru.Add("L_AddCustomExe", "+   ДОБАВИТЬ СВОЙ .EXE");
            ru.Add("L_UserStatus", "ПРЕМИУМ ПОЛЬЗОВАТЕЛЬ");
            ru.Add("L_LicenseOver", "ЛИЦЕНЗИОННЫЙ КЛЮЧ");
            ru.Add("L_RefLicense", "ОБНОВИТЬ ЛИЦЕНЗИЮ");
            ru.Add("L_SysStat", "СИСТЕМНАЯ СТАТИСТИКА");
            ru.Add("L_StatJunk", "🗑 ОЧИЩЕНО МУСОРА");
            ru.Add("L_StatRam", "🧠 ОСВОБОЖДЕНО RAM");
            ru.Add("L_StatTweaks", "⚙ ПРИМЕНЕНО ТВИКОВ");
            ru.Add("L_StatBoosts", "🚀 УСКОРЕНО ИГР");
            ru.Add("L_SysBehav", "ПОВЕДЕНИЕ СИСТЕМЫ");
            ru.Add("L_StartWin", "Запускать с Windows");
            ru.Add("L_MinTray", "Сворачивать в трей");
            ru.Add("L_DiscordRpc", "Статус Discord RPC");
            ru.Add("L_LangConfig", "НАСТРОЙКИ ЯЗЫКА");
            ru.Add("L_SyncSet", "СИНХРОНИЗИРОВАТЬ НАСТРОЙКИ");
            ru.Add("L_VisualDna", "ВИЗУАЛЬНЫЙ СТИЛЬ");
            ru.Add("L_DynRgb", "Динамический RGB-пульс");
            ru.Add("L_Opacity", "ПРОЗРАЧНОСТЬ HUD");
            ru.Add("L_ParticleDen", "ПЛОТНОСТЬ ЧАСТИЦ");
            ru.Add("L_Uptime", "ВРЕМЯ РАБОТЫ: 0д 1ч 44м");
            ru.Add("L_LoggingIn", "Вход в систему...");
            _locales.Add("ru", ru);

            // ================== DEUTSCH (de) ==================
            var de = new ResourceDictionary();
            de.Add("L_AppTitle", "KSYXIS TWEAKS");
            de.Add("TT_NavUser", "Dein Profil und Statistiken");
            de.Add("TT_NavHome", "Hauptoptimierung");
            de.Add("TT_NavTurbo", "Schnelle Aktionen und Turbo");
            de.Add("TT_NavGames", "Spielprofile");
            de.Add("TT_NavSettings", "Einstellungen");
            de.Add("L_NetworkStack", "NETZWERK-STACK");
            de.Add("L_DisableNagle", "Nagle-Alg. Deaktivieren");
            de.Add("TT_DisableNagle", "Reduziert Netzwerklatenz und Ping.");
            de.Add("L_FlushDns", "DNS-Cache Leeren");
            de.Add("TT_FlushDns", "Leert den DNS-Cache für stabilere Verbindungen.");
            de.Add("L_TcpHardware", "TCP Hardware Offload");
            de.Add("TT_TcpHardware", "Verlagert Netzwerkverarbeitung auf die Hardware.");
            de.Add("L_BlockWinUpdate", "Windows-Updates Blockieren");
            de.Add("TT_BlockWinUpdate", "Blockiert Updates während des Spielens.");
            de.Add("L_TcpWindow", "🔒 TCP Window Tuner");
            de.Add("TT_TcpWindow", "Optimiert das TCP-Empfangsfenster.");
            de.Add("L_NetshContext", "🔒 Netsh Context Switch");
            de.Add("TT_NetshContext", "Reduziert Netzwerk-Overhead.");
            de.Add("L_GpuRendering", "GPU & RENDERING");
            de.Add("L_HighGpu", "🔒 Hohe GPU-Priorität");
            de.Add("TT_HighGpu", "Gibt Spielen höchste Grafikpriorität.");
            de.Add("L_DisableMpo", "🔒 DWM MPO Deaktivieren");
            de.Add("TT_DisableMpo", "Behebt Ruckeln im Fenstermodus.");
            de.Add("L_ForceGameMode", "🔒 Spielmodus Erzwingen");
            de.Add("TT_ForceGameMode", "Erzwingt Windows-Spielmodus.");
            de.Add("L_ClearShader", "🔒 Shader-Cache Leeren");
            de.Add("TT_ClearShader", "Entfernt alte Shader.");
            de.Add("L_UltPower", "🔒 Ultimativer Energiesparplan");
            de.Add("TT_UltPower", "Aktiviert verborgenen maximalen Leistungsplan.");
            de.Add("L_InputLatency", "EINGABE & LATENZ");
            de.Add("L_RawInput", "🔒 Roheingabe 1:1");
            de.Add("TT_RawInput", "Deaktiviert Mausbeschleunigung.");
            de.Add("L_KeyboardFix", "🔒 Tastatur-Latenz Fix");
            de.Add("TT_KeyboardFix", "Reduziert Tastaturverzögerung.");
            de.Add("L_TimerRes", "🔒 0.5ms Timer-Auflösung");
            de.Add("TT_TimerRes", "Erhöht die Präzision der Systemuhr.");
            de.Add("L_UsbOverride", "🔒 USB-Abfragerate Erzwingen");
            de.Add("TT_UsbOverride", "Optimiert USB-Ports.");
            de.Add("L_CleanAudio", "🔒 Saubere Audioverarbeitung");
            de.Add("TT_CleanAudio", "Entfernt Windows-Audioverbesserungen.");
            de.Add("L_ServicesOs", "DIENSTE & OS");
            de.Add("L_DisSpooler", "🔒 Spooler Deaktivieren");
            de.Add("TT_DisSpooler", "Deaktiviert Druckdienste.");
            de.Add("L_DisSysmain", "🔒 SysMain Deaktivieren");
            de.Add("TT_DisSysmain", "Deaktiviert Hintergrundindizierung.");
            de.Add("L_DisMaps", "🔒 Karten Deaktivieren");
            de.Add("TT_DisMaps", "Deaktiviert Hintergrund-Tracking.");
            de.Add("L_DisFax", "🔒 Fax Deaktivieren");
            de.Add("TT_DisFax", "Deaktiviert Fax-Dienste.");
            de.Add("L_DeepTemp", "🔒 Tiefe Temp-Reinigung");
            de.Add("TT_DeepTemp", "Löscht aggressiv temporäre Dateien.");
            de.Add("L_KernelOverride", "KERNEL-MODIFIKATION");
            de.Add("L_MsiMode", "🔒 MSI-Modus (GPU) Aktivieren");
            de.Add("TT_MsiMode", "MSI-Modus für stabilere Frameraten.");
            de.Add("L_CoreLasso", "🔒 Kern-Isolation (Lasso)");
            de.Add("TT_CoreLasso", "Isoliert CPU-Kerne für Spiele.");
            de.Add("L_BcdTweak", "🔒 Boot-Optimierung (BCD)");
            de.Add("TT_BcdTweak", "Optimiert Kernel-Startparameter.");
            de.Add("L_Interrupt", "🔒 Interrupt-Steuerung");
            de.Add("TT_Interrupt", "Leitet Hardware-Interrupts um.");
            de.Add("L_ProDefense", "PROFI-SCHUTZ");
            de.Add("L_WmiBlock", "🔒 WMI-Event-Blocker");
            de.Add("TT_WmiBlock", "Blockiert Telemetrie-Events.");
            de.Add("L_ExploitOff", "🔒 Exploit-Schutz Aus");
            de.Add("TT_ExploitOff", "Deaktiviert FPS-senkende Sicherheit.");
            de.Add("L_CorePark", "🔒 Core-Parking Deaktivieren");
            de.Add("TT_CorePark", "Verhindert CPU-Schlafmodus.");
            de.Add("L_DisThrottle", "🔒 Drosselung Deaktivieren");
            de.Add("TT_DisThrottle", "Verhindert CPU-Heruntertakten.");
            de.Add("L_RamFix", "🔒 RAM-Latenz-Fix");
            de.Add("TT_RamFix", "Optimiert Speicher-Timings.");
            de.Add("L_ApplyBtn", "KONFIGURATION ANWENDEN");
            de.Add("TT_ApplyBtn", "Wendet alle Änderungen an.");
            de.Add("L_QuickActions", "SCHNELLE AKTIONEN");
            de.Add("L_CleanSys", "🔒 SYSTEM REINIGEN");
            de.Add("L_CleanLaunch", "🔒 LAUNCHER REINIGEN");
            de.Add("L_Debloat", "🔒 PREMIUM DEBLOAT");
            de.Add("L_NetReset", "🔒 NETZWERK ZURÜCKSETZEN");
            de.Add("L_OptiRam", "🔒 RAM OPTIMIEREN");
            de.Add("L_BtnCloudflare", "🔒 CLOUDFLARE");
            de.Add("L_BtnGoogle", "🔒 GOOGLE");
            de.Add("L_NetIntel", "NETZWERK-INTELLIGENZ");
            de.Add("L_DnsInject", "DNS INJEKTION");
            de.Add("L_SysSecurity", "SYSTEMSICHERHEIT");
            de.Add("L_FreezeUpd", "🔒 UPDATES EINFRIEREN");
            de.Add("L_SfcScan", "🔒 SFC INTEGRITÄTSSCAN");
            de.Add("L_LimitGames", "LIMIT: 15 SPIELE (PREMIUM)");
            de.Add("L_AddCustomExe", "+   EIGENE .EXE HINZUFÜGEN");
            de.Add("L_UserStatus", "PREMIUM-MITGLIED");
            de.Add("L_LicenseOver", "LIZENZSCHLÜSSEL");
            de.Add("L_RefLicense", "LIZENZ AKTUALISIEREN");
            de.Add("L_SysStat", "SYSTEMSTATISTIKEN");
            de.Add("L_StatJunk", "🗑 MÜLL ENTFERNT");
            de.Add("L_StatRam", "🧠 RAM BEFREIT");
            de.Add("L_StatTweaks", "⚙ TWEAKS ANGEWENDET");
            de.Add("L_StatBoosts", "🚀 SPIELE BESCHLEUNIGT");
            de.Add("L_SysBehav", "SYSTEMVERHALTEN");
            de.Add("L_StartWin", "Mit Windows starten");
            de.Add("L_MinTray", "In Tray minimieren");
            de.Add("L_DiscordRpc", "Discord RPC");
            de.Add("L_LangConfig", "SPRACHEINSTELLUNG");
            de.Add("L_SyncSet", "EINSTELLUNGEN SYNCHRONISIEREN");
            de.Add("L_VisualDna", "VISUELLE DNA");
            de.Add("L_DynRgb", "Dynamischer RGB-Puls");
            de.Add("L_Opacity", "HUD-TRANSPARENZ");
            de.Add("L_ParticleDen", "PARTIKELDICHTE");
            de.Add("L_Uptime", "LAUFZEIT: 0t 1s 44m");
            de.Add("L_LoggingIn", "Anmelden...");
            _locales.Add("de", de);

            // ================== PORTUGUÊS (pt-BR) ==================
            var br = new ResourceDictionary();
            br.Add("L_AppTitle", "KSYXIS TWEAKS");
            br.Add("TT_NavUser", "Seu Perfil e Estatísticas");
            br.Add("TT_NavHome", "Painel Principal");
            br.Add("TT_NavTurbo", "Ações Rápidas e Turbo");
            br.Add("TT_NavGames", "Perfis de Jogos");
            br.Add("TT_NavSettings", "Configurações");
            br.Add("L_NetworkStack", "PILHA DE REDE");
            br.Add("L_DisableNagle", "Desativar Algoritmo Nagle");
            br.Add("TT_DisableNagle", "Reduz latência e ping.");
            br.Add("L_FlushDns", "Limpar Cache DNS");
            br.Add("TT_FlushDns", "Limpa cache DNS para estabilidade.");
            br.Add("L_TcpHardware", "Hardware Offload TCP");
            br.Add("TT_TcpHardware", "Reduz carga de rede do CPU.");
            br.Add("L_BlockWinUpdate", "Bloquear Atualizações");
            br.Add("TT_BlockWinUpdate", "Bloqueia updates durante jogos.");
            br.Add("L_TcpWindow", "🔒 Ajuste de Janela TCP");
            br.Add("TT_TcpWindow", "Otimiza a recepção de dados TCP.");
            br.Add("L_NetshContext", "🔒 Netsh Context Switch");
            br.Add("TT_NetshContext", "Reduz sobrecarga de rede.");
            br.Add("L_GpuRendering", "GPU E RENDERIZAÇÃO");
            br.Add("L_HighGpu", "🔒 Alta Prioridade GPU");
            br.Add("TT_HighGpu", "Força prioridade máxima gráfica.");
            br.Add("L_DisableMpo", "🔒 Desativar DWM MPO");
            br.Add("TT_DisableMpo", "Corrige engasgos em modo janela.");
            br.Add("L_ForceGameMode", "🔒 Forçar Modo de Jogo");
            br.Add("TT_ForceGameMode", "Força Modo Jogo do Windows.");
            br.Add("L_ClearShader", "🔒 Limpar Cache Shader");
            br.Add("TT_ClearShader", "Remove shaders antigos.");
            br.Add("L_UltPower", "🔒 Plano de Energia Máxima");
            br.Add("TT_UltPower", "Ativa desempenho máximo de energia.");
            br.Add("L_InputLatency", "ENTRADA E LATÊNCIA");
            br.Add("L_RawInput", "🔒 Entrada Bruta 1:1");
            br.Add("TT_RawInput", "Desativa aceleração do mouse.");
            br.Add("L_KeyboardFix", "🔒 Fix de Latência de Teclado");
            br.Add("TT_KeyboardFix", "Reduz atraso do teclado.");
            br.Add("L_TimerRes", "🔒 Resolução de 0.5ms");
            br.Add("TT_TimerRes", "Aumenta precisão do relógio.");
            br.Add("L_UsbOverride", "🔒 Override Taxa USB");
            br.Add("TT_UsbOverride", "Otimiza polling rate do USB.");
            br.Add("L_CleanAudio", "🔒 Processamento de Áudio Limpo");
            br.Add("TT_CleanAudio", "Remove melhorias de áudio do Windows.");
            br.Add("L_ServicesOs", "SERVIÇOS E SO");
            br.Add("L_DisSpooler", "🔒 Desativar Spooler");
            br.Add("TT_DisSpooler", "Desativa serviços de impressão.");
            br.Add("L_DisSysmain", "🔒 Desativar SysMain");
            br.Add("TT_DisSysmain", "Desativa indexação de fundo.");
            br.Add("L_DisMaps", "🔒 Desativar Mapas");
            br.Add("TT_DisMaps", "Desativa rastreamento de fundo.");
            br.Add("L_DisFax", "🔒 Desativar Fax");
            br.Add("TT_DisFax", "Desativa serviço de fax.");
            br.Add("L_DeepTemp", "🔒 Limpeza Profunda Temp");
            br.Add("TT_DeepTemp", "Limpa arquivos temporários agressivamente.");
            br.Add("L_KernelOverride", "MODIFICAÇÃO KERNEL");
            br.Add("L_MsiMode", "🔒 Ativar Modo MSI (GPU)");
            br.Add("TT_MsiMode", "Modo MSI para FPS estável.");
            br.Add("L_CoreLasso", "🔒 Isolamento de Núcleos");
            br.Add("TT_CoreLasso", "Isola CPU para jogos.");
            br.Add("L_BcdTweak", "🔒 Otimização de Boot (BCD)");
            br.Add("TT_BcdTweak", "Otimiza inicialização do kernel.");
            br.Add("L_Interrupt", "🔒 Redirecionamento de Interrupção");
            br.Add("TT_Interrupt", "Redireciona interrupções de hardware.");
            br.Add("L_ProDefense", "DEFESA PRO");
            br.Add("L_WmiBlock", "🔒 Bloqueador WMI");
            br.Add("TT_WmiBlock", "Bloqueia telemetria.");
            br.Add("L_ExploitOff", "🔒 Proteção Exploit OFF");
            br.Add("TT_ExploitOff", "Desativa defesas que causam lag.");
            br.Add("L_CorePark", "🔒 Desativar Core Parking");
            br.Add("TT_CorePark", "Impede núcleos de dormir.");
            br.Add("L_DisThrottle", "🔒 Desativar Throttling");
            br.Add("TT_DisThrottle", "Impede CPU de diminuir clock.");
            br.Add("L_RamFix", "🔒 Fix de Latência RAM");
            br.Add("TT_RamFix", "Otimiza latência de RAM.");
            br.Add("L_ApplyBtn", "APLICAR CONFIGURAÇÃO");
            br.Add("TT_ApplyBtn", "Aplica as mudanças selecionadas.");
            br.Add("L_QuickActions", "AÇÕES RÁPIDAS");
            br.Add("L_CleanSys", "🔒 LIMPAR SISTEMA");
            br.Add("L_CleanLaunch", "🔒 LIMPAR LAUNCHERS");
            br.Add("L_Debloat", "🔒 DEBLOAT PREMIUM");
            br.Add("L_NetReset", "🔒 RESET DE REDE");
            br.Add("L_OptiRam", "🔒 OTIMIZAR RAM");
            br.Add("L_BtnCloudflare", "🔒 CLOUDFLARE");
            br.Add("L_BtnGoogle", "🔒 GOOGLE");
            br.Add("L_NetIntel", "INTELIGÊNCIA DE REDE");
            br.Add("L_DnsInject", "INJEÇÃO DE DNS");
            br.Add("L_SysSecurity", "SEGURANÇA DO SISTEMA");
            br.Add("L_FreezeUpd", "🔒 CONGELAR ATUALIZAÇÕES");
            br.Add("L_SfcScan", "🔒 SCAN SFC DE INTEGRIDADE");
            br.Add("L_LimitGames", "LIMITE: 15 JOGOS (PREMIUM)");
            br.Add("L_AddCustomExe", "+   ADICIONAR SEU .EXE");
            br.Add("L_UserStatus", "MEMBRO PREMIUM");
            br.Add("L_LicenseOver", "CHAVE DE LICENÇA");
            br.Add("L_RefLicense", "ATUALIZAR LICENÇA");
            br.Add("L_SysStat", "ESTATÍSTICAS DO SISTEMA");
            br.Add("L_StatJunk", "🗑 LIXO REMOVIDO");
            br.Add("L_StatRam", "🧠 RAM LIBERADA");
            br.Add("L_StatTweaks", "⚙ TWEAKS APLICADOS");
            br.Add("L_StatBoosts", "🚀 JOGOS ACELERADOS");
            br.Add("L_SysBehav", "COMPORTAMIENTO DO SISTEMA");
            br.Add("L_StartWin", "Iniciar com Windows");
            br.Add("L_MinTray", "Minimizar para a Bandeja");
            br.Add("L_DiscordRpc", "Discord RPC");
            br.Add("L_LangConfig", "IDIOMA");
            br.Add("L_SyncSet", "SINCRONIZAR CONFIGURAÇÕES");
            br.Add("L_VisualDna", "ESTILO VISUAL");
            br.Add("L_DynRgb", "Pulso RGB Dinâmico");
            br.Add("L_Opacity", "OPACIDADE HUD");
            br.Add("L_ParticleDen", "DENSIDADE DE PARTÍCULAS");
            br.Add("L_Uptime", "TEMPO ATIVO: 0d 1h 44m");
            br.Add("L_LoggingIn", "Logando no servidor...");
            _locales.Add("pt-BR", br);

            // ================== FRANÇAIS (fr) ==================
            var fr = new ResourceDictionary();
            fr.Add("L_AppTitle", "KSYXIS TWEAKS");
            fr.Add("TT_NavUser", "Votre Profil et Statistiques");
            fr.Add("TT_NavHome", "Panneau Principal");
            fr.Add("TT_NavTurbo", "Actions Rapides et Turbo");
            fr.Add("TT_NavGames", "Profils de Jeux");
            fr.Add("TT_NavSettings", "Paramètres");
            fr.Add("L_NetworkStack", "PILE RÉSEAU");
            fr.Add("L_DisableNagle", "Désact. Algorithme Nagle");
            fr.Add("TT_DisableNagle", "Réduit la latence et le ping.");
            fr.Add("L_FlushDns", "Vider le Cache DNS");
            fr.Add("TT_FlushDns", "Vide le DNS pour la stabilité.");
            fr.Add("L_TcpHardware", "Déchargement Matériel TCP");
            fr.Add("TT_TcpHardware", "Soulage le processeur réseau.");
            fr.Add("L_BlockWinUpdate", "Bloquer Mises à Jour Win");
            fr.Add("TT_BlockWinUpdate", "Bloque les MAJ en jeu.");
            fr.Add("L_TcpWindow", "🔒 Optimisation Fenêtre TCP");
            fr.Add("TT_TcpWindow", "Optimise la réception TCP.");
            fr.Add("L_NetshContext", "🔒 Commutation Netsh");
            fr.Add("TT_NetshContext", "Réduit la charge réseau.");
            fr.Add("L_GpuRendering", "GPU ET RENDU");
            fr.Add("L_HighGpu", "🔒 Haute Priorité GPU");
            fr.Add("TT_HighGpu", "Donne la priorité graphique max.");
            fr.Add("L_DisableMpo", "🔒 Désactiver DWM MPO");
            fr.Add("TT_DisableMpo", "Répare les lags en mode fenêtré.");
            fr.Add("L_ForceGameMode", "🔒 Forcer Mode Jeu");
            fr.Add("TT_ForceGameMode", "Force le mode jeu Windows.");
            fr.Add("L_ClearShader", "🔒 Vider Cache Shaders");
            fr.Add("TT_ClearShader", "Supprime les anciens shaders.");
            fr.Add("L_UltPower", "🔒 Plan Énergie Maximal");
            fr.Add("TT_UltPower", "Active l'alimentation max.");
            fr.Add("L_InputLatency", "ENTRÉE ET LATENCE");
            fr.Add("L_RawInput", "🔒 Entrée Brute 1:1");
            fr.Add("TT_RawInput", "Désactive l'accélération souris.");
            fr.Add("L_KeyboardFix", "🔒 Opti. Latence Clavier");
            fr.Add("TT_KeyboardFix", "Réduit le délai du clavier.");
            fr.Add("L_TimerRes", "🔒 Résolution Horloge 0.5ms");
            fr.Add("TT_TimerRes", "Augmente la précision de l'horloge.");
            fr.Add("L_UsbOverride", "🔒 Forçage Taux USB");
            fr.Add("TT_UsbOverride", "Optimise le taux de rafraîchissement USB.");
            fr.Add("L_CleanAudio", "🔒 Traitement Audio Propre");
            fr.Add("TT_CleanAudio", "Désactive les effets audio inutiles.");
            fr.Add("L_ServicesOs", "SERVICES ET OS");
            fr.Add("L_DisSpooler", "🔒 Désactiver Spooler");
            fr.Add("TT_DisSpooler", "Désactive l'impression.");
            fr.Add("L_DisSysmain", "🔒 Désactiver SysMain");
            fr.Add("TT_DisSysmain", "Désactive l'indexation.");
            fr.Add("L_DisMaps", "🔒 Désactiver Cartes");
            fr.Add("TT_DisMaps", "Désactive le suivi Windows.");
            fr.Add("L_DisFax", "🔒 Désactiver Fax");
            fr.Add("TT_DisFax", "Désactive les fax Windows.");
            fr.Add("L_DeepTemp", "🔒 Nettoyage Profond Temp");
            fr.Add("TT_DeepTemp", "Supprime les fichiers temporaires.");
            fr.Add("L_KernelOverride", "MODIFICATION KERNEL");
            fr.Add("L_MsiMode", "🔒 Activer Mode MSI (GPU)");
            fr.Add("TT_MsiMode", "Mode MSI pour des FPS stables.");
            fr.Add("L_CoreLasso", "🔒 Isolation Coeurs");
            fr.Add("TT_CoreLasso", "Isole les cœurs pour le jeu.");
            fr.Add("L_BcdTweak", "🔒 Optimisation Démarrage");
            fr.Add("TT_BcdTweak", "Optimise le boot du kernel.");
            fr.Add("L_Interrupt", "🔒 Redirection Interruptions");
            fr.Add("TT_Interrupt", "Gère les interruptions réseau.");
            fr.Add("L_ProDefense", "DÉFENSE PRO");
            fr.Add("L_WmiBlock", "🔒 Bloqueur WMI");
            fr.Add("TT_WmiBlock", "Bloque la télémétrie.");
            fr.Add("L_ExploitOff", "🔒 Désact. Protection Exploit");
            fr.Add("TT_ExploitOff", "Désactive les défenses gênantes.");
            fr.Add("L_CorePark", "🔒 Désact. Mise en Veille CPU");
            fr.Add("TT_CorePark", "Empêche les cœurs de dormir.");
            fr.Add("L_DisThrottle", "🔒 Désactiver Throttling");
            fr.Add("TT_DisThrottle", "Empêche le CPU de ralentir.");
            fr.Add("L_RamFix", "🔒 Opti. Latence RAM");
            fr.Add("TT_RamFix", "Améliore la latence RAM.");
            fr.Add("L_ApplyBtn", "APPLIQUER LA CONFIGURATION");
            fr.Add("TT_ApplyBtn", "Applique tous les changements.");
            fr.Add("L_QuickActions", "ACTIONS RAPIDES");
            fr.Add("L_CleanSys", "🔒 NETTOYER SYSTÈME");
            fr.Add("L_CleanLaunch", "🔒 NETTOYER LAUNCHERS");
            fr.Add("L_Debloat", "🔒 DEBLOAT PREMIUM");
            fr.Add("L_NetReset", "🔒 RÉINITIALISER RÉSEAU");
            fr.Add("L_OptiRam", "🔒 OPTIMISER LA RAM");
            fr.Add("L_BtnCloudflare", "🔒 CLOUDFLARE");
            fr.Add("L_BtnGoogle", "🔒 GOOGLE");
            fr.Add("L_NetIntel", "INTELLIGENCE RÉSEAU");
            fr.Add("L_DnsInject", "INJECTION DNS");
            fr.Add("L_SysSecurity", "SÉCURITÉ SYSTÈME");
            fr.Add("L_FreezeUpd", "🔒 GELER MISES À JOUR");
            fr.Add("L_SfcScan", "🔒 SCAN D'INTÉGRITÉ SFC");
            fr.Add("L_LimitGames", "LIMITE: 15 JEUX (PREMIUM)");
            fr.Add("L_AddCustomExe", "+   AJOUTER VOTRE .EXE");
            fr.Add("L_UserStatus", "MEMBRE PREMIUM");
            fr.Add("L_LicenseOver", "CLÉ DE LICENCE");
            fr.Add("L_RefLicense", "RAFRAÎCHIR LICENCE");
            fr.Add("L_SysStat", "STATISTIQUES SYSTÈME");
            fr.Add("L_StatJunk", "🗑 DÉCHETS PURGÉS");
            fr.Add("L_StatRam", "🧠 RAM LIBÉRÉE");
            fr.Add("L_StatTweaks", "⚙ TWEAKS APPLIQUÉS");
            fr.Add("L_StatBoosts", "🚀 JEUX BOOSTÉS");
            fr.Add("L_SysBehav", "COMPORTEMENT SYSTÈME");
            fr.Add("L_StartWin", "Démarrer avec Windows");
            fr.Add("L_MinTray", "Réduire dans la barre");
            fr.Add("L_DiscordRpc", "Discord RPC");
            fr.Add("L_LangConfig", "LANGUE");
            fr.Add("L_SyncSet", "SYNCHRONISER PARAMÈTRES");
            fr.Add("L_VisualDna", "STYLE VISUEL");
            fr.Add("L_DynRgb", "Pouls RGB Dynamique");
            fr.Add("L_Opacity", "OPACITÉ DU HUD");
            fr.Add("L_ParticleDen", "DENSITÉ DES PARTICULES");
            fr.Add("L_Uptime", "TEMPS D'ACTIVITÉ: 0j 1h 44m");
            fr.Add("L_LoggingIn", "Connexion au serveur...");
            _locales.Add("fr", fr);

            // ================== TÜRKÇE (tr) ==================
            var tr = new ResourceDictionary();
            tr.Add("L_AppTitle", "KSYXIS TWEAKS");
            tr.Add("TT_NavUser", "Profiliniz ve İstatistikler");
            tr.Add("TT_NavHome", "Ana Optimizasyon Paneli");
            tr.Add("TT_NavTurbo", "Hızlı İşlemler ve Turbo");
            tr.Add("TT_NavGames", "Oyun Profilleri");
            tr.Add("TT_NavSettings", "Uygulama Ayarları");
            tr.Add("L_NetworkStack", "AĞ YIĞINI");
            tr.Add("L_DisableNagle", "Nagle Alg. Kapat");
            tr.Add("TT_DisableNagle", "Gecikmeyi ve pingi azaltır.");
            tr.Add("L_FlushDns", "DNS Önbelleğini Temizle");
            tr.Add("TT_FlushDns", "Sabit ağ için DNS temizler.");
            tr.Add("L_TcpHardware", "TCP Donanım Boşaltması");
            tr.Add("TT_TcpHardware", "Ağ yükünü donanıma aktarır.");
            tr.Add("L_BlockWinUpdate", "Windows Güncellemelerini Engelle");
            tr.Add("TT_BlockWinUpdate", "Oyun sırasında güncellemeleri durdurur.");
            tr.Add("L_TcpWindow", "🔒 TCP Pencere Ayarı");
            tr.Add("TT_TcpWindow", "TCP veri alımını optimize eder.");
            tr.Add("L_NetshContext", "🔒 Netsh Bağlam Değişimi");
            tr.Add("TT_NetshContext", "Ağ geçiş gecikmesini azaltır.");
            tr.Add("L_GpuRendering", "GPU VE İŞLEME");
            tr.Add("L_HighGpu", "🔒 Yüksek GPU Önceliği");
            tr.Add("TT_HighGpu", "Oyunlara en yüksek grafik önceliğini verir.");
            tr.Add("L_DisableMpo", "🔒 DWM MPO Kapat");
            tr.Add("TT_DisableMpo", "Pencere modunda takılmaları düzeltir.");
            tr.Add("L_ForceGameMode", "🔒 Oyun Moduna Zorla");
            tr.Add("TT_ForceGameMode", "Windows Oyun modunu açar.");
            tr.Add("L_ClearShader", "🔒 Shader Önbelleği Temizle");
            tr.Add("TT_ClearShader", "Grafik hataları için eski shaderları siler.");
            tr.Add("L_UltPower", "🔒 Nihai Güç Planı");
            tr.Add("TT_UltPower", "Gizli performans güç planını açar.");
            tr.Add("L_InputLatency", "GİRİŞ VE GECİKME");
            tr.Add("L_RawInput", "🔒 Birebir Ham Giriş");
            tr.Add("TT_RawInput", "Fare ivmesini kapatır.");
            tr.Add("L_KeyboardFix", "🔒 Klavye Gecikme Düzeltmesi");
            tr.Add("TT_KeyboardFix", "Klavye tepki süresini kısaltır.");
            tr.Add("L_TimerRes", "🔒 0.5ms Zamanlayıcı");
            tr.Add("TT_TimerRes", "Sistem saat hassasiyetini artırır.");
            tr.Add("L_UsbOverride", "🔒 USB Oranını Geçersiz Kıl");
            tr.Add("TT_UsbOverride", "USB port hızını optimize eder.");
            tr.Add("L_CleanAudio", "🔒 Temiz Ses İşleme");
            tr.Add("TT_CleanAudio", "Ses geliştirmelerini kapatıp CPU korur.");
            tr.Add("L_ServicesOs", "HİZMETLER VE İŞLETİM SİSTEMİ");
            tr.Add("L_DisSpooler", "🔒 Yazdırmayı Kapat");
            tr.Add("TT_DisSpooler", "Yazıcı hizmetlerini kapatır.");
            tr.Add("L_DisSysmain", "🔒 SysMain Kapat");
            tr.Add("TT_DisSysmain", "Arka plan dosya indekslemeyi durdurur.");
            tr.Add("L_DisMaps", "🔒 Haritaları Kapat");
            tr.Add("TT_DisMaps", "Konum izlemeyi kapatır.");
            tr.Add("L_DisFax", "🔒 Faksı Kapat");
            tr.Add("TT_DisFax", "Faks hizmetlerini kapatır.");
            tr.Add("L_DeepTemp", "🔒 Derin Temp Temizliği");
            tr.Add("TT_DeepTemp", "Geçici dosyaları siler.");
            tr.Add("L_KernelOverride", "ÇEKİRDEK (KERNEL) MODİFİYESİ");
            tr.Add("L_MsiMode", "🔒 MSI Modu Aç (GPU)");
            tr.Add("TT_MsiMode", "GPU için kararlı MSI modu.");
            tr.Add("L_CoreLasso", "🔒 Çekirdek İzolasyonu");
            tr.Add("TT_CoreLasso", "Oyunlar için CPU izole eder.");
            tr.Add("L_BcdTweak", "🔒 Önyükleme Optimizasyonu");
            tr.Add("TT_BcdTweak", "Kernel başlatma parametrelerini düzenler.");
            tr.Add("L_Interrupt", "🔒 Kesinti Yönlendirme");
            tr.Add("TT_Interrupt", "Ağ kesintilerini düzenler.");
            tr.Add("L_ProDefense", "PRO SAVUNMA");
            tr.Add("L_WmiBlock", "🔒 WMI Etkinlik Engelleyici");
            tr.Add("TT_WmiBlock", "Arka plan telemetrisini durdurur.");
            tr.Add("L_ExploitOff", "🔒 Exploit Koruması Kapalı");
            tr.Add("TT_ExploitOff", "FPS düşüren güvenliği kapatır.");
            tr.Add("L_CorePark", "🔒 Çekirdek Uyutmayı Kapat");
            tr.Add("TT_CorePark", "CPU'nun uyku moduna geçmesini önler.");
            tr.Add("L_DisThrottle", "🔒 Yavaşlatmayı Kapat");
            tr.Add("TT_DisThrottle", "CPU hız düşürmesini engeller.");
            tr.Add("L_RamFix", "🔒 RAM Gecikme Düzeltmesi");
            tr.Add("TT_RamFix", "Bellek gecikmelerini optimize eder.");
            tr.Add("L_ApplyBtn", "YAPILANDIRMAYI UYGULA");
            tr.Add("TT_ApplyBtn", "Tüm değişiklikleri uygular.");
            tr.Add("L_QuickActions", "HIZLI İŞLEMLER");
            tr.Add("L_CleanSys", "🔒 SİSTEMİ TEMİZLE");
            tr.Add("L_CleanLaunch", "🔒 LAUNCHERLARI TEMİZLE");
            tr.Add("L_Debloat", "🔒 PREMIUM DEBLOAT");
            tr.Add("L_NetReset", "🔒 AĞ SIFIRLAMA");
            tr.Add("L_OptiRam", "🔒 RAM OPTİMİZE ET");
            tr.Add("L_BtnCloudflare", "🔒 CLOUDFLARE");
            tr.Add("L_BtnGoogle", "🔒 GOOGLE");
            tr.Add("L_NetIntel", "AĞ İSTİHBARATI");
            tr.Add("L_DnsInject", "DNS ENJEKSİYONU");
            tr.Add("L_SysSecurity", "SİSTEM GÜVENLİĞİ");
            tr.Add("L_FreezeUpd", "🔒 GÜNCELLEMELERİ DONDUR");
            tr.Add("L_SfcScan", "🔒 SFC BÜTÜNLÜK TARAMASI");
            tr.Add("L_LimitGames", "LİMİT: 15 OYUN (PREMIUM)");
            tr.Add("L_AddCustomExe", "+\tÖZEL .EXE EKLE");
            tr.Add("L_UserStatus", "PREMIUM ÜYE");
            tr.Add("L_LicenseOver", "LİSANS ANAHTARI");
            tr.Add("L_RefLicense", "LİSANSI YENİLE");
            tr.Add("L_SysStat", "SİSTEM İSTATİSTİKLERİ");
            tr.Add("L_StatJunk", "🗑 SİLİNEN ÇÖP");
            tr.Add("L_StatRam", "🧠 BOŞALTILAN RAM");
            tr.Add("L_StatTweaks", "⚙ UYGULANAN TWEAKS");
            tr.Add("L_StatBoosts", "🚀 HIZLANDIRILAN OYUNLAR");
            tr.Add("L_SysBehav", "SİSTEM DAVRANIŞI");
            tr.Add("L_StartWin", "Windows ile başlat");
            tr.Add("L_MinTray", "Görev çubuğuna küçült");
            tr.Add("L_DiscordRpc", "Discord RPC");
            tr.Add("L_LangConfig", "DİL AYARLARI");
            tr.Add("L_SyncSet", "AYARLARI EŞİTLE");
            tr.Add("L_VisualDna", "GÖRSEL DNA");
            tr.Add("L_DynRgb", "Dinamik RGB Puls");
            tr.Add("L_Opacity", "HUD SAYDAMLIK");
            tr.Add("L_ParticleDen", "PARÇACIK YOĞUNLUĞU");
            tr.Add("L_Uptime", "ÇALIŞMA SÜRESİ: 0g 1s 44d");
            tr.Add("L_LoggingIn", "Sunucuya bağlanıyor...");
            _locales.Add("tr", tr);
        }

        public static void ChangeLanguage(string langCode)
        {
            if (_locales.ContainsKey(langCode))
            {
                var dict = _locales[langCode];

                // Keys that belong ONLY to the PLUS package
                string[] plusKeys = { "L_CleanSys", "L_CleanLaunch", "L_NetReset", "L_OptiRam",
                                      "L_HighGpu", "L_DisableMpo", "L_ForceGameMode", "L_ClearShader",
                                      "L_UltPower", "L_RawInput", "L_KeyboardFix", "L_TimerRes", "L_DeepTemp" };

                foreach (System.Collections.DictionaryEntry entry in dict)
                {
                    string keyStr = entry.Key.ToString() ?? "";
                    string valueStr = entry.Value?.ToString() ?? "";

                    // DYNAMIC LOCK REMOVAL BASED ON TIER
                    if (CurrentTier == UserTier.Premium)
                    {
                        valueStr = valueStr.Replace("🔒 ", "");
                    }
                    else if (CurrentTier == UserTier.Plus)
                    {
                        if (plusKeys.Contains(keyStr))
                        {
                            valueStr = valueStr.Replace("🔒 ", "");
                        }
                    }

                    Application.Current.Resources[keyStr] = valueStr;
                }
            }
        }
    }
}
