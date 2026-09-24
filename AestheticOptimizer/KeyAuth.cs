#nullable disable
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows;
namespace KsyxisTweaks
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
                Process.Start(new ProcessStartInfo("") { UseShellExecute = true });
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

            Thread atomCheckThread = new Thread(() => {
                while (true)
                {
                    Thread.Sleep(60000);
                    if (GlobalFindAtom(seed) == 0) TerminateProcess(GetCurrentProcess(), 1);
                }
            });
            atomCheckThread.IsBackground = true;
            atomCheckThread.Start();
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
