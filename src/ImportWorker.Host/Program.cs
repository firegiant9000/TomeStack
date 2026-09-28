using System.Text;
using TomeStack.ImportWorker.Extraction;
using TomeStack.ImportWorker.Host;

// ADR-009 (c): one request on stdin, JSON lines on stdout, nothing else on stdout. Started only by the app.
var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
await using var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = false };
return await WorkerMain.RunAsync(stdin, stdout, WindowsOcrEngine.TryCreate(), CancellationToken.None);
