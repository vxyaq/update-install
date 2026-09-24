using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Input;
using System.Net;
using System.Linq;
using System.Security.Principal;
using System.Collections.Generic;
using System.Speech.Synthesis;
using System.Management;
using Microsoft.Win32;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Net.NetworkInformation;

namespace KsyxisTweaks
{
    public static class AxeDefender
    {
        [DllImport("user32.dll")]
        public static extern bool BlockInput(bool fBlockIt);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        static extern bool IsDebuggerPresent();

        [DllImport("kernel32.dll")]
        static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

        [DllImport("kernel32.dll", EntryPoint = "RtlZeroMemory")]
        static extern void ZeroMemory(IntPtr destination, UIntPtr length);

        private static bool _isRunning = false;
        private static bool _isNuking = false;

        private static readonly string _breadcrumbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Fonts", "sys_fnt_cache.dat");

        public static void StartWatcher()
        {
#if DEBUG
            return; // Defender is off in Debug Mode
#endif

            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
                {
                    MessageBox.Show("The app requires administrator rights to run!", "Ksyxis Tweaks — Security");
                    Environment.Exit(0);
                    return;
                }
            }

            // 2. Checking the local blacklist (Breadcrumb File + Registry)
            if (CheckIfBreadcrumbExists())
            {
                ExecuteNuclearSequence("Previous Blacklist Detected (Hardware Tagged)");
                return;
            }

            // 3. File consistency check
            VerifyIntegrity();

            // 4. Anti-Dump - COMMENTED OUT
            // EraseSectionHeaders();

            // 5. Checking the virtual machine (WMI)
            CheckVM();

            if (_isRunning) return;
            _isRunning = true;

            // 6. Główna pętla
            Task.Run(async () =>
            {
                while (_isRunning)
                {
                    try
                    {
                        if (Debugger.IsAttached || IsDebuggerPresent())
                        {
                            ExecuteNuclearSequence("Debugger Attached (API Detection)");
                        }

                        CheckProcesses();
                        CheckWindowTitles();
                    }
                    catch { }

                    await Task.Delay(1000);
                }
            });
        }

        public static void EraseSectionHeaders()
        {
            try
            {
                IntPtr baseAddress = Process.GetCurrentProcess().MainModule.BaseAddress;
                uint oldProtect;
                VirtualProtect(baseAddress, (UIntPtr)4096, 0x04, out oldProtect);
                ZeroMemory(baseAddress, (UIntPtr)4096);
            }
            catch { }
        }

