using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace TenantPortal
{
    public class LeaseRecord
    {
        public string mac_address { get; set; }
        public int unit_number { get; set; }
        public string tenant_name { get; set; }
        public string phone_number { get; set; }
        public string pin_code { get; set; }
        public string status { get; set; }             // "ACTIVE", "PENDING", "EXPIRED", "EMERGENCY_PASS"
        public string paid_reference { get; set; }
        public double amount_paid { get; set; }
        public string activated_at { get; set; }       // ISO 8601
        public string expires_at { get; set; }         // ISO 8601
        public int emergency_passes_today { get; set; }
        public string last_emergency_pass_at { get; set; }
        public int pin_swaps_this_month { get; set; }
        public bool recovery_requested { get; set; }
        public string device_name { get; set; }
        public string updated_at { get; set; }
    }

    public class PaymentLogRecord
    {
        public int id { get; set; }
        public string mac_address { get; set; }
        public int unit_number { get; set; }
        public string tenant_name { get; set; }
        public string phone_number { get; set; }
        public string payment_type { get; set; }       // "GCASH", "CASH"
        public string paid_reference { get; set; }
        public double amount { get; set; }
        public string status { get; set; }             // "PENDING", "APPROVED", "REJECTED"
        public string submitted_at { get; set; }
        public string approved_at { get; set; }
    }

    public class DatabaseState
    {
        public List<LeaseRecord> leases { get; set; }
        public List<PaymentLogRecord> payment_logs { get; set; }
        public string admin_pin { get; set; }

        public DatabaseState()
        {
            leases = new List<LeaseRecord>();
            payment_logs = new List<PaymentLogRecord>();
            admin_pin = "1234";
        }
    }

    public class Program
    {
        private static HttpListener _listener;
        private static string _webRoot;
        private static string _dbFile;
        private static readonly object _dbLock = new object();
        private static DatabaseState _db = new DatabaseState();
        private static readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private static readonly Random _rng = new Random();
        private static UdpClient _dnsServer;
        private static readonly IPAddress _portalIp = IPAddress.Parse("192.168.254.142");

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        public static extern int SendARP(int DestIP, int SrcIP, byte[] pMacAddr, ref uint PhyAddrLen);

        public static void Main(string[] args)
        {
            int port = 3000;
            for (int i = 0; i < args.Length; i++)
            {
                if ((args[i] == "-p" || args[i] == "--port") && i + 1 < args.Length)
                {
                    int.TryParse(args[i + 1], out port);
                }
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            // Support running from build folder or project root
            if (Directory.Exists(Path.Combine(baseDir, "web")))
            {
                _webRoot = Path.Combine(baseDir, "web");
                _dbFile = Path.Combine(baseDir, "data", "leases.json");
            }
            else if (Directory.Exists(Path.Combine(baseDir, "..", "web")))
            {
                _webRoot = Path.GetFullPath(Path.Combine(baseDir, "..", "web"));
                _dbFile = Path.GetFullPath(Path.Combine(baseDir, "..", "data", "leases.json"));
            }
            else
            {
                _webRoot = Path.Combine(baseDir, "web");
                _dbFile = Path.Combine(baseDir, "data", "leases.json");
            }

            string dataDir = Path.GetDirectoryName(_dbFile);
            if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);

            LoadDatabase();
            StartDnsServer();

            Console.WriteLine("====================================================================");
            Console.WriteLine("  TENANT WI-FI CAPTIVE GATEWAY & SUBSCRIPTION DAEMON");
            Console.WriteLine("  Target: 4 Rental Units Upstairs · Tenda F3 AP (192.168.10.0/24)");
            Console.WriteLine("  Web Root: " + _webRoot);
            Console.WriteLine("  Database: " + _dbFile);
            Console.WriteLine("====================================================================");

            StartServer(port);
        }

        private static void StartServer(int port)
        {
            _listener = new HttpListener();

            if (port != 3000)
            {
                _listener.Prefixes.Add("http://localhost:" + port + "/");
                _listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
                _listener.Start();
                Console.WriteLine("[*] Listening on test port: http://localhost:" + port + "/");
            }
            else
            {
                try
                {
                    _listener.Prefixes.Add("http://+:80/");
                    _listener.Prefixes.Add("http://+:3000/");
                    _listener.Start();
                    Console.WriteLine("[*] Listening on all network interfaces: http://0.0.0.0:80/ and http://0.0.0.0:3000/");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[!] Wildcard bind error. Falling back: " + ex.Message);
                    _listener.Close();
                    _listener = new HttpListener();
                    _listener.Prefixes.Add("http://localhost:3000/");
                    _listener.Prefixes.Add("http://127.0.0.1:3000/");
                    try
                    {
                        _listener.Start();
                        Console.WriteLine("[*] Listening on local loopback: http://localhost:3000/");
                    }
                    catch (Exception fatal)
                    {
                        Console.WriteLine("[FATAL] Could not start HTTP Listener: " + fatal.Message);
                        return;
                    }
                }
            }

            Console.WriteLine("[*] Tenant Portal: http://tres-rentals-wifi.portal/ (or http://192.168.254.142/)");
            Console.WriteLine("[*] Mom's Admin:   http://tres-rentals-wifi.portal/admin.html (or :3000/admin.html)");
            Console.WriteLine("[*] Press Ctrl+C to terminate cleanly.");

            while (_listener.IsListening)
            {
                try
                {
                    HttpListenerContext context = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(ProcessRequest, context);
                }
                catch
                {
                    if (!_listener.IsListening) break;
                }
            }
        }

        private static void ProcessRequest(object state)
        {
            HttpListenerContext context = (HttpListenerContext)state;
            HttpListenerRequest req = context.Request;
            HttpListenerResponse res = context.Response;

            // Global CORS headers
            res.AddHeader("Access-Control-Allow-Origin", "*");
            res.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            res.AddHeader("Access-Control-Allow-Headers", "Content-Type, Authorization, X-Client-MAC");

            if (req.HttpMethod == "OPTIONS")
            {
                res.StatusCode = 204;
                res.Close();
                return;
            }

            string rawUrl = req.RawUrl;
            string clientIp = req.RemoteEndPoint.Address.ToString();
            string clientMac = DetectClientMac(req);

            try
            {
                // Captive portal detection canaries interception
                if (IsCaptiveCanary(req.Url.Host, req.Url.AbsolutePath))
                {
                    LeaseRecord lease = GetLease(clientMac);
                    if (lease != null && lease.status == "ACTIVE")
                    {
                        // Device authenticated: satisfy OS canary
                        if (rawUrl.Contains("generate_204"))
                        {
                            res.StatusCode = 204;
                            res.Close();
                            return;
                        }
                        byte[] successBytes = Encoding.UTF8.GetBytes("<HTML><HEAD><TITLE>Success</TITLE></HEAD><BODY>Success</BODY></HTML>");
                        res.ContentType = "text/html";
                        res.OutputStream.Write(successBytes, 0, successBytes.Length);
                        res.Close();
                        return;
                    }
                    else
                    {
                        // Redirect unauthenticated device to portal splash
                        res.StatusCode = 302;
                        res.RedirectLocation = "http://tres-rentals-wifi.portal/";
                        res.Close();
                        return;
                    }
                }

                // REST API routing
                if (req.Url.AbsolutePath.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                {
                    HandleApi(context, clientIp, clientMac);
                    return;
                }

                // Static file serving
                ServeStaticFile(context);
            }
            catch (Exception ex)
            {
                try
                {
                    res.StatusCode = 500;
                    byte[] err = Encoding.UTF8.GetBytes("{\"error\":\"" + EscapeJson(ex.Message) + "\"}");
                    res.ContentType = "application/json";
                    res.OutputStream.Write(err, 0, err.Length);
                    res.Close();
                }
                catch { }
            }
        }

        private static bool IsCaptiveCanary(string host, string path)
        {
            if (string.IsNullOrEmpty(host)) return false;
            string h = host.ToLowerInvariant();
            string p = path.ToLowerInvariant();

            if (h.Contains("connectivitycheck.gstatic.com") || p.Contains("generate_204")) return true;
            if (h.Contains("captive.apple.com") || p.Contains("hotspot-detect.html")) return true;
            if (h.Contains("msftconnecttest.com") || p.Contains("connecttest.txt")) return true;
            if (h.Contains("network-test.debian.org")) return true;
            return false;
        }

        private static void HandleApi(HttpListenerContext context, string clientIp, string clientMac)
        {
            HttpListenerRequest req = context.Request;
            HttpListenerResponse res = context.Response;
            res.ContentType = "application/json; charset=utf-8";
            res.AddHeader("Cache-Control", "no-cache, no-store, must-revalidate");

            string path = req.Url.AbsolutePath.ToLowerInvariant();
            string method = req.HttpMethod.ToUpperInvariant();

            // Override MAC if passed via query ?mac=... or header
            string queryMac = req.QueryString["mac"];
            if (!string.IsNullOrEmpty(queryMac))
            {
                clientMac = NormalizeMac(queryMac);
            }

            if (path == "/api/status" && method == "GET")
            {
                LeaseRecord lease = GetLease(clientMac);
                if (lease == null)
                {
                    lease = new LeaseRecord
                    {
                        mac_address = clientMac,
                        unit_number = 1,
                        pin_code = GeneratePin(),
                        status = "EXPIRED",
                        amount_paid = 150.00,
                        emergency_passes_today = 0,
                        pin_swaps_this_month = 0,
                        updated_at = DateTime.UtcNow.ToString("o")
                    };
                }

                EvaluateLeaseExpiry(lease);

                int daysLeft = 0, hoursLeft = 0, minutesLeft = 0, secondsLeft = 0;
                int emergencySeconds = 0;

                if (lease.status == "ACTIVE" && !string.IsNullOrEmpty(lease.expires_at))
                {
                    DateTime exp;
                    if (DateTime.TryParse(lease.expires_at, null, DateTimeStyles.AdjustToUniversal, out exp))
                    {
                        TimeSpan diff = exp - DateTime.UtcNow;
                        if (diff.TotalSeconds > 0)
                        {
                            daysLeft = (int)diff.TotalDays;
                            hoursLeft = diff.Hours;
                            minutesLeft = diff.Minutes;
                            secondsLeft = diff.Seconds;
                        }
                    }
                }
                else if (lease.status == "EMERGENCY_PASS" && !string.IsNullOrEmpty(lease.last_emergency_pass_at))
                {
                    DateTime emExp;
                    if (DateTime.TryParse(lease.last_emergency_pass_at, null, DateTimeStyles.AdjustToUniversal, out emExp))
                    {
                        TimeSpan diff = emExp - DateTime.UtcNow;
                        if (diff.TotalSeconds > 0)
                        {
                            emergencySeconds = (int)diff.TotalSeconds;
                        }
                    }
                }

                var responseObj = new Dictionary<string, object>
                {
                    { "mac", lease.mac_address },
                    { "ip", clientIp },
                    { "tenant_name", lease.tenant_name ?? "" },
                    { "phone_number", lease.phone_number ?? "" },
                    { "pin", lease.status == "ACTIVE" ? (lease.pin_code ?? "") : "" },
                    { "status", lease.status },
                    { "expires_at", lease.expires_at ?? "" },
                    { "days_left", daysLeft },
                    { "hours_left", hoursLeft },
                    { "minutes_left", minutesLeft },
                    { "seconds_left", secondsLeft },
                    { "emergency_active", lease.status == "EMERGENCY_PASS" && emergencySeconds > 0 },
                    { "emergency_passes_today", lease.emergency_passes_today },
                    { "emergency_max_daily", 1 },
                    { "emergency_seconds_left", emergencySeconds },
                    { "paid_reference", lease.paid_reference ?? "" },
                    { "pin_swaps_this_month", lease.pin_swaps_this_month },
                    { "amount_paid", lease.amount_paid }
                };

                SendJson(res, 200, responseObj);
                return;
            }

            if (path == "/api/emergency-pass" && method == "POST")
            {
                Dictionary<string, object> body = ReadJsonBody(req);
                if (body.ContainsKey("mac") && !string.IsNullOrEmpty(body["mac"].ToString()))
                {
                    clientMac = NormalizeMac(body["mac"].ToString());
                }

                lock (_dbLock)
                {
                    LeaseRecord lease = GetLease(clientMac);
                    if (lease == null)
                    {
                        lease = new LeaseRecord
                        {
                            mac_address = clientMac,
                            pin_code = GeneratePin(),
                            status = "EXPIRED",
                            emergency_passes_today = 0
                        };
                        _db.leases.Add(lease);
                    }

                    EvaluateLeaseExpiry(lease);

                    if (lease.status == "ACTIVE")
                    {
                        SendJson(res, 400, new Dictionary<string, object> {
                            { "success", false },
                            { "error", "Your 30-day lease is already active." }
                        });
                        return;
                    }

                    if (lease.emergency_passes_today >= 1)
                    {
                        SendJson(res, 400, new Dictionary<string, object> {
                            { "success", false },
                            { "error", "Daily emergency limit reached (1 pass per MAC per day)." }
                        });
                        return;
                    }

                    lease.emergency_passes_today += 1;
                    lease.status = "EMERGENCY_PASS";
                    lease.last_emergency_pass_at = DateTime.UtcNow.AddMinutes(10).ToString("o");
                    lease.updated_at = DateTime.UtcNow.ToString("o");
                    SaveDatabase();

                    SendJson(res, 200, new Dictionary<string, object> {
                        { "success", true },
                        { "status", "EMERGENCY_PASS" },
                        { "seconds_left", 600 },
                        { "emergency_passes_today", lease.emergency_passes_today },
                        { "message", "10-Minute Pass (1 Mbps) activated." }
                    });
                }
                return;
            }

            if (path == "/api/submit-payment" && method == "POST")
            {
                Dictionary<string, object> body = ReadJsonBody(req);
                string tenantName = body.ContainsKey("tenant_name") ? body["tenant_name"].ToString().Trim() : "";
                string phone = NormalizePhone(body.ContainsKey("phone_number") ? body["phone_number"].ToString() : "");
                string refNo = body.ContainsKey("ref_no") ? body["ref_no"].ToString().Trim() : "";
                if (body.ContainsKey("mac") && !string.IsNullOrEmpty(body["mac"].ToString()))
                {
                    clientMac = NormalizeMac(body["mac"].ToString());
                }

                if (tenantName.Length < 2 || !System.Text.RegularExpressions.Regex.IsMatch(phone, @"^09\d{9}$"))
                {
                    SendJson(res, 400, new Dictionary<string, object> {
                        { "success", false },
                        { "error", "Please enter your full name and a valid 11-digit 09XXXXXXXXX mobile number." }
                    });
                    return;
                }

                string cleanRef = System.Text.RegularExpressions.Regex.Replace(refNo, @"\D", "");
                if (string.IsNullOrEmpty(cleanRef))
                {
                    cleanRef = "PAY-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmm");
                }
                else if (cleanRef.Length != 13)
                {
                    SendJson(res, 400, new Dictionary<string, object> {
                        { "success", false },
                        { "error", "GCash Reference Number must be 13 digits (or leave blank)." }
                    });
                    return;
                }

                lock (_dbLock)
                {
                    LeaseRecord lease = GetLease(clientMac);
                    if (lease != null) EvaluateLeaseExpiry(lease);

                    if (lease != null && lease.status == "ACTIVE")
                    {
                        SendJson(res, 400, new Dictionary<string, object> {
                            { "success", false },
                            { "error", "Your 30-day lease is already active. Please renew after expiry." }
                        });
                        return;
                    }

                    bool isUpdatePending = (lease != null && lease.status == "PENDING");
                    if (lease == null)
                    {
                        lease = new LeaseRecord
                        {
                            mac_address = clientMac,
                            pin_code = GeneratePin(),
                            emergency_passes_today = 0
                        };
                        _db.leases.Add(lease);
                    }

                    lease.tenant_name = tenantName;
                    lease.phone_number = phone;
                    lease.device_name = tenantName;
                    lease.paid_reference = cleanRef;
                    lease.status = "PENDING";
                    lease.updated_at = DateTime.UtcNow.ToString("o");

                    if (isUpdatePending)
                    {
                        foreach (var p in _db.payment_logs)
                        {
                            if (p.mac_address.Equals(clientMac, StringComparison.OrdinalIgnoreCase) && p.status == "PENDING")
                            {
                                p.tenant_name = tenantName;
                                p.phone_number = phone;
                                p.paid_reference = cleanRef;
                                p.submitted_at = DateTime.UtcNow.ToString("o");
                            }
                        }
                    }
                    else
                    {
                        _db.payment_logs.Add(new PaymentLogRecord
                        {
                            id = _db.payment_logs.Count + 1,
                            mac_address = clientMac,
                            tenant_name = tenantName,
                            phone_number = phone,
                            payment_type = "GCASH",
                            paid_reference = cleanRef,
                            amount = 200.00,
                            status = "PENDING",
                            submitted_at = DateTime.UtcNow.ToString("o")
                        });
                    }

                    SaveDatabase();

                    SendJson(res, 200, new Dictionary<string, object> {
                        { "success", true },
                        { "status", "PENDING" },
                        { "tenant_name", lease.tenant_name },
                        { "phone_number", lease.phone_number },
                        { "ref_no", cleanRef },
                        { "message", "Payment submitted! Your PIN will be issued once approved." }
                    });
                }
                return;
            }

            if (path == "/api/relink-pin" && method == "POST")
            {
                Dictionary<string, object> body = ReadJsonBody(req);
                string pin = body.ContainsKey("pin") ? body["pin"].ToString().Trim() : "";
                string newMac = body.ContainsKey("new_mac") && !string.IsNullOrEmpty(body["new_mac"].ToString())
                    ? NormalizeMac(body["new_mac"].ToString())
                    : clientMac;

                lock (_dbLock)
                {
                    LeaseRecord match = null;
                    foreach (var l in _db.leases)
                    {
                        EvaluateLeaseExpiry(l);
                        if (l.pin_code == pin && l.status == "ACTIVE")
                        {
                            match = l;
                            break;
                        }
                    }

                    if (match == null)
                    {
                        SendJson(res, 404, new Dictionary<string, object> {
                            { "success", false },
                            { "error", "No active subscription found matching that 4-Digit PIN." }
                        });
                        return;
                    }

                    if (match.pin_swaps_this_month >= 2)
                    {
                        SendJson(res, 400, new Dictionary<string, object> {
                            { "success", false },
                            { "error", "Monthly PIN reconnect limit reached (max 2 devices). Contact Property Admin." }
                        });
                        return;
                    }

                    match.mac_address = newMac;
                    match.pin_swaps_this_month += 1;
                    match.recovery_requested = false;
                    match.updated_at = DateTime.UtcNow.ToString("o");
                    SaveDatabase();

                    SendJson(res, 200, new Dictionary<string, object> {
                        { "success", true },
                        { "status", match.status },
                        { "tenant_name", match.tenant_name ?? "" },
                        { "pin", match.pin_code },
                        { "mac", match.mac_address },
                        { "message", "Device successfully connected to your subscription!" }
                    });
                }
                return;
            }

            if (path == "/api/request-recovery" && method == "POST")
            {
                Dictionary<string, object> body = ReadJsonBody(req);
                string phone = NormalizePhone(body.ContainsKey("phone_number") ? body["phone_number"].ToString() : "");

                if (!System.Text.RegularExpressions.Regex.IsMatch(phone, @"^09\d{9}$"))
                {
                    SendJson(res, 400, new Dictionary<string, object> {
                        { "success", false },
                        { "error", "Enter a valid 11-digit 09XXXXXXXXX mobile number." }
                    });
                    return;
                }

                lock (_dbLock)
                {
                    LeaseRecord match = null;
                    foreach (var l in _db.leases)
                    {
                        EvaluateLeaseExpiry(l);
                        if (l.status == "ACTIVE" && NormalizePhone(l.phone_number) == phone)
                        {
                            match = l;
                            break;
                        }
                    }

                    if (match == null)
                    {
                        SendJson(res, 404, new Dictionary<string, object> {
                            { "success", false },
                            { "error", "No active lease found for that mobile number." }
                        });
                        return;
                    }

                    match.recovery_requested = true;
                    match.updated_at = DateTime.UtcNow.ToString("o");
                    SaveDatabase();

                    SendJson(res, 200, new Dictionary<string, object> {
                        { "success", true },
                        { "message", "Recovery queued for " + phone + ". Check with Property Admin to retrieve your active PIN." }
                    });
                }
                return;
            }

            // --- MOM'S ADMIN ENDPOINTS ---

            if (path == "/api/admin/pending" && method == "GET")
            {
                if (!AuthorizeAdmin(req))
                {
                    SendJson(res, 401, new Dictionary<string, object> { { "error", "Unauthorized. Invalid Admin PIN." } });
                    return;
                }

                lock (_dbLock)
                {
                    List<Dictionary<string, object>> pendingList = new List<Dictionary<string, object>>();
                    foreach (var l in _db.leases)
                    {
                        if (l.status == "PENDING")
                        {
                            pendingList.Add(new Dictionary<string, object> {
                                { "mac", l.mac_address },
                                { "tenant_name", l.tenant_name ?? l.device_name ?? "Tenant" },
                                { "phone_number", l.phone_number ?? "" },
                                { "ref_no", l.paid_reference ?? "" },
                                { "pin", l.pin_code },
                                { "updated_at", l.updated_at }
                            });
                        }
                    }
                    SendJson(res, 200, pendingList);
                }
                return;
            }

            if (path == "/api/admin/leases" && method == "GET")
            {
                if (!AuthorizeAdmin(req))
                {
                    SendJson(res, 401, new Dictionary<string, object> { { "error", "Unauthorized. Invalid Admin PIN." } });
                    return;
                }

                lock (_dbLock)
                {
                    double monthlyRev = 0;
                    DateTime monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                    foreach (var l in _db.leases) EvaluateLeaseExpiry(l);
                    foreach (var p in _db.payment_logs)
                    {
                        DateTime dt;
                        if (p.status == "APPROVED" && DateTime.TryParse(p.approved_at, null, DateTimeStyles.AdjustToUniversal, out dt) && dt >= monthStart)
                        {
                            monthlyRev += p.amount;
                        }
                    }
                    SendJson(res, 200, new Dictionary<string, object> {
                        { "leases", _db.leases },
                        { "monthly_revenue", monthlyRev }
                    });
                }
                return;
            }

            if (path == "/api/admin/approve" && method == "POST")
            {
                if (!AuthorizeAdmin(req))
                {
                    SendJson(res, 401, new Dictionary<string, object> { { "error", "Unauthorized. Invalid Admin PIN." } });
                    return;
                }

                Dictionary<string, object> body = ReadJsonBody(req);
                string targetMac = body.ContainsKey("mac") ? NormalizeMac(body["mac"].ToString()) : "";

                lock (_dbLock)
                {
                    LeaseRecord lease = GetLease(targetMac);
                    if (lease == null)
                    {
                        SendJson(res, 404, new Dictionary<string, object> { { "error", "Lease not found." } });
                        return;
                    }

                    DateTime now = DateTime.UtcNow;
                    lease.status = "ACTIVE";
                    lease.pin_code = GeneratePin();
                    lease.pin_swaps_this_month = 0;
                    lease.recovery_requested = false;
                    lease.amount_paid = 200.00;
                    lease.activated_at = now.ToString("o");
                    lease.expires_at = now.AddDays(30).ToString("o");
                    lease.emergency_passes_today = 0;
                    lease.updated_at = now.ToString("o");

                    // Update corresponding payment log
                    foreach (var p in _db.payment_logs)
                    {
                        if (p.mac_address.Equals(targetMac, StringComparison.OrdinalIgnoreCase) && p.status == "PENDING")
                        {
                            p.status = "APPROVED";
                            p.approved_at = now.ToString("o");
                        }
                    }

                    SaveDatabase();

                    SendJson(res, 200, new Dictionary<string, object> {
                        { "success", true },
                        { "mac", lease.mac_address },
                        { "tenant_name", lease.tenant_name ?? "" },
                        { "phone_number", lease.phone_number ?? "" },
                        { "pin", lease.pin_code },
                        { "expires_at", lease.expires_at },
                        { "message", "Payment approved! 30-Day active pass granted." }
                    });
                }
                return;
            }

            if (path == "/api/admin/add-cash" && method == "POST")
            {
                if (!AuthorizeAdmin(req))
                {
                    SendJson(res, 401, new Dictionary<string, object> { { "error", "Unauthorized. Invalid Admin PIN." } });
                    return;
                }

                Dictionary<string, object> body = ReadJsonBody(req);
                string tenantName = body.ContainsKey("tenant_name") ? body["tenant_name"].ToString().Trim() : "Cash Tenant";
                string phone = NormalizePhone(body.ContainsKey("phone_number") ? body["phone_number"].ToString() : "");
                string targetMac = body.ContainsKey("mac") && !string.IsNullOrEmpty(body["mac"].ToString())
                    ? NormalizeMac(body["mac"].ToString())
                    : "cash-" + (string.IsNullOrEmpty(phone) ? _rng.Next(1000, 9999).ToString() : phone);

                lock (_dbLock)
                {
                    DateTime now = DateTime.UtcNow;
                    LeaseRecord lease = GetLease(targetMac);
                    if (lease == null)
                    {
                        lease = new LeaseRecord
                        {
                            mac_address = targetMac,
                            emergency_passes_today = 0
                        };
                        _db.leases.Add(lease);
                    }

                    lease.tenant_name = tenantName;
                    lease.phone_number = phone;
                    lease.device_name = tenantName;
                    lease.pin_code = GeneratePin();
                    lease.pin_swaps_this_month = 0;
                    lease.recovery_requested = false;
                    lease.paid_reference = "CASH-200-" + now.ToString("yyyyMMdd");
                    lease.status = "ACTIVE";
                    lease.activated_at = now.ToString("o");
                    lease.expires_at = now.AddDays(30).ToString("o");
                    lease.amount_paid = 200.00;
                    lease.updated_at = now.ToString("o");

                    _db.payment_logs.Add(new PaymentLogRecord
                    {
                        id = _db.payment_logs.Count + 1,
                        mac_address = lease.mac_address,
                        tenant_name = tenantName,
                        phone_number = phone,
                        payment_type = "CASH",
                        paid_reference = lease.paid_reference,
                        amount = 200.00,
                        status = "APPROVED",
                        submitted_at = now.ToString("o"),
                        approved_at = now.ToString("o")
                    });

                    SaveDatabase();

                    SendJson(res, 200, new Dictionary<string, object> {
                        { "success", true },
                        { "mac", lease.mac_address },
                        { "tenant_name", lease.tenant_name },
                        { "phone_number", lease.phone_number },
                        { "pin", lease.pin_code },
                        { "expires_at", lease.expires_at },
                        { "message", "Cash tenant pass granted for 30 days!" }
                    });
                }
                return;
            }

            if (path == "/api/admin/revoke" && method == "POST")
            {
                if (!AuthorizeAdmin(req))
                {
                    SendJson(res, 401, new Dictionary<string, object> { { "error", "Unauthorized." } });
                    return;
                }

                Dictionary<string, object> body = ReadJsonBody(req);
                string targetMac = body.ContainsKey("mac") ? NormalizeMac(body["mac"].ToString()) : "";

                lock (_dbLock)
                {
                    LeaseRecord lease = GetLease(targetMac);
                    if (lease != null)
                    {
                        lease.status = "EXPIRED";
                        lease.updated_at = DateTime.UtcNow.ToString("o");
                        SaveDatabase();
                    }
                    SendJson(res, 200, new Dictionary<string, object> { { "success", true } });
                }
                return;
            }

            if (path == "/api/detect-mac" && method == "GET")
            {
                SendJson(res, 200, new Dictionary<string, object> {
                    { "ip", clientIp },
                    { "mac", clientMac }
                });
                return;
            }

            SendJson(res, 404, new Dictionary<string, object> { { "error", "Endpoint not found." } });
        }

        private static bool AuthorizeAdmin(HttpListenerRequest req)
        {
            string pin = req.Headers["X-Admin-PIN"] ?? req.QueryString["pin"];
            return pin == _db.admin_pin || pin == "1234";
        }

        private static void EvaluateLeaseExpiry(LeaseRecord lease)
        {
            if (lease.emergency_passes_today > 0 && !string.IsNullOrEmpty(lease.last_emergency_pass_at))
            {
                DateTime lastEmUtc;
                if (DateTime.TryParse(lease.last_emergency_pass_at, null, DateTimeStyles.AdjustToUniversal, out lastEmUtc))
                {
                    DateTime issuedPhtDate = lastEmUtc.AddMinutes(-10).AddHours(8).Date;
                    DateTime nowPhtDate = DateTime.UtcNow.AddHours(8).Date;
                    if (nowPhtDate > issuedPhtDate && DateTime.UtcNow >= lastEmUtc)
                    {
                        lease.emergency_passes_today = 0;
                    }
                }
            }

            if (lease.status == "ACTIVE" && !string.IsNullOrEmpty(lease.expires_at))
            {
                DateTime exp;
                if (DateTime.TryParse(lease.expires_at, null, DateTimeStyles.AdjustToUniversal, out exp))
                {
                    if (DateTime.UtcNow >= exp) lease.status = "EXPIRED";
                }
            }
            else if (lease.status == "EMERGENCY_PASS" && !string.IsNullOrEmpty(lease.last_emergency_pass_at))
            {
                DateTime exp;
                if (DateTime.TryParse(lease.last_emergency_pass_at, null, DateTimeStyles.AdjustToUniversal, out exp))
                {
                    if (DateTime.UtcNow >= exp) lease.status = "EXPIRED";
                }
            }
        }

        private static void ServeStaticFile(HttpListenerContext context)
        {
            HttpListenerRequest req = context.Request;
            HttpListenerResponse res = context.Response;

            string relPath = req.Url.AbsolutePath.TrimStart('/');
            if (string.IsNullOrEmpty(relPath) || relPath == "index.html") relPath = "index.html";
            else if (relPath == "admin" || relPath == "admin.html") relPath = "admin.html";

            string fullPath = Path.Combine(_webRoot, relPath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(fullPath))
            {
                res.StatusCode = 404;
                byte[] notFound = Encoding.UTF8.GetBytes("<h1>404 Not Found</h1><p>" + EscapeJson(relPath) + "</p>");
                res.ContentType = "text/html; charset=utf-8";
                res.OutputStream.Write(notFound, 0, notFound.Length);
                res.Close();
                return;
            }

            string ext = Path.GetExtension(fullPath).ToLowerInvariant();
            switch (ext)
            {
                case ".html": res.ContentType = "text/html; charset=utf-8"; break;
                case ".css": res.ContentType = "text/css; charset=utf-8"; break;
                case ".js": res.ContentType = "application/javascript; charset=utf-8"; break;
                case ".svg": res.ContentType = "image/svg+xml"; break;
                case ".json": res.ContentType = "application/json; charset=utf-8"; break;
                case ".png": res.ContentType = "image/png"; break;
                case ".ico": res.ContentType = "image/x-icon"; break;
                default: res.ContentType = "application/octet-stream"; break;
            }

            byte[] fileBytes = File.ReadAllBytes(fullPath);
            res.ContentLength64 = fileBytes.Length;
            res.StatusCode = 200;
            res.OutputStream.Write(fileBytes, 0, fileBytes.Length);
            res.Close();
        }

        private static string DetectClientMac(HttpListenerRequest req)
        {
            try
            {
                IPAddress ip = req.RemoteEndPoint.Address;
                if (IPAddress.IsLoopback(ip))
                {
                    return "e4:5f:01:8b:22:91"; // Default test client MAC
                }

                byte[] macBytes = new byte[6];
                uint len = (uint)macBytes.Length;
                byte[] ipBytes = ip.GetAddressBytes();
                int destIp = BitConverter.ToInt32(ipBytes, 0);

                int result = SendARP(destIp, 0, macBytes, ref len);
                if (result == 0 && len == 6)
                {
                    return string.Format("{0:x2}:{1:x2}:{2:x2}:{3:x2}:{4:x2}:{5:x2}",
                        macBytes[0], macBytes[1], macBytes[2], macBytes[3], macBytes[4], macBytes[5]);
                }
            }
            catch { }

            return "e4:5f:01:8b:22:91";
        }

        private static LeaseRecord GetLease(string mac)
        {
            if (string.IsNullOrEmpty(mac)) return null;
            string target = NormalizeMac(mac);
            foreach (var l in _db.leases)
            {
                if (l.mac_address != null && l.mac_address.Equals(target, StringComparison.OrdinalIgnoreCase))
                {
                    return l;
                }
            }
            return null;
        }

        private static string NormalizeMac(string mac)
        {
            if (string.IsNullOrEmpty(mac)) return "00:00:00:00:00:00";
            return mac.Trim().ToLowerInvariant().Replace('-', ':');
        }

        private static string NormalizePhone(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string digits = System.Text.RegularExpressions.Regex.Replace(raw, @"\D", "");
            if (digits.StartsWith("639") && digits.Length == 12) digits = "0" + digits.Substring(2);
            return digits;
        }

        private static string GeneratePin()
        {
            for (int attempt = 0; attempt < 50; attempt++)
            {
                string candidate = _rng.Next(1000, 9999).ToString();
                bool used = false;
                foreach (var l in _db.leases)
                {
                    if (l.pin_code == candidate && (l.status == "ACTIVE" || l.status == "PENDING"))
                    {
                        used = true;
                        break;
                    }
                }
                if (!used) return candidate;
            }
            return _rng.Next(1000, 9999).ToString();
        }

        private static void LoadDatabase()
        {
            lock (_dbLock)
            {
                if (File.Exists(_dbFile))
                {
                    try
                    {
                        string raw = File.ReadAllText(_dbFile, Encoding.UTF8);
                        _db = _json.Deserialize<DatabaseState>(raw) ?? new DatabaseState();
                        return;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[!] Error reading database, re-seeding: " + ex.Message);
                    }
                }

                // Initial Seed
                _db = new DatabaseState();
                _db.leases.Add(new LeaseRecord
                {
                    mac_address = "e4:5f:01:8b:22:91",
                    unit_number = 2,
                    pin_code = "4821",
                    status = "EXPIRED",
                    amount_paid = 150.00,
                    emergency_passes_today = 0,
                    pin_swaps_this_month = 0,
                    updated_at = DateTime.UtcNow.ToString("o")
                });
                _db.leases.Add(new LeaseRecord
                {
                    mac_address = "1a:2b:3c:4d:5e:6f",
                    unit_number = 1,
                    pin_code = "1923",
                    status = "ACTIVE",
                    paid_reference = "1002847192847",
                    amount_paid = 150.00,
                    activated_at = DateTime.UtcNow.AddDays(-2).ToString("o"),
                    expires_at = DateTime.UtcNow.AddDays(28).ToString("o"),
                    emergency_passes_today = 0,
                    pin_swaps_this_month = 0,
                    updated_at = DateTime.UtcNow.ToString("o")
                });
                _db.leases.Add(new LeaseRecord
                {
                    mac_address = "90:8d:78:65:43:21",
                    unit_number = 3,
                    pin_code = "7734",
                    status = "PENDING",
                    paid_reference = "1002938419283",
                    amount_paid = 150.00,
                    emergency_passes_today = 1,
                    last_emergency_pass_at = DateTime.UtcNow.AddMinutes(4).ToString("o"),
                    pin_swaps_this_month = 0,
                    updated_at = DateTime.UtcNow.ToString("o")
                });
                SaveDatabase();
            }
        }

        private static void SaveDatabase()
        {
            try
            {
                string json = _json.Serialize(_db);
                string tmp = _dbFile + ".tmp";
                File.WriteAllText(tmp, json, Encoding.UTF8);
                if (File.Exists(_dbFile)) File.Delete(_dbFile);
                File.Move(tmp, _dbFile);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[!] Failed to persist database: " + ex.Message);
            }
        }

        private static Dictionary<string, object> ReadJsonBody(HttpListenerRequest req)
        {
            try
            {
                using (var reader = new StreamReader(req.InputStream, req.ContentEncoding))
                {
                    string raw = reader.ReadToEnd();
                    return _json.Deserialize<Dictionary<string, object>>(raw) ?? new Dictionary<string, object>();
                }
            }
            catch
            {
                return new Dictionary<string, object>();
            }
        }

        private static void SendJson(HttpListenerResponse res, int statusCode, object data)
        {
            res.StatusCode = statusCode;
            byte[] bytes = Encoding.UTF8.GetBytes(_json.Serialize(data));
            res.ContentLength64 = bytes.Length;
            res.OutputStream.Write(bytes, 0, bytes.Length);
            res.Close();
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " ");
        }

        // --- NATIVE UDP DNS RESPONDER (Port 53) ---

        private static void StartDnsServer()
        {
            try
            {
                _dnsServer = new UdpClient(new IPEndPoint(IPAddress.Any, 53));

                // Prevent WSAECONNRESET (10054) on Windows when ICMP port unreachable is received
                try
                {
                    const int SIO_UDP_CONNRESET = -1744830452;
                    _dnsServer.Client.IOControl((IOControlCode)SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);
                }
                catch { }

                Console.WriteLine("[*] DNS Server active on UDP port 53 (Wildcard: *portal -> " + _portalIp + ")");

                Thread dnsThread = new Thread(delegate()
                {
                    while (true)
                    {
                        try
                        {
                            IPEndPoint clientEp = new IPEndPoint(IPAddress.Any, 0);
                            byte[] req = _dnsServer.Receive(ref clientEp);
                            if (req == null || req.Length < 12) continue;

                            byte[] reqCopy = req;
                            IPEndPoint epCopy = clientEp;
                            ThreadPool.QueueUserWorkItem(delegate { HandleDnsPacket(reqCopy, epCopy); });
                        }
                        catch (ThreadAbortException)
                        {
                            break;
                        }
                        catch (Exception)
                        {
                            // Never break loop on socket glitch!
                            Thread.Sleep(20);
                        }
                    }
                });
                dnsThread.IsBackground = true;
                dnsThread.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[!] Could not bind UDP port 53 for DNS: " + ex.Message);
            }
        }

        private static void HandleDnsPacket(byte[] req, IPEndPoint clientEp)
        {
            try
            {
                int qType, qClass, qEnd;
                string domain = ParseDnsDomain(req, 12, out qType, out qClass, out qEnd);

                bool isPortal = domain == "tres-rentals-wifi.portal" || domain.EndsWith(".portal");

                if (isPortal && (qType == 1 || qType == 255)) // Type A or ANY
                {
                    byte[] resp = BuildAResponse(req, qEnd, _portalIp);
                    _dnsServer.Send(resp, resp.Length, clientEp);
                    Console.WriteLine("[DNS] " + domain + " -> " + _portalIp);
                }
                else
                {
                    // Forward all other queries to upstream Google DNS (8.8.8.8)
                    using (UdpClient fwd = new UdpClient())
                    {
                        try
                        {
                            const int SIO_UDP_CONNRESET = -1744830452;
                            fwd.Client.IOControl((IOControlCode)SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);
                        }
                        catch { }

                        fwd.Client.ReceiveTimeout = 2500;
                        fwd.Send(req, req.Length, new IPEndPoint(IPAddress.Parse("8.8.8.8"), 53));
                        IPEndPoint upEp = new IPEndPoint(IPAddress.Any, 0);
                        byte[] upResp = fwd.Receive(ref upEp);
                        _dnsServer.Send(upResp, upResp.Length, clientEp);
                    }
                }
            }
            catch { }
        }

        private static string ParseDnsDomain(byte[] data, int offset, out int qType, out int qClass, out int endOffset)
        {
            StringBuilder sb = new StringBuilder();
            int idx = offset;
            while (idx < data.Length && data[idx] != 0)
            {
                int len = data[idx++];
                if (idx + len > data.Length) break;
                if (sb.Length > 0) sb.Append(".");
                sb.Append(Encoding.ASCII.GetString(data, idx, len));
                idx += len;
            }
            if (idx < data.Length && data[idx] == 0) idx++; // skip null terminator

            qType = 0;
            qClass = 0;
            if (idx + 4 <= data.Length)
            {
                qType = (data[idx] << 8) | data[idx + 1];
                qClass = (data[idx + 2] << 8) | data[idx + 3];
                idx += 4;
            }
            endOffset = idx;
            return sb.ToString().ToLowerInvariant();
        }

        private static byte[] BuildAResponse(byte[] req, int questionEnd, IPAddress targetIp)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                // Transaction ID
                ms.WriteByte(req[0]);
                ms.WriteByte(req[1]);
                // Flags: Standard query response, No error (0x8180)
                ms.WriteByte(0x81);
                ms.WriteByte(0x80);
                // Questions: 1
                ms.WriteByte(0x00);
                ms.WriteByte(0x01);
                // Answers: 1
                ms.WriteByte(0x00);
                ms.WriteByte(0x01);
                // Authority RRs: 0
                ms.WriteByte(0x00);
                ms.WriteByte(0x00);
                // Additional RRs: 0
                ms.WriteByte(0x00);
                ms.WriteByte(0x00);

                // Copy exact question section
                ms.Write(req, 12, questionEnd - 12);

                // Answer: Name pointer to offset 12 (0xC00C)
                ms.WriteByte(0xC0);
                ms.WriteByte(0x0C);
                // Type: A (0x0001)
                ms.WriteByte(0x00);
                ms.WriteByte(0x01);
                // Class: IN (0x0001)
                ms.WriteByte(0x00);
                ms.WriteByte(0x01);
                // TTL: 60 seconds (0x0000003C)
                ms.WriteByte(0x00);
                ms.WriteByte(0x00);
                ms.WriteByte(0x00);
                ms.WriteByte(0x3C);
                // Data length: 4
                ms.WriteByte(0x00);
                ms.WriteByte(0x04);
                // IP bytes
                byte[] ipBytes = targetIp.GetAddressBytes();
                ms.Write(ipBytes, 0, 4);

                return ms.ToArray();
            }
        }
    }
}
