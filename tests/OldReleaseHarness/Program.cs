// Committed harness, never shipped (spec section 8, real older build). Compiled against an older release tag's own
// src/AppService, it drives the JSON dispatcher the way the UI does so an old build can open a package and calculate a sheet.
// Modes (each needs --data-dir <dir>):
//   import  <package> --data-dir <dir>      package.preview, then package.apply
//   restore <backup>  --data-dir <dir>      library.restoreChoose, then library.restoreApply
//   sheet   <characterId> --data-dir <dir>  character.get, printed as { id, name, fields }
// stdout carries one compact JSON document. Exit 0 = ok, 1 = the dispatcher refused, 2 = usage error or exception.
using System.Text.Json.Nodes;
using TomeStack.AppService;

static class Program
{
    static int _requestId;

    static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 2;
        }
    }

    static int Usage(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine("usage: OldReleaseHarness (import <package> | restore <backup> | sheet <characterId>) --data-dir <dir>");
        return 2;
    }

    static int Run(string[] args)
    {
        string? dataDir = null;
        var positional = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--data-dir")
            {
                if (i + 1 >= args.Length) return Usage("--data-dir needs a value");
                dataDir = args[++i];
            }
            else positional.Add(args[i]);
        }

        if (dataDir is null) return Usage("--data-dir is required");
        if (positional.Count != 2 || positional[0] is not ("import" or "restore" or "sheet"))
            return Usage("expected a mode (import, restore or sheet) and one argument");

        var full = Path.GetFullPath(dataDir);
        var live = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TomeStack"));
        var liveWithSeparator = live.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (string.Equals(full.TrimEnd(Path.DirectorySeparatorChar), live, StringComparison.OrdinalIgnoreCase)
            || (full.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar).StartsWith(liveWithSeparator, StringComparison.OrdinalIgnoreCase))
            return Usage("refusing to use the app's own data folder");

        var mode = positional[0];
        var argument = positional[1];
        Directory.CreateDirectory(full);
        using var app = TomeStackApp.Open(full, syncRoots: Array.Empty<string>());
        var dispatcher = new CommandDispatcher(app, host: new Host(mode == "restore" ? Path.GetFullPath(argument) : null));

        JsonNode output;
        var accepted = true;
        switch (mode)
        {
            case "import":
                (accepted, output) = Import(dispatcher, argument);
                break;
            case "restore":
                (accepted, output) = Restore(dispatcher);
                break;
            default:
                (accepted, output) = Sheet(dispatcher, argument);
                break;
        }

        Console.Out.WriteLine(output.ToJsonString());
        return accepted ? 0 : 1;
    }

    // Returns the dispatcher result, or null with the error filled in.
    static JsonNode? Try(CommandDispatcher dispatcher, string command, JsonNode? payload, out JsonNode? error)
    {
        var request = new JsonObject { ["id"] = (++_requestId).ToString(), ["command"] = command };
        if (payload is not null) request["payload"] = payload;
        var response = JsonNode.Parse(dispatcher.Dispatch(request.ToJsonString()))!;
        if (response["ok"]!.GetValue<bool>())
        {
            error = null;
            return response["result"] ?? new JsonObject();
        }

        error = response["error"] ?? new JsonObject();
        Console.Error.WriteLine($"{command} refused: {error.ToJsonString()}");
        return null;
    }

    static JsonObject Obj(params (string Key, JsonNode? Value)[] props)
    {
        var o = new JsonObject();
        foreach (var (key, value) in props) o[key] = value;
        return o;
    }

    static (bool Ok, JsonNode Output) Import(CommandDispatcher dispatcher, string file)
    {
        var base64 = Convert.ToBase64String(File.ReadAllBytes(file));
        JsonNode? error;
        if (Try(dispatcher, "package.preview", Obj(("base64", base64)), out error) is not null)
            Try(dispatcher, "package.apply", Obj(("base64", base64)), out error);
        return Finish(dispatcher, error);
    }

    static (bool Ok, JsonNode Output) Restore(CommandDispatcher dispatcher)
    {
        JsonNode? error;
        var chosen = Try(dispatcher, "library.restoreChoose", null, out error);
        if (chosen is not null)
        {
            if (chosen["chosen"]?.GetValue<bool>() == true)
                Try(dispatcher, "library.restoreApply", Obj(("token", chosen["token"]!.GetValue<string>()), ("confirm", true)), out error);
            else
                error = Obj(("code", "not-chosen"), ("message", "library.restoreChoose chose no file"));
        }

        return Finish(dispatcher, error);
    }

    static (bool Ok, JsonNode Output) Finish(CommandDispatcher dispatcher, JsonNode? error)
    {
        var ids = new List<string>();
        if (Try(dispatcher, "character.list", null, out var listError) is { } list)
            foreach (var summary in list.AsArray()) ids.Add(summary!["id"]!.GetValue<string>());
        else error ??= listError;
        ids.Sort(StringComparer.Ordinal);

        var output = Obj(("ok", error is null), ("error", error?.DeepClone()),
            ("characters", new JsonArray(ids.Select(id => (JsonNode?)id).ToArray())));
        return (error is null, output);
    }

    static (bool Ok, JsonNode Output) Sheet(CommandDispatcher dispatcher, string id)
    {
        var view = Try(dispatcher, "character.get", Obj(("id", id)), out var error);
        if (view is null) return (false, Obj(("ok", false), ("error", error?.DeepClone())));

        var fields = new JsonObject();
        foreach (var f in view["sheet"]!["fields"]!.AsArray())
            fields[f!["field"]!.GetValue<string>()] = f["value"]?.DeepClone();
        var name = view["character"]?["name"]?.DeepClone();
        return (true, Obj(("id", id), ("name", name), ("fields", fields)));
    }

    sealed class Host(string? open) : IHostServices
    {
        public string? ChooseSaveLocation(string suggestedFileName, string filterDescription, string extension) => null;
        public bool CanOpenFiles => open is not null;
        public string? ChooseOpenFile(string filterDescription, string extension) => open;
    }
}
