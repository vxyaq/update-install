#pragma warning disable CA1416 
#pragma warning disable CS4014
using KeyAuth;
using Supabase.Gotrue;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace KsyxisTweaks
{
    public partial class LoginWindow : Window
    {
        public static api KeyAuthApp = new api(
            "",
            "",
            ""
        );

        public static bool IsKeyAuthInitialized = false;

        private List<Ellipse> _particles = new List<Ellipse>();
        private List<Vector> _velocities = new List<Vector>();
        private Random _random = new Random();

        private readonly string authPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "axe_secure.dat");
        private string _machineHwid = "";

        public LoginWindow()
        {
            InitializeComponent();
            InitializeParticles(40);

            AccountPanel.IsEnabled = false;
            LicensePanel.IsEnabled = false;
            AccountPanel.Visibility = Visibility.Collapsed;
            LicensePanel.Visibility = Visibility.Collapsed;

            SafeInit();
        }

        private async void LoginWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await UpdateService.CheckAndOfferAsync(this);
        }

        private async void SafeInit()
        {
            if (File.Exists(authPath))
            {
                AccountPanel.Visibility = Visibility.Visible;
                AccountPanel.Opacity = 0.5;
            }
            else
            {
                LicensePanel.Visibility = Visibility.Visible;
            }

            try
            {
#if !DEBUG
                if (await IsVpnActive())
                {
                    MessageBox.Show("VPN / PROXY DETECTED!\nPlease disable it.", "KSYXIS TWEAKS — SECURITY", MessageBoxButton.OK, MessageBoxImage.Warning);
                    Application.Current.Shutdown();
                    return;
                }
#endif

                await Task.Run(() => KeyAuthApp.init());

                if (KeyAuthApp.response == null || !KeyAuthApp.response.success)
                {
                    MessageBox.Show("Connection failed: " + (KeyAuthApp.response?.message ?? "Unknown error"), "Ksyxis Tweaks");
                    Application.Current.Shutdown();
                    return;
                }

                IsKeyAuthInitialized = true;
                _machineHwid = AxeDefender.GetUltraHWID();

                if (File.Exists(authPath))
                {
                    await CheckIfAlreadyLicensed();
                }
                else
                {
                    LicensePanel.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Critical Error: " + ex.Message);
            }
        }

        private async Task CheckIfAlreadyLicensed()
        {
            try
            {
                if (File.Exists(authPath))
                {
                    byte[] encrypted = File.ReadAllBytes(authPath);
                    byte[] secret = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                    string decodedData = Encoding.UTF8.GetString(secret);
                    var data = decodedData.Split('|');

                    if (data.Length >= 5 && data[4] == _machineHwid)
                    {
                        string savedKey = data[0];
                        await Task.Run(() => KeyAuthApp.license(savedKey));

                        if (KeyAuthApp.response.success)
                        {
                            AccountPanel.Visibility = Visibility.Visible;
                            AccountPanel.Opacity = 1.0;
                            AccountPanel.IsEnabled = true;
                            UserInput.Text = data[1];
                        }
                        else { ResetToLicense(); }
                    }
                    else { ResetToLicense(); }
                }
            }
            catch { ResetToLicense(); }
        }

        private void ResetToLicense()
        {
            AccountPanel.Visibility = Visibility.Collapsed;
            LicensePanel.Visibility = Visibility.Visible;
            LicensePanel.IsEnabled = true;
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void DiscordInvite_Click(object sender, RoutedEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo("https://discord.gg/axeoptimizer") { UseShellExecute = true }); } catch { }
        }

        private async void ActivateLicense_Click(object sender, RoutedEventArgs e)
        {
            string keyToVerify = LicenseInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(keyToVerify)) return;
            LicensePanel.IsEnabled = false;

            await Task.Run(() => KeyAuthApp.license(keyToVerify));

            if (KeyAuthApp.response.success)
            {
                SaveAuthData(keyToVerify, "", "", false);
                LicensePanel.Visibility = Visibility.Collapsed;
                AccountPanel.Visibility = Visibility.Visible;
                AccountPanel.Opacity = 1.0;
                AccountPanel.IsEnabled = true;
                LicenseInput.Text = "";
            }
            else
            {
                MessageBox.Show("Błąd: " + KeyAuthApp.response.message);
                LicensePanel.IsEnabled = true;
            }
        }

        private void FinalLogin_Click(object sender, RoutedEventArgs e)
        {
            if (!IsKeyAuthInitialized) return;

            string username = UserInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(username))
            {
                MessageBox.Show("Please enter a username.", "Ksyxis Tweaks");
                return;
            }

            string key = "";
            try
            {
                if (File.Exists(authPath))
                {
                    byte[] encrypted = File.ReadAllBytes(authPath);
                    byte[] secret = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                    string decodedData = Encoding.UTF8.GetString(secret);
                    key = decodedData.Split('|')[0];
                }
            }
            catch { }

            bool shouldRemember = RememberMeCheck.IsChecked ?? false;
            if (shouldRemember) SaveAuthData(key, username, PassInput.Password, true);

            // --- WYSYŁANIE WEBHOOKA W TLE ---
            Task.Run(() => SendDiscordWebhook(username, _machineHwid));

            MainWindow main = new MainWindow(username);
            main.Show();
            this.Close();
        }

        private void ForgotPass_Click(object sender, RoutedEventArgs e)
        {
            AccountPanel.Visibility = Visibility.Collapsed;
            ResetPanel.Visibility = Visibility.Visible;
        }

        private void BackToLogin_Click(object sender, RoutedEventArgs e)
        {
            ResetPanel.Visibility = Visibility.Collapsed;
            AccountPanel.Visibility = Visibility.Visible;
        }

        private void ConfirmReset_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Reset available on Discord.");
        }

        private async Task<bool> IsVpnActive()
        {
            try
            {
                using (var client = new System.Net.Http.HttpClient())
                {
                    client.DefaultRequestHeaders.Add("user-agent", "AxeOptimizer_App");
                    string response = await client.GetStringAsync("https://demo.ip-api.com/json/?fields=proxy,hosting");
                    return response.Contains("\"proxy\":true") || response.Contains("\"hosting\":true");
                }
            }
            catch { return false; }
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

        private void SaveAuthData(string key, string user, string pass, bool remember)
        {
            try
            {
                string raw = $"{key}|{user}|{pass}|{remember}|{_machineHwid}";
                byte[] secret = Encoding.UTF8.GetBytes(raw);
                byte[] encrypted = ProtectedData.Protect(secret, null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(authPath, encrypted);
            }
            catch { }
        }

        private bool _isLoginRendering = false;

        private void InitializeParticles(int count)
        {
            if (_isLoginRendering) return;
            _isLoginRendering = true;

            for (int i = 0; i < count; i++)
            {
                Ellipse p = new Ellipse { Width = _random.Next(2, 5), Height = _random.Next(2, 5), Fill = new SolidColorBrush(Colors.White) { Opacity = 0.5 } };
                p.Fill.Freeze();
                Canvas.SetLeft(p, _random.Next(0, 620)); Canvas.SetTop(p, _random.Next(0, 500));
                _particles.Add(p); _velocities.Add(new Vector(_random.NextDouble() * 0.6 - 0.3, _random.NextDouble() * 0.6 - 0.3));
                ParticleCanvas.Children.Add(p);
            }

            CompositionTarget.Rendering += (s, e) => {
                for (int i = 0; i < _particles.Count; i++)
                {
                    double x = Canvas.GetLeft(_particles[i]) + _velocities[i].X;
                    double y = Canvas.GetTop(_particles[i]) + _velocities[i].Y;

                    if (x < -10) x = 620; if (x > 620) x = -10;
                    if (y < -10) y = 500; if (y > 500) y = -10;

                    Canvas.SetLeft(_particles[i], x);
                    Canvas.SetTop(_particles[i], y);
                }
            };
        }

        // =========================================================================================
        // NOWA FUNKCJA DO DISCORD WEBHOOKA (Wysyła IP, Kraj, Username, HWID)
        // =========================================================================================
        private async Task SendDiscordWebhook(string username, string hwid)
        {
            try
            {
                string webhookUrl = "";

                string ip = "Unknown";
                string country = "Unknown";

                try
                {
                    using (var client = new System.Net.Http.HttpClient())
                    {
                        client.DefaultRequestHeaders.Add("user-agent", "Mozilla/5.0");
                        string response = await client.GetStringAsync("http://ip-api.com/json/");

                        if (response.Contains("\"query\""))
                        {
                            int ipStart = response.IndexOf("\"query\":\"") + 9;
                            int ipEnd = response.IndexOf("\"", ipStart);
                            ip = response.Substring(ipStart, ipEnd - ipStart);
                        }
                        if (response.Contains("\"country\""))
                        {
                            int cStart = response.IndexOf("\"country\":\"") + 11;
                            int cEnd = response.IndexOf("\"", cStart);
                            country = response.Substring(cStart, cEnd - cStart);
                        }
                    }
                }
                catch { }

                string jsonPayload = $@"{{
                    ""embeds"": [
                        {{
                            ""title"": ""📥 NEW USER LOGIN"",
                            ""color"": 3447003,
                            ""fields"": [
                                {{
                                    ""name"": ""👤 Username:"",
                                    ""value"": ""{username}"",
                                    ""inline"": true
                                }},
                                {{
                                    ""name"": ""🔑 HWID:"",
                                    ""value"": ""{hwid}"",
                                    ""inline"": true
                                }},
                                {{
                                    ""name"": ""🌐 IP Address:"",
                                    ""value"": ""{ip}"",
                                    ""inline"": true
                                }},
                                {{
                                    ""name"": ""🌍 Country:"",
                                    ""value"": ""{country}"",
                                    ""inline"": true
                                }}
                            ],
                            ""footer"": {{
                                ""text"": ""Ksyxis Tweaks Logger""
                            }},
                            ""timestamp"": ""{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}""
                        }}
                    ]
                }}";

                using (var client = new System.Net.Http.HttpClient())
                {
                    var content = new System.Net.Http.StringContent(jsonPayload, Encoding.UTF8, "application/json");
                    await client.PostAsync(webhookUrl, content);
                }
            }
            catch { }
        }
    }
}
namespace KeyAuth
{
    public class api
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern ushort GlobalAddAtom(string lpString);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern ushort GlobalFindAtom(string lpString);

