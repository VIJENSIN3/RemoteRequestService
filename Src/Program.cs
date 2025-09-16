using System.Management.Automation;
using System.Net;
using System.Security;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddJsonConsole();
var app = builder.Build();

// Health endpoint
app.MapGet("/ping", () => "running");

// Metrics endpoint 
app.MapGet("/metrics", (IConfiguration config) =>
{
    // Metrics with counters
    var maxAttempts = config.GetValue<int>("Resilience:MaxAttempts", 3);
    return $"MaxAttempts: {maxAttempts}\nRequests: 0\nSuccess: 0\nFailed: 0";
});

// Common API route for http requests and powershell requests
app.MapMethods("/api/{**path}", ["GET", "POST", "PUT", "PATCH", "DELETE"], async (HttpContext ctx, IConfiguration config, ILogger<Program> logger) =>
{
    // Configuration values
    var maxBodySize = config.GetValue<long>("MaxBodySizeBytes", 1048576);
    var maxAttempts = config.GetValue<int>("Resilience:MaxAttempts", 3);
    var baseDelayMs = config.GetValue<int>("Resilience:BaseDelayMs", 1000);
    var timeoutMs = config.GetValue<int>("Resilience:PerAttemptTimeoutMs", 5000);
    var allowedCommands = config.GetSection("PowerShell:AllowedCommands").Get<string[]>() ?? ["Get-Mailbox", "Get-User"];
    var allowedHeaders = config.GetSection("Http:AllowedHeaders").Get<string[]>() ?? ["Authorization", "Content-Type"];

    var requestId = Guid.NewGuid().ToString();
    logger.LogInformation("Start {RequestId} {Method} {Path}", requestId, ctx.Request.Method, ctx.Request.Path);

    var start = DateTime.UtcNow;
    var path = ctx.Request.Path.Value?.Split('/', 3)[2] ?? "";
    var body = ctx.Request.ContentLength > 0 ? await new StreamReader(ctx.Request.Body).ReadToEndAsync() : null;

    // Validate body size
    if (body?.Length > maxBodySize) return Results.BadRequest(new { Error = "Body too large" });

    string executorType = path.StartsWith("powershell/") ? "powershell" : "http";
    var attempts = new List<(int, string, double)>();
    object? result = null;
    string status = "Fail";

    // Retry loop
    for (int attempt = 1; attempt <= maxAttempts; attempt++)
    {
        var attemptStart = DateTime.UtcNow;
        string outcome = "Fail";
        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            if (executorType == "http")
            {
                result = await ExecuteHttp(ctx, path, allowedHeaders, cts.Token);
                outcome = "Success";
                status = "Success";
            }
            else
            {
                var cmd = path[10..].TrimStart('/'); //validate the allowed command
                if (!allowedCommands.Contains(cmd)) return Results.BadRequest(new { Error = "Invalid command" });
                result = await ExecutePowerShell(ctx, cmd, body, cts.Token);
                outcome = "Success";
                status = "Success";
            }
            break;
        }
        catch (Exception ex)
        {
            outcome = ex.Message.Contains("transient") ? "TransientFail" : "NonTransientFail";
            if (outcome == "NonTransientFail") break;
            await Task.Delay(baseDelayMs * (int)Math.Pow(2, attempt - 1) + Random.Shared.Next(0, 500));
        }
        attempts.Add((attempt, outcome, (DateTime.UtcNow - attemptStart).TotalMilliseconds));
    }

    var end = DateTime.UtcNow;
    logger.LogInformation("End {RequestId} {Status}", requestId, status);

    return Results.Json(new
    {
        RequestId = requestId,
        ExecutorType = executorType,
        Start = start,
        End = end,
        Status = status,
        Attempts = attempts.Select(a => new { Attempt = a.Item1, Outcome = a.Item2, DurationMs = a.Item3 }),
        Result = result
    });
});

// HTTP executor
async Task<object> ExecuteHttp(HttpContext ctx, string path, string[] allowedHeaders, CancellationToken ct)
{
    using var client = new HttpClient();
    var url = path.StartsWith("http") ? path : $"https://{path}{ctx.Request.QueryString}";
    var req = new HttpRequestMessage(new HttpMethod(ctx.Request.Method), url);
    foreach (var h in ctx.Request.Headers)
        if (allowedHeaders.Contains(h.Key)) req.Headers.Add(h.Key, h.Value.ToString());
    if (ctx.Request.ContentLength > 0) req.Content = new StringContent(await new StreamReader(ctx.Request.Body).ReadToEndAsync());
    var resp = await client.SendAsync(req, ct);
    var body = await resp.Content.ReadAsStringAsync();
    if (!resp.IsSuccessStatusCode) throw new Exception(resp.StatusCode >= HttpStatusCode.InternalServerError ? "transient" : "non-transient");
    return new { Status = (int)resp.StatusCode, Body = body };
}

// PowerShell executor
async Task<object> ExecutePowerShell(HttpContext ctx, string command, string? body, CancellationToken ct)
{
    using var ps = PowerShell.Create();
    if (!System.Diagnostics.Debugger.IsAttached)
    {
        ps.AddScript("Import-Module ExchangeOnlineManagement -ErrorAction Stop");
        await ps.InvokeAsync();
        if (ps.HadErrors) throw new Exception("Failed to import ExchangeOnlineManagement");

        var user = ctx.Request.Headers["X-Auth-User"].FirstOrDefault();
        var pass = ctx.Request.Headers["X-Auth-Pass"].FirstOrDefault();
        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass)) throw new Exception("non-transient");

        var securePass = new SecureString();
        foreach (var c in pass) securePass.AppendChar(c);
        ps.AddCommand("Connect-ExchangeOnline").AddParameter("Credential", new PSCredential(user, securePass));
        await ps.InvokeAsync();

        if (ps.HadErrors)
        {
            var err = ps.Streams.Error[0].ToString();
            throw new Exception(err.Contains("authentication") ? "non-transient" : "transient");
        }

        ps.Commands.Clear();
        ps.AddCommand(command);
        if (body != null)
        {
            var parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(body);
            foreach (var p in parameters ?? []) ps.AddParameter(p.Key, p.Value);
        }

        var results = await ps.InvokeAsync();
        var output = JsonSerializer.Serialize(results.Select(obj => PSObjectToDict(obj)));

        ps.AddCommand("Disconnect-ExchangeOnline").AddParameter("Confirm", false);
        await ps.InvokeAsync();
        return new { Command = command, Output = output };
    }
    else
    {
        return new { Command = command, Output = "[{\"DisplayName\":\"Test Powershell\",\"Identity\":\"testpowershell@example.com\"}]" };
    }
}
// Helper method to convert PSObject to dictionary (unchanged)
static object? PSObjectToDict(PSObject? pso)
{
    if (pso == null) return null;
    var dict = new Dictionary<string, object?>();
    foreach (var prop in pso.Properties)
        dict[prop.Name] = prop.Value;
    return dict;
}
app.Run();