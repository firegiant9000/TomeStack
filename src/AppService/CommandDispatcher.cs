using System.Text.Json;
using TomeStack.AppService.Packages;
using TomeStack.RulesCore;

namespace TomeStack.AppService;

/// <summary>
/// Transport-neutral JSON command protocol (ADR-006). Request:
/// <c>{ "id": "1", "command": "character.get", "payload": { ... } }</c>. Response:
/// <c>{ "id": "1", "ok": true, "result": ... }</c> or <c>{ "id": "1", "ok": false, "error": { "code", "message", "diagnostics" } }</c>.
/// </summary>
public sealed class CommandDispatcher(TomeStackApp app)
{
    /// <summary>Base64 package payloads dominate; this bounds a 50 MB package plus envelope.</summary>
    public const int MaxRequestChars = 72 * 1024 * 1024;

    public static IReadOnlyList<string> Commands { get; } =
    [
        "app.info", "content.list", "character.list", "character.get", "character.create", "character.save",
        "package.export", "package.preview", "package.apply",
    ];

    public string Dispatch(string requestJson)
    {
        string? id = null;
        try
        {
            if (requestJson.Length > MaxRequestChars)
                return Error(null, "request.too-large", "Request exceeds the size limit.");
            var request = JsonSerializer.Deserialize<CommandRequest>(requestJson, RulesJson.Compact)
                ?? throw new JsonException("Empty request.");
            id = request.Id;
            var result = Execute(request.Command, request.Payload);
            return JsonSerializer.Serialize(new { id, ok = true, result }, RulesJson.Compact);
        }
        catch (AppValidationException ex)
        {
            return Error(id, "validation", ex.Message, ex.Problems);
        }
        catch (PackageException ex)
        {
            return Error(id, "package", ex.Message, ex.Errors);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or FormatException or KeyNotFoundException)
        {
            return Error(id, "bad-request", ex.Message);
        }
        catch (Exception ex)
        {
            // Transport boundary: one failing command must not take down the shell or host.
            return Error(id, "internal", ex.Message);
        }
    }

    private object Execute(string command, JsonElement? payload) => command switch
    {
        "app.info" => app.GetInfo(),
        "content.list" => app.ListContent(Payload<RulesFamilyPayload>(payload).RulesFamily),
        "character.list" => app.ListCharacters(),
        "character.get" => app.GetCharacter(Payload<IdPayload>(payload).Id),
        "character.create" => app.CreateCharacter(Payload<CreateCharacterRequest>(payload)),
        "character.save" => app.SaveCharacter(Payload<Character>(payload)),
        "package.export" => ExportPackage(Payload<ExportPayload>(payload)),
        "package.preview" => app.PreviewImport(Convert.FromBase64String(Payload<PackagePayload>(payload).Base64)),
        "package.apply" => app.ApplyImport(Convert.FromBase64String(Payload<PackagePayload>(payload).Base64)),
        _ => throw new KeyNotFoundException($"Unknown command '{command}'."),
    };

    private object ExportPackage(ExportPayload payload)
    {
        var export = app.ExportCharacters(payload.CharacterIds);
        return new { export.FileName, Base64 = Convert.ToBase64String(export.Content), export.Manifest };
    }

    private static T Payload<T>(JsonElement? payload) =>
        payload is { ValueKind: JsonValueKind.Object } element
            ? element.Deserialize<T>(RulesJson.Compact) ?? throw new JsonException("Payload is null.")
            : throw new JsonException($"Command requires a {typeof(T).Name} payload object.");

    private static string Error(string? id, string code, string message, IReadOnlyList<Diagnostic>? diagnostics = null) =>
        JsonSerializer.Serialize(new { id, ok = false, error = new { code, message, diagnostics } }, RulesJson.Compact);

    private sealed record CommandRequest(string Id, string Command, JsonElement? Payload);

    private sealed record RulesFamilyPayload(string RulesFamily);

    private sealed record IdPayload(Guid Id);

    private sealed record ExportPayload(IReadOnlyList<Guid> CharacterIds);

    private sealed record PackagePayload(string Base64);
}
