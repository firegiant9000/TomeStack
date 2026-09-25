using System.Net;
using System.Security.Cryptography;
using System.Text;
using TomeStack.AppService;

// Development-only transport: exposes the same CommandDispatcher as the WebView2 bridge over
// 127.0.0.1 so the UI can run under Vite with hot reload. Never binds other interfaces.
// Each launch writes a fresh random token to <data dir>/devhost.token; the Vite proxy attaches it.

var dataDirectory = TomeStackApp.DefaultDataDirectory("TomeStack-dev");
var port = int.TryParse(Environment.GetEnvironmentVariable("TOMESTACK_DEV_PORT"), out var configuredPort) ? configuredPort : 5178;
string[] allowedOrigins = ["http://localhost:5173", "http://127.0.0.1:5173"];

using var tomeStack = TomeStackApp.Open(dataDirectory);
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
