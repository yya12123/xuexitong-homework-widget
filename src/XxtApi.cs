using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Script.Serialization;

namespace XxtWidget
{
    public class XxtException : Exception
    {
        public XxtException(string message) : base(message) { }
        public XxtException(string message, Exception inner) : base(message, inner) { }
    }

    public class XxtSessionException : XxtException
    {
        public XxtSessionException(string message) : base(message) { }
    }

    public class XxtApi : IDisposable
    {
        private const string AesKey = "u2oh6Vu^HWe4_AES";
        private readonly CookieContainer _cookies = new CookieContainer();
        private readonly HttpClient _http;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private readonly string _account;
        private readonly string _password;
        private bool _loggedIn;

        private static readonly Regex WorkDtRegex = new Regex(@"<dt\b([^>]*)>(.*?)</dt>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        private static readonly Regex DeadlineCnRegex = new Regex(@"截止时间\s*(\d{1,2})月(\d{1,2})日(\d{1,2})时(\d{1,2})分(?:(\d{1,2})秒)?", RegexOptions.Singleline);
        private static readonly Regex DeadlineShortRegex = new Regex(@"截止时间\s*[：:]\s*(\d{1,2})-(\d{1,2})\s+(\d{1,2}):(\d{1,2})", RegexOptions.Singleline);

        public List<string> LastWarnings { get; private set; }
        public DateTime LastSyncLocal { get; set; }

        static XxtApi()
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls | (SecurityProtocolType)12288;
                ServicePointManager.Expect100Continue = false;
                ServicePointManager.DefaultConnectionLimit = 32;
            }
            catch { }
        }

        public XxtApi(string account, string password)
        {
            _account = account;
            _password = password;
            LastWarnings = new List<string>();
            HttpClientHandler handler = new HttpClientHandler();
            handler.CookieContainer = _cookies;
            handler.UseCookies = true;
            handler.UseProxy = false;
            handler.AllowAutoRedirect = true;
            handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            _http = new HttpClient(handler);
            _http.Timeout = TimeSpan.FromSeconds(30);
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
            _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9");
        }

        public void Dispose()
        {
            try { _http.Dispose(); } catch { }
        }

