using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace XxtWidget
{
    public class AppConfig
    {
        public string Account { get; set; }
        public string PasswordProtected { get; set; }
        public int RefreshMinutes { get; set; }
        public bool IncludeOverdue { get; set; }
        public int OverdueDays { get; set; }
        public double Opacity { get; set; }
        public bool AutoStart { get; set; }
        public double WindowLeft { get; set; }
        public double WindowTop { get; set; }
        public double WindowWidth { get; set; }
        public double WindowHeight { get; set; }

        public AppConfig()
        {
            Account = "";
            PasswordProtected = "";
            RefreshMinutes = 60;
            IncludeOverdue = false;
            OverdueDays = 30;
            Opacity = 0.9;
            AutoStart = true;
            WindowLeft = -1;
            WindowTop = -1;
            WindowWidth = 440;
            WindowHeight = 580;
        }
    }

    public static class ConfigStore
    {
        private static readonly JavaScriptSerializer Ser = new JavaScriptSerializer();

        public static AppConfig Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    AppConfig cfg = Ser.Deserialize<AppConfig>(File.ReadAllText(path, Encoding.UTF8));
                    if (cfg != null)
                    {
                        if (cfg.RefreshMinutes < 10) cfg.RefreshMinutes = 60;
                        if (cfg.RefreshMinutes > 360) cfg.RefreshMinutes = 360;
                        if (cfg.OverdueDays != 7 && cfg.OverdueDays != 30) cfg.OverdueDays = 30;
                        if (cfg.Opacity < 0.55) cfg.Opacity = 0.55;
                        if (cfg.Opacity > 1.0) cfg.Opacity = 1.0;
                        if (cfg.WindowWidth < 340) cfg.WindowWidth = 440;
                        if (cfg.WindowHeight < 260) cfg.WindowHeight = 580;
                        return cfg;
                    }
                }
            }
            catch (Exception ex) { Log.Write("config load: " + ex.Message); }
            return new AppConfig();
        }

        public static void Save(string path, AppConfig cfg)
        {
            try
            {
                string json = Ser.Serialize(cfg);
                File.WriteAllText(path, json, new UTF8Encoding(false));
            }
            catch (Exception ex) { Log.Write("config save: " + ex.Message); }
        }

        public static string GetPassword(AppConfig cfg)
        {
            try
            {
                if (string.IsNullOrEmpty(cfg.PasswordProtected)) return "";
                byte[] data = Convert.FromBase64String(cfg.PasswordProtected);
                byte[] plain = ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch (Exception ex) { Log.Write("password unprotect: " + ex.Message); return ""; }
        }

        public static void SetPassword(AppConfig cfg, string password)
        {
            try
            {
                byte[] plain = Encoding.UTF8.GetBytes(password);
                byte[] data = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
                cfg.PasswordProtected = Convert.ToBase64String(data);
            }
            catch (Exception ex) { Log.Write("password protect: " + ex.Message); cfg.PasswordProtected = ""; }
        }
    }

    public class CacheItem
    {
        public string Title { get; set; }
        public string CourseName { get; set; }
        public string ClassName { get; set; }
        public string DetailUrl { get; set; }
        public string Status { get; set; }
        public long DeadlineTicks { get; set; }
        public long PublishTicks { get; set; }
    }

    public class CacheFile
    {
        public string LastSync { get; set; }
        public List<CacheItem> Items { get; set; }

        public CacheFile()
        {
            LastSync = "";
            Items = new List<CacheItem>();
        }
    }

    public static class CacheStore
    {
        private static readonly JavaScriptSerializer Ser = new JavaScriptSerializer();

        public static CacheFile Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                return Ser.Deserialize<CacheFile>(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception ex) { Log.Write("cache load: " + ex.Message); return null; }
        }

        public static void Save(string path, IEnumerable<HomeworkItem> items, DateTime lastSync)
        {
            try
            {
                CacheFile file = new CacheFile();
                file.LastSync = lastSync.ToString("o");
                foreach (HomeworkItem it in items)
                {
                    CacheItem c = new CacheItem();
                    c.Title = it.Title;
                    c.CourseName = it.CourseName;
                    c.ClassName = it.ClassName;
                    c.DetailUrl = it.DetailUrl;
                    c.Status = it.Status;
                    c.DeadlineTicks = it.Deadline.HasValue ? it.Deadline.Value.Ticks : 0;
                    c.PublishTicks = it.PublishDate.HasValue ? it.PublishDate.Value.Ticks : 0;
                    file.Items.Add(c);
                }
                File.WriteAllText(path, Ser.Serialize(file), new UTF8Encoding(false));
            }
            catch (Exception ex) { Log.Write("cache save: " + ex.Message); }
        }
    }

    public static class AutoStart
    {
        public static string ShortcutPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "学习通作业提醒.lnk"); }
        }

        public static bool IsEnabled()
        {
            try { return File.Exists(ShortcutPath); } catch { return false; }
        }

        public static bool Set(bool enabled)
        {
            try
            {
                if (!enabled)
                {
                    if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
                    return true;
                }
                string exe = Assembly.GetEntryAssembly().Location;
                object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                object link = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { ShortcutPath });
                Type t = link.GetType();
                t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { exe });
                t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { Path.GetDirectoryName(exe) });
                t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, link, new object[] { exe + ",0" });
                t.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { "学习通作业剩余时间悬浮窗" });
                t.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
                return true;
            }
            catch (Exception ex) { Log.Write("autostart: " + ex.Message); return false; }
        }
    }

    public static class Log
    {
        private static readonly object Sync = new object();
        public static string FilePath { get; set; }

        public static void Write(string message)
        {
            try
            {
                if (string.IsNullOrEmpty(FilePath)) return;
                lock (Sync)
                {
                    FileInfo fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > 512 * 1024) fi.Delete();
                    File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }
    }
}