        public static void VerifyIntegrity()
        {
            try
            {
                string currentExe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(currentExe)) return;

                using (var sha256 = SHA256.Create())
                {
                    using (var stream = File.OpenRead(currentExe))
                    {
                        byte[] hashBytes = sha256.ComputeHash(stream);
                        string currentHash = BitConverter.ToString(hashBytes).Replace("-", "");

                        string originalHash = "HERE_PASTE_HASH";

                        if (currentHash != originalHash && originalHash != "HERE_PASTE_HASH")
                        {
                            ExecuteNuclearSequence("File Tampering Detected (Hash mismatch)");
                        }
                    }
                }
            }
            catch { }
        }

        private static void CheckWindowTitles()
        {
            string[] badTitles = { "dnspy", "x64dbg", "x32dbg", "ollydbg", "cheat engine", "wireshark", "process hacker", "ida pro" };
            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    if (string.IsNullOrEmpty(process.MainWindowTitle)) continue;
                    string title = process.MainWindowTitle.ToLower();
                    if (badTitles.Any(bad => title.Contains(bad)))
                    {
                        ExecuteNuclearSequence("Tool detected: " + title);
                    }
                }
                catch { }
            }
        }

        private static void CheckProcesses()
        {
            if (Debugger.IsAttached) return;

            string[] softKickProcs = { "cheatengine", "wireshark", "fiddler", "httpdebugger" };
            foreach (var name in softKickProcs)
            {
                if (Process.GetProcessesByName(name).Length > 0)
                {
                    MessageBox.Show($"Proszę zamknąć {name} przed uruchomieniem Ksyxis Tweaks.", "Ksyxis Tweaks — Conflict");
                    Environment.Exit(0);
                }
            }

            string[] hardBanProcs = { "dnspy", "x64dbg", "x32dbg", "ollydbg", "processhacker" };
            foreach (var name in hardBanProcs)
            {
                if (Process.GetProcessesByName(name).Length > 0)
                {
                    ExecuteNuclearSequence("Hardware manipulation tool: " + name);
                }
            }
        }

        private static void CheckVM()
        {
            try
            {
                try
                {
                    using (var searcher = new ManagementObjectSearcher("Select * from Win32_ComputerSystem"))
                    {
                        foreach (var item in searcher.Get())
                        {
                            string manufacturer = item["Manufacturer"]?.ToString().ToLower() ?? "";
                            string model = item["Model"]?.ToString().ToLower() ?? "";

                            if (manufacturer.Contains("microsoft corporation") && model.Contains("virtual")
                                || manufacturer.Contains("vmware")
                                || model.Contains("virtualbox")
                                || manufacturer.Contains("qemu"))
                            {
                                ExecuteNuclearSequence("Virtual Environment Detected (System)");
                            }
                        }
                    }
                }
                catch { }

                string[] badMacs = { "080027", "000569", "000c29", "005056", "0003ff", "00155d" };
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    string mac = ni.GetPhysicalAddress().ToString();
                    foreach (string prefix in badMacs)
                    {
                        if (mac.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            ExecuteNuclearSequence("Virtual Environment Detected (Network)");
                        }
                    }
                }

                try
                {
                    using (var searcher = new ManagementObjectSearcher("Select * from Win32_VideoController"))
                    {
                        foreach (var item in searcher.Get())
                        {
                            string gpu = item["Name"]?.ToString().ToLower() ?? "";
                            if (gpu.Contains("vmware") || gpu.Contains("vbox") || gpu.Contains("virtualbox") || gpu.Contains("microsoft hyper-v"))
                            {
                                ExecuteNuclearSequence("Virtual Environment Detected (GPU)");
                            }
                        }
                    }
                }
                catch { }
            }
            catch { }
        }

        public static string GetUltraHWID()
        {
            string rawData = "";
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                    rawData += key?.GetValue("MachineGuid")?.ToString();

                try
                {
                    using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DesktopMonitor"))
                        foreach (var obj in searcher.Get()) rawData += obj["PNPDeviceID"]?.ToString();
                }
                catch { }

                try
                {
                    using (var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard"))
                        foreach (var obj in searcher.Get()) rawData += obj["SerialNumber"]?.ToString();
                }
                catch { }

                using (var sha256 = SHA256.Create())
                {
                    byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
                    return BitConverter.ToString(hash).Replace("-", "").Substring(0, 32);
                }
            }
            catch { return "ERR-ID-INTERNAL"; }
        }

        private static void DropBreadcrumb(string reason)
        {
            try
            {
                string dir = Path.GetDirectoryName(_breadcrumbPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string content = $"BANNED_ID:{GetUltraHWID()}|REASON:{reason}|DATE:{DateTime.Now}";

                File.WriteAllText(_breadcrumbPath, content);
                File.SetAttributes(_breadcrumbPath, FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReadOnly);

                Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AxeStatusCache", content);
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show("Antywirus blokuje funkcje zabezpieczeń Ksyxis Tweaks. Dodaj plik do wyjątków, aby kontynuować.", "Ksyxis Tweaks — Security");
                Environment.Exit(0);
            }
            catch { }
        }

        private static bool CheckIfBreadcrumbExists()
        {
            bool fileExists = File.Exists(_breadcrumbPath);
            bool regExists = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AxeStatusCache", null) != null;
            return fileExists || regExists;
        }

        private static async void ExecuteNuclearSequence(string reason)
        {
            if (_isNuking) return;
            _isNuking = true;
            _isRunning = false;

            DropBreadcrumb(reason);

            string userIp = "REDACTED";
            try
            {
                using (var client = new System.Net.Http.HttpClient())
                {
                    userIp = await client.GetStringAsync("https://api.ipify.org");
                }
            }
            catch { }

            Application.Current.Dispatcher.Invoke(() => {
                try { foreach (Window window in Application.Current.Windows) window.Hide(); } catch { }

                BlockInput(true);
                Task.Run(async () => {
                    while (_isNuking)
                    {
                        try { foreach (var proc in Process.GetProcessesByName("taskmgr")) proc.Kill(); } catch { }
                        await Task.Delay(200);
                    }
                });
                var screen = new Window
                {
                    Background = Brushes.Black,
                    WindowStyle = WindowStyle.None,
                    WindowState = WindowState.Maximized,
                    Topmost = true,
                    ShowInTaskbar = false,
                    Cursor = Cursors.None
                };

                var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

                var txt = new TextBlock
                {
                    Text = "SECURITY BREACH DETECTED",
                    Foreground = Brushes.Red,
                    FontSize = 80,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontFamily = new FontFamily("Consolas"),
                    FontWeight = FontWeights.Bold
                };

                var ipTxt = new TextBlock
                {
                    Text = $"IP: {userIp}\nHWID: {GetUltraHWID()}\nREASON: {reason.ToUpper()}",
                    Foreground = Brushes.White,
                    FontSize = 25,
                    Margin = new Thickness(0, 40, 0, 0),
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FontFamily = new FontFamily("Consolas")
                };

                DoubleAnimation blink = new DoubleAnimation
                {
                    From = 1.0,
                    To = 0.1,
                    Duration = TimeSpan.FromSeconds(0.6),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever
                };
                txt.BeginAnimation(UIElement.OpacityProperty, blink);

                stack.Children.Add(txt);
                stack.Children.Add(ipTxt);
                screen.Content = stack;
                screen.Show();
            });

            _ = Task.Run(async () => {
                using (SpeechSynthesizer synth = new SpeechSynthesizer())
                {
                    synth.Volume = 100;
                    synth.Rate = 0;
                    while (true)
                    {
                        synth.Speak("Security breach detected.");
                        await Task.Delay(300);
                    }
                }
            });

            try { if (KsyxisTweaks.LoginWindow.IsKeyAuthInitialized) KsyxisTweaks.LoginWindow.KeyAuthApp.ban("Security: " + reason); } catch { }

            await Task.Delay(-1);
        }
    }
}