        public string name, ownerid, version, path, seed;

        public api(string name, string ownerid, string version, string path = null)
        {
            if (ownerid.Length != 10)
            {
                Process.Start(new ProcessStartInfo("https://youtube.com/watch?v=RfDTdiBq4_o") { UseShellExecute = true });
                error("Application not setup correctly. Please watch the YouTube video for setup.");
                TerminateProcess(GetCurrentProcess(), 1);
            }

            this.name = name;
            this.ownerid = ownerid;
            this.version = version;
            this.path = path;
        }

        #region structures
        [DataContract]
        private class response_structure
        {
            [DataMember] public bool success { get; set; }
            [DataMember] public bool newSession { get; set; }
            [DataMember] public string sessionid { get; set; }
            [DataMember] public string contents { get; set; }
            [DataMember] public string response { get; set; }
            [DataMember] public string message { get; set; }
            [DataMember] public string ownerid { get; set; }
            [DataMember] public string download { get; set; }
            [DataMember(IsRequired = false, EmitDefaultValue = false)] public user_data_structure info { get; set; }
            [DataMember(IsRequired = false, EmitDefaultValue = false)] public app_data_structure appinfo { get; set; }
            [DataMember] public List<msg> messages { get; set; }
            [DataMember] public List<users> users { get; set; }
            [DataMember(Name = "2fa", IsRequired = false, EmitDefaultValue = false)] public TwoFactorData twoFactor { get; set; }
        }

