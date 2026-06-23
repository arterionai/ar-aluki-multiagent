using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Aluki.Runtime.Functions.Admin;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Aluki.Runtime.Functions.Functions;

public sealed class AdminFunctions
{
    private readonly IConfiguration _config;
    private readonly ILogger<AdminFunctions> _logger;
    private readonly IHttpClientFactory _httpFactory;

    public AdminFunctions(IConfiguration config, ILogger<AdminFunctions> logger, IHttpClientFactory httpFactory)
    {
        _config = config;
        _logger = logger;
        _httpFactory = httpFactory;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private NpgsqlConnection OpenConnection()
    {
        var connStr = _config["Postgres:AppConnection"]
                   ?? _config["Postgres__AppConnection"]
                   ?? throw new InvalidOperationException("Postgres connection string not configured.");
        var conn = new NpgsqlConnection(connStr);
        conn.Open();
        return conn;
    }

    private static void AddCorsHeaders(HttpResponseData resp)
    {
        resp.Headers.TryAddWithoutValidation("Access-Control-Allow-Origin", "*");
        resp.Headers.TryAddWithoutValidation("Access-Control-Allow-Methods", "GET, OPTIONS");
        resp.Headers.TryAddWithoutValidation("Access-Control-Allow-Headers", "Authorization, Content-Type");
    }

    private static HttpResponseData Unauthorized(HttpRequestData req)
    {
        var resp = req.CreateResponse(System.Net.HttpStatusCode.Unauthorized);
        AddCorsHeaders(resp);
        resp.WriteString("Unauthorized");
        return resp;
    }

    private static HttpResponseData JsonOk(HttpRequestData req, object data)
    {
        var resp = req.CreateResponse(System.Net.HttpStatusCode.OK);
        AddCorsHeaders(resp);
        resp.Headers.Add("Content-Type", "application/json; charset=utf-8");
        resp.WriteString(JsonSerializer.Serialize(data, JsonOptions));
        return resp;
    }

    [Function("AdminOverview")]
    public async Task<HttpResponseData> GetOverview(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dashboard/overview")] HttpRequestData req)
    {
        if (!await AdminTokenValidator.IsValidAsync(req.Headers.TryGetValues("Authorization", out var h) ? h.FirstOrDefault() : null, _config, _logger))
            return Unauthorized(req);

        try
        {
            await using var conn = OpenConnection();

            // Messages last 24h, 7d, 30d
            await using var msgCmd = new NpgsqlCommand("""
                SELECT
                    COUNT(*) FILTER (WHERE recorded_at >= NOW() - INTERVAL '24 hours') AS messages_24h,
                    COUNT(*) FILTER (WHERE recorded_at >= NOW() - INTERVAL '7 days')  AS messages_7d,
                    COUNT(*) FILTER (WHERE recorded_at >= NOW() - INTERVAL '30 days') AS messages_30d
                FROM app.unified_message_artifact
                """, conn);

            long messages24h = 0, messages7d = 0, messages30d = 0;
            try
            {
                await using var msgReader = await msgCmd.ExecuteReaderAsync();
                if (await msgReader.ReadAsync())
                {
                    messages24h = msgReader.GetInt64(0);
                    messages7d  = msgReader.GetInt64(1);
                    messages30d = msgReader.GetInt64(2);
                }
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01") { /* table not yet migrated */ }

            // Active tenants last 30d
            long activeTenants = 0;
            try
            {
                await using var tenantCmd = new NpgsqlCommand("""
                    SELECT COUNT(DISTINCT tenant_id) FROM app.unified_message_artifact
                    WHERE recorded_at >= NOW() - INTERVAL '30 days'
                    """, conn);
                activeTenants = (long)(await tenantCmd.ExecuteScalarAsync() ?? 0L);
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01") { }

            // Outbound messages last 30d
            long outboundMessages = 0;
            try
            {
                await using var outCmd = new NpgsqlCommand("""
                    SELECT COUNT(*) FROM app.outbound_messages
                    WHERE created_at_utc >= NOW() - INTERVAL '30 days'
                    """, conn);
                outboundMessages = (long)(await outCmd.ExecuteScalarAsync() ?? 0L);
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01") { }

            // Total distinct users (unique phones) and new users today
            long totalUsers = 0;
            long newUsersToday = 0;
            try
            {
                await using var usersCmd = new NpgsqlCommand("""
                    SELECT
                        COUNT(DISTINCT u.id)                                                         AS total_users,
                        COUNT(DISTINCT u.id) FILTER (WHERE u.created_at >= CURRENT_DATE::timestamptz) AS new_today
                    FROM app.users_profile u
                    WHERE u.phone IS NOT NULL
                    """, conn);
                await using var usersReader = await usersCmd.ExecuteReaderAsync();
                if (await usersReader.ReadAsync())
                {
                    totalUsers    = usersReader.GetInt64(0);
                    newUsersToday = usersReader.GetInt64(1);
                }
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01")
            {
                // Fall back to distinct senders in message table if users_profile not available
                try
                {
                    await using var fallbackCmd = new NpgsqlCommand("""
                        SELECT
                            COUNT(DISTINCT created_by_user_id)                                                              AS total_users,
                            COUNT(DISTINCT created_by_user_id) FILTER (WHERE recorded_at >= CURRENT_DATE::timestamptz)  AS new_today
                        FROM app.unified_message_artifact
                        """, conn);
                    await using var r = await fallbackCmd.ExecuteReaderAsync();
                    if (await r.ReadAsync())
                    {
                        totalUsers    = r.GetInt64(0);
                        newUsersToday = r.GetInt64(1);
                    }
                }
                catch (PostgresException) { }
            }

            return JsonOk(req, new
            {
                messages24h,
                messages7d,
                messages30d,
                activeTenants,
                outboundMessages,
                totalUsers,
                newUsersToday,
                generatedAt = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in AdminOverview");
            var err = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
            err.WriteString("Internal server error");
            return err;
        }
    }

    [Function("AdminAiCosts")]
    public async Task<HttpResponseData> GetAiCosts(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dashboard/ai-costs")] HttpRequestData req)
    {
        if (!await AdminTokenValidator.IsValidAsync(req.Headers.TryGetValues("Authorization", out var h) ? h.FirstOrDefault() : null, _config, _logger))
            return Unauthorized(req);

        try
        {
            var subscriptionId    = _config["Admin:SubscriptionId"]    ?? "33bbf1e1-134d-42e3-a370-0dfb1da16cff";
            var resourceGroupName = _config["Admin:ResourceGroupName"] ?? "ar-Aluki";
            var token = await GetAzureManagementTokenAsync();

            if (token == null)
            {
                _logger.LogWarning("AdminAiCosts: managed identity token unavailable");
                return JsonOk(req, new { byDay = Array.Empty<object>(), byService = Array.Empty<object>(), totalMtd = 0m, currency = "USD", managedIdentityUnavailable = true, generatedAt = DateTimeOffset.UtcNow });
            }

            using var client = _httpFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Scoped to the resource group so only costs for this app are returned.
            var queryUrl = $"https://management.azure.com/subscriptions/{subscriptionId}/resourceGroups/{resourceGroupName}/providers/Microsoft.CostManagement/query?api-version=2023-11-01";

            // Daily costs — last 30 days
            var from = DateTimeOffset.UtcNow.AddDays(-30).ToString("yyyy-MM-dd");
            var to   = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
            var dailyPayload = """
                {"type":"ActualCost","timeframe":"Custom","timePeriod":{"from":"FROM_DATE","to":"TO_DATE"},"dataset":{"granularity":"Daily","aggregation":{"totalCost":{"name":"Cost","function":"Sum"}}}}
                """.Replace("FROM_DATE", from).Replace("TO_DATE", to);

            var byDay = new List<object>();
            var currency = "USD";

            var dailyResp = await client.PostAsync(queryUrl, new StringContent(dailyPayload, Encoding.UTF8, "application/json"));
            if (dailyResp.IsSuccessStatusCode)
            {
                using var dailyDoc = JsonDocument.Parse(await dailyResp.Content.ReadAsStringAsync());
                var props = dailyDoc.RootElement.GetProperty("properties");
                var cols  = props.GetProperty("columns").EnumerateArray().Select((c, i) => (name: c.GetProperty("name").GetString()!, idx: i)).ToList();
                var costIdx = cols.First(c => c.name == "Cost").idx;
                var dateIdx = cols.First(c => c.name == "UsageDate").idx;
                var currIdx = cols.FirstOrDefault(c => c.name == "Currency").idx;

                foreach (var row in props.GetProperty("rows").EnumerateArray())
                {
                    var rawDate = row[dateIdx].GetInt64().ToString();
                    var day = $"{rawDate[..4]}-{rawDate[4..6]}-{rawDate[6..8]}";
                    var cost = row[costIdx].GetDecimal();
                    if (currIdx >= 0) currency = row[currIdx].GetString() ?? currency;
                    byDay.Add(new { day, cost });
                }
            }
            else
            {
                _logger.LogWarning("AdminAiCosts: daily cost query failed {Status}", dailyResp.StatusCode);
            }

            // By-service costs — month-to-date
            var servicePayload = """
                {"type":"ActualCost","timeframe":"MonthToDate","dataset":{"granularity":"None","aggregation":{"totalCost":{"name":"Cost","function":"Sum"}},"grouping":[{"type":"Dimension","name":"ServiceName"}]}}
                """;

            var byService = new List<object>();
            decimal totalMtd = 0m;

            var serviceResp = await client.PostAsync(queryUrl, new StringContent(servicePayload, Encoding.UTF8, "application/json"));
            if (serviceResp.IsSuccessStatusCode)
            {
                using var svcDoc = JsonDocument.Parse(await serviceResp.Content.ReadAsStringAsync());
                var props   = svcDoc.RootElement.GetProperty("properties");
                var cols    = props.GetProperty("columns").EnumerateArray().Select((c, i) => (name: c.GetProperty("name").GetString()!, idx: i)).ToList();
                var costIdx = cols.First(c => c.name == "Cost").idx;
                var nameIdx = cols.First(c => c.name == "ServiceName").idx;

                foreach (var row in props.GetProperty("rows").EnumerateArray())
                {
                    var svc  = row[nameIdx].GetString() ?? "Unknown";
                    var cost = row[costIdx].GetDecimal();
                    totalMtd += cost;
                    byService.Add(new { service = svc, cost });
                }

                byService = byService.OrderByDescending(o => ((dynamic)o).cost).ToList<object>();
            }
            else
            {
                _logger.LogWarning("AdminAiCosts: service cost query failed {Status}", serviceResp.StatusCode);
            }

            return JsonOk(req, new { byDay, byService, totalMtd, currency, generatedAt = DateTimeOffset.UtcNow });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in AdminAiCosts");
            var err = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
            err.WriteString("Internal server error");
            return err;
        }
    }

    private async Task<string?> GetAzureManagementTokenAsync()
    {
        var endpoint = Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT");
        var identityHeader = Environment.GetEnvironmentVariable("IDENTITY_HEADER");
        if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(identityHeader)) return null;

        try
        {
            using var client = _httpFactory.CreateClient();
            client.DefaultRequestHeaders.Add("X-IDENTITY-HEADER", identityHeader);
            var url = $"{endpoint}?api-version=2019-08-01&resource=https%3A%2F%2Fmanagement.azure.com%2F";
            var resp = await client.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return doc.RootElement.GetProperty("access_token").GetString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AdminFunctions: MSI token fetch failed");
            return null;
        }
    }

    [Function("AdminTenants")]
    public async Task<HttpResponseData> GetTenants(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dashboard/tenants")] HttpRequestData req)
    {
        if (!await AdminTokenValidator.IsValidAsync(req.Headers.TryGetValues("Authorization", out var h) ? h.FirstOrDefault() : null, _config, _logger))
            return Unauthorized(req);

        try
        {
            await using var conn = OpenConnection();
            var tenants = new List<object>();

            try
            {
                await using var cmd = new NpgsqlCommand("""
                    SELECT t.id, t.display_name, t.created_at,
                           COUNT(m.id) AS message_count,
                           MAX(m.recorded_at) AS last_activity
                    FROM app.tenant t
                    LEFT JOIN app.unified_message_artifact m ON m.tenant_id = t.id
                    GROUP BY t.id, t.display_name, t.created_at
                    ORDER BY last_activity DESC NULLS LAST
                    LIMIT 100
                    """, conn);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    tenants.Add(new
                    {
                        id = reader.GetGuid(0),
                        displayName = reader.IsDBNull(1) ? null : reader.GetString(1),
                        createdAt = reader.GetDateTime(2),
                        messageCount = reader.GetInt64(3),
                        lastActivity = reader.IsDBNull(4) ? (DateTime?)null : reader.GetDateTime(4)
                    });
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01")
            {
                // Fall back to just tenant table
                try
                {
                    await using var fallbackCmd = new NpgsqlCommand("SELECT id, display_name, created_at FROM app.tenant ORDER BY created_at DESC LIMIT 100", conn);
                    await using var reader = await fallbackCmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                        tenants.Add(new
                        {
                            id = reader.GetGuid(0),
                            displayName = reader.IsDBNull(1) ? null : reader.GetString(1),
                            createdAt = reader.GetDateTime(2),
                            messageCount = 0L,
                            lastActivity = (DateTime?)null
                        });
                }
                catch (PostgresException) { }
            }

            return JsonOk(req, new { tenants, generatedAt = DateTimeOffset.UtcNow });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in AdminTenants");
            var err = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
            err.WriteString("Internal server error");
            return err;
        }
    }

    [Function("AdminWhatsApp")]
    public async Task<HttpResponseData> GetWhatsApp(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dashboard/whatsapp")] HttpRequestData req)
    {
        if (!await AdminTokenValidator.IsValidAsync(req.Headers.TryGetValues("Authorization", out var h) ? h.FirstOrDefault() : null, _config, _logger))
            return Unauthorized(req);

        try
        {
            await using var conn = OpenConnection();
            var inboundByDay = new List<object>();
            var outboundByDay = new List<object>();
            var agentDistribution = new List<object>();

            try
            {
                await using var cmd = new NpgsqlCommand("""
                    SELECT DATE(recorded_at) AS day, COUNT(*) AS count
                    FROM app.unified_message_artifact
                    WHERE recorded_at >= NOW() - INTERVAL '30 days'
                    GROUP BY DATE(recorded_at) ORDER BY day
                    """, conn);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    inboundByDay.Add(new { day = r.GetDateTime(0).ToString("yyyy-MM-dd"), count = r.GetInt64(1) });
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01") { }

            try
            {
                await using var cmd = new NpgsqlCommand("""
                    SELECT DATE(created_at_utc) AS day, COUNT(*) AS count
                    FROM app.outbound_messages
                    WHERE created_at_utc >= NOW() - INTERVAL '30 days'
                    GROUP BY DATE(created_at_utc) ORDER BY day
                    """, conn);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    outboundByDay.Add(new { day = r.GetDateTime(0).ToString("yyyy-MM-dd"), count = r.GetInt64(1) });
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01") { }

            try
            {
                await using var cmd = new NpgsqlCommand("""
                    SELECT selected_agent_id, COUNT(*) AS count
                    FROM app.dispatch_audit_events
                    WHERE dispatched_at >= NOW() - INTERVAL '30 days'
                    GROUP BY selected_agent_id ORDER BY count DESC
                    """, conn);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    agentDistribution.Add(new { agent = r.IsDBNull(0) ? "fallback" : r.GetString(0), count = r.GetInt64(1) });
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01") { }

            return JsonOk(req, new { inboundByDay, outboundByDay, agentDistribution, generatedAt = DateTimeOffset.UtcNow });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in AdminWhatsApp");
            var err = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
            err.WriteString("Internal server error");
            return err;
        }
    }

    [Function("AdminBilling")]
    public async Task<HttpResponseData> GetBilling(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dashboard/billing")] HttpRequestData req)
    {
        if (!await AdminTokenValidator.IsValidAsync(req.Headers.TryGetValues("Authorization", out var h) ? h.FirstOrDefault() : null, _config, _logger))
            return Unauthorized(req);

        try
        {
            await using var conn = OpenConnection();
            var accountsByStatus = new List<object>();
            var revenueByDay = new List<object>();
            var subscriptionsByState = new List<object>();
            decimal totalCreditBalance = 0;

            try
            {
                await using var cmd = new NpgsqlCommand("""
                    SELECT billing_status, COUNT(*) FROM app.billing_accounts GROUP BY billing_status
                    """, conn);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    accountsByStatus.Add(new { status = r.GetString(0), count = r.GetInt64(1) });
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01") { }

            try
            {
                await using var cmd = new NpgsqlCommand("""
                    SELECT DATE(created_at) AS day, SUM(amount_usd) AS revenue
                    FROM app.billing_ledger_entries
                    WHERE created_at >= NOW() - INTERVAL '30 days'
                    GROUP BY DATE(created_at) ORDER BY day
                    """, conn);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    revenueByDay.Add(new { day = r.GetDateTime(0).ToString("yyyy-MM-dd"), revenue = r.GetDecimal(1) });
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01") { }

            try
            {
                await using var cmd = new NpgsqlCommand("""
                    SELECT state, COUNT(*) FROM app.package_subscriptions GROUP BY state
                    """, conn);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    subscriptionsByState.Add(new { state = r.GetString(0), count = r.GetInt64(1) });
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01") { }

            try
            {
                await using var cmd = new NpgsqlCommand("SELECT COALESCE(SUM(balance_usd), 0) FROM app.credit_balances", conn);
                totalCreditBalance = (decimal)(await cmd.ExecuteScalarAsync() ?? 0m);
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01") { }

            return JsonOk(req, new { accountsByStatus, revenueByDay, subscriptionsByState, totalCreditBalance, generatedAt = DateTimeOffset.UtcNow });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in AdminBilling");
            var err = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
            err.WriteString("Internal server error");
            return err;
        }
    }

    [Function("AdminSystem")]
    public async Task<HttpResponseData> GetSystem(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dashboard/system")] HttpRequestData req)
    {
        if (!await AdminTokenValidator.IsValidAsync(req.Headers.TryGetValues("Authorization", out var h) ? h.FirstOrDefault() : null, _config, _logger))
            return Unauthorized(req);

        try
        {
            await using var conn = OpenConnection();
            var tableSizes = new List<object>();
            long failedExtractionJobs24h = 0;
            long oldestPendingReminderAgeMinutes = 0;

            try
            {
                await using var cmd = new NpgsqlCommand("""
                    SELECT schemaname || '.' || tablename AS table_name,
                           pg_size_pretty(pg_total_relation_size(schemaname || '.' || tablename)) AS size,
                           pg_total_relation_size(schemaname || '.' || tablename) AS size_bytes
                    FROM pg_tables
                    WHERE schemaname = 'app'
                    ORDER BY size_bytes DESC
                    LIMIT 20
                    """, conn);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    tableSizes.Add(new { tableName = r.GetString(0), size = r.GetString(1), sizeBytes = r.GetInt64(2) });
            }
            catch (Exception) { }

            try
            {
                await using var cmd = new NpgsqlCommand("""
                    SELECT COUNT(*) FROM app.extraction_jobs
                    WHERE status = 'failed' AND created_at >= NOW() - INTERVAL '24 hours'
                    """, conn);
                failedExtractionJobs24h = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01") { }

            try
            {
                await using var cmd = new NpgsqlCommand("""
                    SELECT EXTRACT(EPOCH FROM (NOW() - MIN(due_at))) / 60
                    FROM app.reminders WHERE status = 'pending'
                    """, conn);
                var val = await cmd.ExecuteScalarAsync();
                if (val != null && val != DBNull.Value)
                    oldestPendingReminderAgeMinutes = Convert.ToInt64(val);
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01") { }

            return JsonOk(req, new
            {
                tableSizes,
                failedExtractionJobs24h,
                oldestPendingReminderAgeMinutes,
                serverUtc = DateTimeOffset.UtcNow,
                generatedAt = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in AdminSystem");
            var err = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
            err.WriteString("Internal server error");
            return err;
        }
    }
}