        public async Task LoginAsync(CancellationToken ct)
        {
            string encrypted = EncryptPassword(_password);
            Dictionary<string, string> fields = new Dictionary<string, string>();
            fields["fid"] = "-1";
            fields["uname"] = _account;
            fields["password"] = encrypted;
            fields["refer"] = "https://i.chaoxing.com";
            fields["t"] = "true";
            fields["forbidotherlogin"] = "0";
            fields["validate"] = "";
            fields["doubleFactorLogin"] = "0";
            fields["independentId"] = "0";
            HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, "https://passport2.chaoxing.com/fanyalogin");
            req.Content = new FormUrlEncodedContent(fields);
            req.Headers.Referrer = new Uri("https://passport2.chaoxing.com/login?fid=&newversion=true&refer=https%3A%2F%2Fi.chaoxing.com");
            string body;
            using (HttpResponseMessage resp = await _http.SendAsync(req, ct))
            {
                body = await resp.Content.ReadAsStringAsync();
            }
            bool ok = false;
            string message = "登录失败，请检查账号或密码";
            try
            {
                Dictionary<string, object> data = _json.Deserialize<Dictionary<string, object>>(body);
                if (data != null)
                {
                    if (data.ContainsKey("status")) ok = Convert.ToBoolean(data["status"]);
                    if (data.ContainsKey("msg")) message = Convert.ToString(data["msg"]);
                }
            }
            catch { }
            if (!ok)
            {
                if (string.IsNullOrWhiteSpace(message) || message == "1") message = "登录失败，请检查账号或密码";
                throw new XxtException("登录失败：" + message);
            }
            _loggedIn = true;
            Log.Write("login ok for " + _account);
        }

        public async Task<List<HomeworkItem>> FetchAllAsync(bool includeOverdue, int overdueDays, IProgress<string> progress, CancellationToken ct)
        {
            LastWarnings.Clear();
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    if (!_loggedIn)
                    {
                        Report(progress, "正在登录学习通...");
                        await LoginAsync(ct);
                    }
                    Report(progress, "正在读取课程列表...");
                    List<CourseRef> courses = await GetCoursesAsync(ct);
                    if (courses.Count == 0) throw new XxtException("没有读取到课程，请确认账号可以正常登录学习通");

                    List<HomeworkItem> raw = new List<HomeworkItem>();
                    object sync = new object();
                    int courseDone = 0;
                    using (SemaphoreSlim sem = new SemaphoreSlim(5))
                    {
                        List<Task> tasks = new List<Task>();
                        foreach (CourseRef course in courses)
                        {
                            CourseRef c = course;
                            tasks.Add(Task.Run(async () =>
                            {
                                await sem.WaitAsync(ct);
                                try
                                {
                                    List<HomeworkItem> items = await GetWorkListAsync(c, includeOverdue, ct);
                                    lock (sync) raw.AddRange(items);
                                }
                                catch (XxtSessionException) { throw; }
                                catch (Exception ex)
                                {
                                    lock (sync) LastWarnings.Add(c.CourseName + "：" + ex.Message);
                                }
                                finally
                                {
                                    sem.Release();
                                    int n = Interlocked.Increment(ref courseDone);
                                    Report(progress, string.Format("正在读取作业列表 {0}/{1}", n, courses.Count));
                                }
                            }));
                        }
                        await Task.WhenAll(tasks);
                    }

                    Dictionary<string, HomeworkItem> unique = new Dictionary<string, HomeworkItem>();
                    foreach (HomeworkItem item in raw)
                    {
                        string key = item.DetailUrl;
                        if (string.IsNullOrEmpty(key)) key = item.Title + "|" + item.CourseName + "|" + item.PublishDate;
                        if (!unique.ContainsKey(key)) unique[key] = item;
                    }
                    List<HomeworkItem> list = unique.Values.ToList();

                    int total = list.Count;
                    int detailDone = 0;
                    if (total > 0)
                    {
                        using (SemaphoreSlim sem2 = new SemaphoreSlim(6))
                        {
                            List<Task> tasks = new List<Task>();
                            foreach (HomeworkItem item in list)
                            {
                                HomeworkItem it = item;
                                tasks.Add(Task.Run(async () =>
                                {
                                    await sem2.WaitAsync(ct);
                                    try
                                    {
                                        if (!string.IsNullOrEmpty(it.DetailUrl))
                                        {
                                            it.Deadline = await GetDeadlineAsync(it, ct);
                                        }
                                    }
                                    catch (XxtSessionException) { throw; }
                                    catch (Exception ex)
                                    {
                                        lock (sync) LastWarnings.Add("截止时间读取失败：" + it.Title + " (" + ex.Message + ")");
                                    }
                                    finally
                                    {
                                        sem2.Release();
                                        int n = Interlocked.Increment(ref detailDone);
                                        if (n == total || n % 4 == 0)
                                        {
                                            Report(progress, string.Format("正在获取截止时间 {0}/{1}", n, total));
                                        }
                                    }
                                }));
                            }
                            await Task.WhenAll(tasks);
                        }
                    }

                    if (includeOverdue)
                    {
                        DateTime cutoff = DateTime.Now.AddDays(-overdueDays);
                        list = list.Where(delegate(HomeworkItem it)
                        {
                            if (it.Status != "2") return true;
                            return it.Deadline.HasValue && it.Deadline.Value >= cutoff;
                        }).ToList();
                    }
                    list.Sort(CompareHomework);
                    for (int i = 0; i < list.Count; i++) list[i].Rank = i + 1;
                    LastSyncLocal = DateTime.Now;
                    Log.Write(string.Format("fetch ok: courses={0}, items={1}, warnings={2}", courses.Count, list.Count, LastWarnings.Count));
                    return list;
                }
                catch (XxtSessionException ex)
                {
                    _loggedIn = false;
                    Log.Write("session retry: " + ex.Message);
                    if (attempt == 0) continue;
                    throw;
                }
            }
            throw new XxtException("同步失败，请稍后重试");
        }

        private static int CompareHomework(HomeworkItem a, HomeworkItem b)
        {
            double ha = a.HoursLeft;
            double hb = b.HoursLeft;
            int cmp = ha.CompareTo(hb);
            if (cmp != 0) return cmp;
            return string.Compare(a.Title, b.Title, StringComparison.Ordinal);
        }

        private async Task<List<CourseRef>> GetCoursesAsync(CancellationToken ct)
        {
            string url = "https://mooc1-api.chaoxing.com/mycourse/backclazzdata?view=json&rss=1";
            string json = await GetAsync(url, "https://i.chaoxing.com/", ct);
            Dictionary<string, object> root;
            try { root = _json.Deserialize<Dictionary<string, object>>(json); }
            catch (Exception ex) { throw new XxtSessionException("课程列表解析失败：" + ex.Message); }
            if (root == null) throw new XxtSessionException("课程列表为空，登录状态可能已失效");
            if (root.ContainsKey("result") && AsString(root["result"]) != "1")
            {
                string msg = root.ContainsKey("msg") ? AsString(root["msg"]) : "未知错误";
                throw new XxtSessionException("读取课程失败：" + msg);
            }
            List<CourseRef> result = new List<CourseRef>();
            HashSet<string> seen = new HashSet<string>();
            object[] channels = AsArray(root.ContainsKey("channelList") ? root["channelList"] : null);
            if (channels == null) throw new XxtSessionException("课程列表格式异常");
            foreach (object chObj in channels)
            {
                Dictionary<string, object> ch = AsDict(chObj);
                if (ch == null) continue;
                string cata = AsString(ch.ContainsKey("cataName") ? ch["cataName"] : null);
                if (cata != "课程") continue;
                Dictionary<string, object> content = AsDict(ch.ContainsKey("content") ? ch["content"] : null);
                if (content == null) continue;
                if (AsLong(content.ContainsKey("isretire") ? content["isretire"] : null) == 1) continue;
                Dictionary<string, object> course = AsDict(content.ContainsKey("course") ? content["course"] : null);
                if (course == null) continue;
                object[] data = AsArray(course.ContainsKey("data") ? course["data"] : null);
                if (data == null || data.Length == 0) continue;
                Dictionary<string, object> info = AsDict(data[0]);
                if (info == null) continue;
                string courseId = AsString(info.ContainsKey("id") ? info["id"] : null);
                string classId = AsString(content.ContainsKey("id") ? content["id"] : null);
                if (string.IsNullOrEmpty(courseId) || string.IsNullOrEmpty(classId)) continue;
                string key = courseId + "_" + classId;
                if (!seen.Add(key)) continue;
                CourseRef c = new CourseRef();
                c.CourseId = courseId;
                c.ClassId = classId;
                c.CourseName = AsString(info.ContainsKey("name") ? info["name"] : null);
                c.ClassName = AsString(content.ContainsKey("name") ? content["name"] : null);
                result.Add(c);
            }
            return result;
        }

        private async Task<List<HomeworkItem>> GetWorkListAsync(CourseRef course, bool includeOverdue, CancellationToken ct)
        {
            string firstUrl = "https://mooc1.chaoxing.com/phone/moocAnalysis/workList?courseId=" + course.CourseId + "&classId=" + course.ClassId;
            string html = await GetAsync(firstUrl, "https://mooc1.chaoxing.com/", ct);
            if (html.IndexOf("3004", StringComparison.Ordinal) >= 0 && html.IndexOf("温馨提示", StringComparison.Ordinal) >= 0)
            {
                throw new XxtException("作业页面没有访问权限 (3004)");
            }
            List<HomeworkItem> list = new List<HomeworkItem>();
            ParseWorkRows(html, course, includeOverdue, list);
            bool more = Regex.IsMatch(html, @"var\s+havemore\s*=\s*true", RegexOptions.IgnoreCase);
            int page = 1;
            while (more && page < 10)
            {
                page++;
                Dictionary<string, string> fields = new Dictionary<string, string>();
                fields["courseId"] = course.CourseId;
                fields["classId"] = course.ClassId;
                fields["pageSize"] = "20";
                fields["pageNum"] = page.ToString(CultureInfo.InvariantCulture);
                HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, "https://mooc1.chaoxing.com/mooc-ans/phone/moocAnalysis/workList");
                req.Content = new FormUrlEncodedContent(fields);
                req.Headers.Referrer = new Uri(firstUrl);
                string fragment;
                using (HttpResponseMessage resp = await _http.SendAsync(req, ct))
                {
                    fragment = await resp.Content.ReadAsStringAsync();
                }
                int before = list.Count;
                ParseWorkRows(fragment, course, includeOverdue, list);
                int rows = Regex.Matches(fragment, @"<dt\b", RegexOptions.IgnoreCase).Count;
                if (list.Count == before && rows == 0) break;
                if (rows < 20) break;
            }
            return list;
        }

        private void ParseWorkRows(string html, CourseRef course, bool includeOverdue, List<HomeworkItem> output)
        {
            foreach (Match m in WorkDtRegex.Matches(html))
            {
                string attrs = m.Groups[1].Value;
                string body = m.Groups[2].Value;
                Match statusMatch = Regex.Match(attrs, @"status=""([^""]*)""", RegexOptions.IgnoreCase);
                if (!statusMatch.Success) continue;
                string status = statusMatch.Groups[1].Value.Trim();
                if (status != "1" && !(includeOverdue && status == "2")) continue;
                Match titleMatch = Regex.Match(body, @"<h3[^>]*>(.*?)</h3>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                if (!titleMatch.Success) continue;
                string title = Regex.Replace(titleMatch.Groups[1].Value, "<[^>]+>", " ");
                title = HttpUtility.HtmlDecode(title).Trim();
                if (string.IsNullOrEmpty(title)) continue;
                Match dataMatch = Regex.Match(attrs, @"data=""([^""]*)""", RegexOptions.IgnoreCase);
                string data = dataMatch.Success ? HttpUtility.HtmlDecode(dataMatch.Groups[1].Value).Trim() : "";
                string detailUrl = "";
                if (!string.IsNullOrEmpty(data))
                {
                    if (data.StartsWith("http", StringComparison.OrdinalIgnoreCase)) detailUrl = data;
                    else if (data.StartsWith("/", StringComparison.Ordinal)) detailUrl = "https://mooc1.chaoxing.com" + data;
                    else detailUrl = "https://mooc1.chaoxing.com/" + data;
                    detailUrl = detailUrl.Replace("#INNER", "");
                }
                Match pubMatch = Regex.Match(body, @"<p[^>]*>\s*(\d{4}-\d{2}-\d{2})\s*</p>", RegexOptions.Singleline);
                DateTime? pub = null;
                if (pubMatch.Success)
                {
                    DateTime parsed;
                    if (DateTime.TryParseExact(pubMatch.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)) pub = parsed;
                }
                HomeworkItem item = new HomeworkItem();
                item.Title = title;
                item.CourseName = course.CourseName;
                item.ClassName = course.ClassName;
                item.Status = status;
                item.PublishDate = pub;
                item.DetailUrl = detailUrl;
                item.Key = course.CourseId + "_" + course.ClassId + "_" + detailUrl;
                output.Add(item);
            }
        }

        private async Task<DateTime?> GetDeadlineAsync(HomeworkItem item, CancellationToken ct)
        {
            string html = await GetAsync(item.DetailUrl, "https://mooc1.chaoxing.com/", ct);
            Match openMatch = Regex.Match(html, "doHomeWork\\?([^'\"<>\\s]+)", RegexOptions.Singleline);
            if (openMatch.Success)
            {
                string query = HttpUtility.HtmlDecode(openMatch.Groups[1].Value).Replace("&amp;", "&");
                if (query.EndsWith("&")) query = query.Substring(0, query.Length - 1);
                item.OpenUrl = "https://mooc1.chaoxing.com/mooc-ans/work/phone/doHomeWork?" + query;
            }
            int month = 0, day = 0, hour = 0, minute = 0, second = 0;
            Match m = DeadlineCnRegex.Match(html);
            if (m.Success)
            {
                month = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                day = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                hour = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                minute = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
                if (m.Groups[5].Success) second = int.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture);
            }
            else
            {
                m = DeadlineShortRegex.Match(html);
                if (!m.Success) return null;
                month = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                day = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                hour = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
                minute = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
            }
            try
            {
                DateTime now = DateTime.Now;
                DateTime value;
                if (item.PublishDate.HasValue)
                {
                    DateTime pub = item.PublishDate.Value;
                    value = new DateTime(pub.Year, month, day, hour, minute, second);
                    if (value < pub) value = value.AddYears(1);
                }
                else
                {
                    value = new DateTime(now.Year, month, day, hour, minute, second);
                    if (value < now.AddDays(-180)) value = value.AddYears(1);
                }
                return value;
            }
            catch { return null; }
        }

        private async Task<string> GetAsync(string url, string referer, CancellationToken ct)
        {
            HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(referer)) req.Headers.Referrer = new Uri(referer);
            req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            using (HttpResponseMessage resp = await _http.SendAsync(req, ct))
            {
                string finalUrl = resp.RequestMessage != null && resp.RequestMessage.RequestUri != null ? resp.RequestMessage.RequestUri.ToString() : url;
                if (resp.StatusCode == HttpStatusCode.Redirect || resp.StatusCode == HttpStatusCode.Found)
                {
                    throw new XxtSessionException("登录状态已失效");
                }
                resp.EnsureSuccessStatusCode();
                string text = await resp.Content.ReadAsStringAsync();
                if (LooksLikeLogin(text, finalUrl)) throw new XxtSessionException("登录状态已失效，请重新登录");
                return text;
            }
        }

        private static bool LooksLikeLogin(string text, string url)
        {
            if (url != null && url.IndexOf("passport2.chaoxing.com/login", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (text == null) return false;
            bool uname = text.IndexOf("id=\"uname\"", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("name=\"uname\"", StringComparison.OrdinalIgnoreCase) >= 0;
            bool pwd = text.IndexOf("id=\"pwd\"", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("name=\"password\"", StringComparison.OrdinalIgnoreCase) >= 0;
            return uname && pwd;
        }

        private static string EncryptPassword(string password)
        {
            using (Aes aes = Aes.Create())
            {
                byte[] key = Encoding.UTF8.GetBytes(AesKey);
                aes.Key = key;
                aes.IV = key;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using (ICryptoTransform enc = aes.CreateEncryptor())
                {
                    byte[] data = Encoding.UTF8.GetBytes(password);
                    return Convert.ToBase64String(enc.TransformFinalBlock(data, 0, data.Length));
                }
            }
        }

        private static void Report(IProgress<string> progress, string message)
        {
            if (progress != null)
            {
                try { progress.Report(message); } catch { }
            }
        }

        private static Dictionary<string, object> AsDict(object value) { return value as Dictionary<string, object>; }
        private static object[] AsArray(object value)
        {
            if (value == null) return null;
            object[] direct = value as object[];
            if (direct != null) return direct;
            System.Collections.IEnumerable enumerable = value as System.Collections.IEnumerable;
            if (enumerable != null && !(value is string))
            {
                List<object> list = new List<object>();
                foreach (object item in enumerable) list.Add(item);
                return list.ToArray();
            }
            return null;
        }
        private static string AsString(object value) { return value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture); }
        private static long AsLong(object value)
        {
            long result;
            if (value != null && long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out result)) return result;
            return 0;
        }
    }
}