        public class msg { public string message { get; set; } public string author { get; set; } public string timestamp { get; set; } }
        public class users { public string credential { get; set; } }

        [DataContract]
        private class user_data_structure
        {
            [DataMember] public string username { get; set; }
            [DataMember] public string ip { get; set; }
            [DataMember] public string hwid { get; set; }
            [DataMember] public string createdate { get; set; }
            [DataMember] public string lastlogin { get; set; }
            [DataMember] public List<Data> subscriptions { get; set; }
        }

        [DataContract]
        private class app_data_structure
        {
            [DataMember] public string numUsers { get; set; }
            [DataMember] public string numOnlineUsers { get; set; }
            [DataMember] public string numKeys { get; set; }
            [DataMember] public string version { get; set; }
            [DataMember] public string customerPanelLink { get; set; }
            [DataMember] public string downloadLink { get; set; }
        }
        #endregion

        private static string sessionid, enckey;
        bool initialized;

        public void init()
        {
            Random random = new Random();
            int length = random.Next(5, 51);
            StringBuilder sb = new StringBuilder(length);
            for (int i = 0; i < length; i++) { sb.Append((char)random.Next(32, 127)); }

            seed = sb.ToString();
            checkAtom();

            var values_to_upload = new NameValueCollection
            {
                ["type"] = "init",
                ["ver"] = version,
                ["hash"] = checksum(Process.GetCurrentProcess().MainModule.FileName),
                ["name"] = name,
                ["ownerid"] = ownerid
            };

            if (!string.IsNullOrEmpty(path))
            {
                values_to_upload.Add("token", File.ReadAllText(path));
                values_to_upload.Add("thash", TokenHash(path));
            }

            var response = req(values_to_upload);

            if (response == "KeyAuth_Invalid")
            {
                error("Application not found");
                TerminateProcess(GetCurrentProcess(), 1);
            }

            var json = response_decoder.string_to_generic<response_structure>(response);
            if (json.ownerid == ownerid)
            {
                load_response_struct(json);
                if (json.success)
                {
                    sessionid = json.sessionid;
                    initialized = true;
                }
                else if (json.message == "invalidver")
                {
                    app_data.downloadLink = json.download;
                }
            }
            else
            {
                TerminateProcess(GetCurrentProcess(), 1);
            }
        }

