using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TomeStack.AppService;
using TomeStack.AppService.Diagnostics;

// Roadmap T6, the recovery drill (docs/features/restore-drill-procedure.md). These modes read and print, and exit before
// any host starts: no socket, no app, nothing written to the data folder.
//   --drill-report <data folder> [--out <report.json>]   counts and table digests, as a table and (optionally) JSON
//   --drill-compare <before.json> <after.json>           every difference, marked expected or not; exit 1 if any is not
if (args.Length > 0 && args[0] is "--drill-report" or "--drill-compare")
    return RunDrill(args);

// Development-only transport: exposes the same CommandDispatcher as the WebView2 bridge over
// 127.0.0.1 so the UI can run under Vite with hot reload. Never binds other interfaces.
// Each launch writes a fresh random token to <data dir>/devhost.token; the Vite proxy attaches it.

var dataDirectory = TomeStackApp.DefaultDataDirectory("TomeStack-dev");
var port = int.TryParse(Environment.GetEnvironmentVariable("TOMESTACK_DEV_PORT"), out var configuredPort) ? configuredPort : 5178;
string[] allowedOrigins = ["http://localhost:5173", "http://127.0.0.1:5173"];

using var tomeStack = TomeStackApp.Open(dataDirectory, devFixtures: true); // development host: original fixtures too
var dispatcher = new CommandDispatcher(tomeStack);
var dispatchGate = new Lock();
var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
var tokenBytes = Encoding.UTF8.GetBytes(token);
var tokenFile = Path.Combine(dataDirectory, "devhost.token");
File.WriteAllText(tokenFile, token);

var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(IPAddress.Loopback, port);
    options.Limits.MaxRequestBodySize = CommandDispatcher.MaxRequestChars;
});
var web = builder.Build();

web.MapGet("/api/health", () => Results.Text("ok"));

web.MapPost("/api/command", async (HttpContext context) =>
{
    var presented = Encoding.UTF8.GetBytes(context.Request.Headers["X-TomeStack-Token"].ToString());
    if (!CryptographicOperations.FixedTimeEquals(presented, tokenBytes))
        return Results.StatusCode(StatusCodes.Status401Unauthorized);

    var origin = context.Request.Headers.Origin.ToString();
    if (origin.Length > 0 && !allowedOrigins.Contains(origin, StringComparer.Ordinal))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
    var request = await reader.ReadToEndAsync(context.RequestAborted);
    string response;
    lock (dispatchGate)
        response = dispatcher.Dispatch(request);
    return Results.Text(response, "application/json", Encoding.UTF8);
});

web.Lifetime.ApplicationStopping.Register(() => File.Delete(tokenFile));
web.Logger.LogInformation("TomeStack dev host on http://127.0.0.1:{Port}; data: {DataDirectory}", port, dataDirectory);
await web.RunAsync();
return 0;

static int RunDrill(string[] args)
{
    var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
    try
    {
        if (args[0] == "--drill-report")
        {
            if (args.Length is not (2 or 4) || (args.Length == 4 && args[2] != "--out"))
            {
                Console.Error.WriteLine("Usage: --drill-report <data folder> [--out <report.json>]");
                return 2;
            }
            var report = RestoreDrill.Count(args[1]);
            Console.Write(RestoreDrill.Format(report));
            if (args.Length == 4)
                File.WriteAllText(args[3], JsonSerializer.Serialize(report, json));
            return 0;
        }
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Usage: --drill-compare <before.json> <after.json>");
            return 2;
        }
        var before = JsonSerializer.Deserialize<RestoreDrill.Report>(File.ReadAllText(args[1]), json)!;
        var after = JsonSerializer.Deserialize<RestoreDrill.Report>(File.ReadAllText(args[2]), json)!;
        var differences = RestoreDrill.Compare(before, after);
        foreach (var d in differences)
            Console.WriteLine($"{(d.Expected is null ? "DIFFERS " : "expected")}  {d.Item,-28} {d.Before} -> {d.After}{(d.Expected is null ? "" : $"  ({d.Expected})")}");
        var unexpected = differences.Count(d => d.Expected is null);
        Console.WriteLine(unexpected == 0 ? $"MATCH: no unexpected difference ({differences.Count} expected)" : $"{unexpected} unexpected difference(s)");
        return unexpected == 0 ? 0 : 1;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
    {
        // The message names no path: the drill record is public.
        Console.Error.WriteLine($"{ex.GetType().Name}: {(ex is DataFolderInUseException ? ex.Message : "the file or folder could not be read")}");
        return 2;
    }
}