        void checkAtom()
        {
            return;
        }

        public static string TokenHash(string tokenPath)
        {
            using (var sha256 = SHA256.Create())
            {
                using (var s = File.OpenRead(tokenPath))
                {
                    return BitConverter.ToString(sha256.ComputeHash(s)).Replace("-", string.Empty);
                }
            }
        }

        public void CheckInit() { if (!initialized) { error("Run KeyAuthApp.init(); first"); TerminateProcess(GetCurrentProcess(), 1); } }

        public void license(string key, string code = null)
        {
            CheckInit();
            string hwid = WindowsIdentity.GetCurrent().User.Value;
            var values_to_upload = new NameValueCollection
            {
                ["type"] = "license",
                ["key"] = key,
                ["hwid"] = hwid,
                ["sessionid"] = sessionid,
                ["name"] = name,
                ["ownerid"] = ownerid,
                ["code"] = code ?? string.Empty
            };
            var response = req(values_to_upload);
            var json = response_decoder.string_to_generic<response_structure>(response);
            if (json.ownerid == ownerid)
            {
                load_response_struct(json);
                if (json.success) load_user_data(json.info);
            }
            else TerminateProcess(GetCurrentProcess(), 1);
        }

        public void ban(string reason = null)
        {
            CheckInit();
            var values_to_upload = new NameValueCollection
            {
                ["type"] = "ban",
                ["reason"] = reason,
                ["sessionid"] = sessionid,
                ["name"] = name,
                ["ownerid"] = ownerid
            };
            var response = req(values_to_upload);
            var json = response_decoder.string_to_generic<response_structure>(response);
            if (json.ownerid == ownerid) load_response_struct(json);
            else TerminateProcess(GetCurrentProcess(), 1);
        }

        public void log(string message)
        {
            CheckInit();
            var values_to_upload = new NameValueCollection
            {
                ["type"] = "log",
                ["pcuser"] = Environment.UserName,
                ["message"] = message,
                ["sessionid"] = sessionid,
                ["name"] = name,
                ["ownerid"] = ownerid
            };
            req(values_to_upload);
        }

        public static string checksum(string filename)
        {
            using (MD5 md = MD5.Create())
            {
                using (FileStream fs = File.OpenRead(filename))
                {
                    return BitConverter.ToString(md.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
                }
            }
        }

        public static void error(string message)
        {
            MessageBox.Show(message, "Ksyxis Tweaks — Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Environment.Exit(0);
        }

        private static string req(NameValueCollection post_data)
        {
            try
            {
                using (WebClient client = new WebClient())
                {
                    client.Proxy = null;
                    ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
                    var raw_response = client.UploadValues("https://keyauth.win/api/1.3/", post_data);
                    string respString = Encoding.Default.GetString(raw_response);
                    sigCheck(respString, client.ResponseHeaders, post_data.Get("type"));
                    return respString;
                }
            }
            catch
            {
                error("Connection failure. KeyAuth server might be down.");
                return "";
            }
        }

        private static void sigCheck(string resp, WebHeaderCollection headers, string type)
        {
            return;
        }

        #region data_loading
        public app_data_class app_data = new app_data_class();
        public class app_data_class { public string numUsers, numOnlineUsers, numKeys, version, customerPanelLink, downloadLink; }
        private void load_app_data(app_data_structure data)
        {
            app_data.numUsers = data.numUsers; app_data.numOnlineUsers = data.numOnlineUsers;
            app_data.numKeys = data.numKeys; app_data.version = data.version;
        }

        public user_data_class user_data = new user_data_class();
        public class user_data_class { public string username, ip, hwid, createdate, lastlogin; public List<Data> subscriptions; }
        public class Data { public string subscription, expiry, timeleft, key; }
        private void load_user_data(user_data_structure data)
        {
            user_data.username = data.username; user_data.ip = data.ip; user_data.hwid = data.hwid;
            user_data.createdate = data.createdate; user_data.lastlogin = data.lastlogin; user_data.subscriptions = data.subscriptions;
        }

        public response_class response = new response_class();
        public class response_class { public bool success; public string message; }
        private void load_response_struct(response_structure data) { response.success = data.success; response.message = data.message; }
        #endregion

        [DataContract] private class TwoFactorData { [DataMember(Name = "secret_code")] public string SecretCode { get; set; } }
        private json_wrapper response_decoder = new json_wrapper(new response_structure());
    }

    public static class encryption
    {
        public static byte[] str_to_byte_arr(string hex)
        {
            int NumberChars = hex.Length;
            byte[] bytes = new byte[NumberChars / 2];
            for (int i = 0; i < NumberChars; i += 2) bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            return bytes;
        }
    }

    public class json_wrapper
    {
        private DataContractJsonSerializer serializer;
        public json_wrapper(object obj) { serializer = new DataContractJsonSerializer(obj.GetType()); }
        public object string_to_object(string json)
        {
            using (var ms = new MemoryStream(Encoding.Default.GetBytes(json))) return serializer.ReadObject(ms);
        }
        public T string_to_generic<T>(string json) => (T)string_to_object(json);
    }
}
