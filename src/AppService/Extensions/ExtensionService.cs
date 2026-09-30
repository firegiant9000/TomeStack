using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TomeStack.AppService.Exports;
using TomeStack.AppService.Extensions;
using TomeStack.RulesCore;

namespace TomeStack.AppService.Extensions
{
    /// <summary>
    /// An installed extension (database v9, ADR-011). <paramref name="Grants"/> are bound to the file with
    /// <paramref name="Sha256"/>: installing another file of the same extension, even with the same permissions, asks again.
    /// </summary>
    public sealed record InstalledExtension(
        Guid Id, string Sha256, ExtensionManifest Manifest, IReadOnlyList<string> Grants, bool Enabled, DateTimeOffset InstalledAt, DateTimeOffset UpdatedAt);

    public sealed record PermissionInfo(string Permission, string Description);

    /// <summary>What installing this file changes for an extension that is already installed.</summary>
    public sealed record ExtensionUpdate(
        string InstalledVersion, string NewVersion, bool SameFile,
        IReadOnlyList<string> PermissionsAdded, IReadOnlyList<string> PermissionsRemoved, IReadOnlyList<string> HooksAdded, IReadOnlyList<string> HooksRemoved);

    /// <summary>
    /// <c>extension.installPreview</c>: the review before anything is installed (CLAUDE.md: imported content never
    /// becomes active without review). <paramref name="Token"/> is set only when it can be installed.
    /// </summary>
    public sealed record ExtensionInstallPreview(
        Guid? Token, ExtensionManifest? Manifest, string? Sha256, IReadOnlyList<PermissionInfo> Permissions, ExtensionUpdate? Update,
        IReadOnlyList<Diagnostic> Errors, IReadOnlyList<Diagnostic> Warnings)
    {
        public bool CanInstall => Token is not null;
    }

    /// <param name="Input">Import hooks: the file's bytes (from the native Open dialog, or base64 in browser development).</param>
    /// <param name="RulesFamily">Import hooks: the rules family of the drafts (a draft may name its own).</param>
    /// <param name="SourceTitle">Import hooks: the title of the new source the drafts go into.</param>
    public sealed record ExtensionRunRequest(
        Guid ExtensionId, string HookId, Guid? CharacterId = null, IReadOnlyList<Guid>? SourceIds = null, SheetPurpose Purpose = SheetPurpose.Share,
        string? RulesFamily = null, string? SourceTitle = null, byte[]? Input = null);

    /// <summary>A draft an import hook would create, with its validation (drafts may be incomplete; publishing checks again).</summary>
    public sealed record ExtensionDraft(string Name, ContentKind Kind, IReadOnlyList<Diagnostic> Errors, IReadOnlyList<Diagnostic> Warnings);

    /// <summary>
    /// <c>extension.runPreview</c>: what a run would write, before anything is written (ADR-011 "Every run is
    /// user-initiated"). Export: the file name, its size and the start of its text, and what was left out. Import: the
    /// drafts and the new source they go into.
    /// </summary>
    public sealed record ExtensionRunPreview(
        Guid Token, HookKind Kind, SheetPurpose Purpose, string? FileName, long? Bytes, string? Excerpt, string? SourceTitle,
        IReadOnlyList<ExtensionDraft> Drafts, IReadOnlyList<SheetDropped> Dropped, IReadOnlyList<SheetNotice> Notices, IReadOnlyList<Diagnostic> Warnings);

    /// <summary><c>extension.runImport</c>: the source the drafts went into.</summary>
    public sealed record ExtensionImportResult(Guid SourceId, string SourceTitle, int Drafts);
}

namespace TomeStack.AppService
{
    /// <summary>
    /// M6 slice 3 (ADR-011, option A, accepted 2026-09-29): installing, granting, running and removing declarative
    /// extensions. No extension code runs: a hook is a transform document that <see cref="DeclarativeTransform"/> reads.
    /// Every run is started by the user, shows a preview, and writes only what the preview showed (a one-use token).
    /// Documented in docs/features/extensions.md.
    /// </summary>
    public sealed partial class TomeStackApp
    {
        public const string ExtensionFolderName = "extensions";
        public const int MaxDraftsPerRun = 5_000;

        private readonly ConcurrentDictionary<Guid, byte[]> _pendingInstalls = new();
        private readonly ConcurrentDictionary<Guid, PendingRun> _pendingRuns = new();

        /// <summary>Test seam: the user-profile path and Windows user name the output scan looks for.</summary>
        internal Func<(string? Profile, string? UserName)> ScanIdentity { get; set; } =
            () => (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.UserName);

        /// <param name="Needed">The permissions the run used; each must still be granted when it is applied (review fix).</param>
        private sealed record PendingRun(
            Guid ExtensionId, string Sha256, HookKind Kind, string? FileName, byte[]? Output, SourceRecord? Source, IReadOnlyList<ContentRevision> Drafts,
            IReadOnlyList<string> Needed);

        private string ExtensionDirectory => Path.Combine(DataDirectory, ExtensionFolderName);

        public IReadOnlyList<InstalledExtension> ListExtensions() => _store.ListExtensions();

        /// <summary>
        /// <c>extension.installPreview</c>: checks the file completely (ADR-011: package limits, path allowlist, manifest,
        /// API version, permissions, every transform) and shows the permissions it asks for. Writes nothing.
        /// </summary>
        public ExtensionInstallPreview PreviewExtensionInstall(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            ExtensionPackage package;
            try
            {
                package = ExtensionReader.Read(bytes);
            }
            catch (ExtensionException ex)
            {
                return new(null, null, null, [], null, ex.Errors, []);
            }
            var manifest = package.Manifest;
            var errors = new List<Diagnostic>();
            var warnings = new List<Diagnostic>();
            ExtensionUpdate? update = null;
            if (_store.FindExtension(manifest.Id) is { } installed)
            {
                // Grants are bound to the file, not to the self-declared id, and the author may not change under an id
                // (review fix in ADR-011; a signing key replaces this check before any code-running runtime exists).
                if (!string.Equals(installed.Manifest.Author, manifest.Author, StringComparison.Ordinal))
                    errors.Add(new("extension.author-changed", "This file claims the id of an installed extension by a different author, so it is not installed. Remove the installed one first if you trust this file."));
                update = new(
                    installed.Manifest.Version, manifest.Version, installed.Sha256 == package.Sha256,
                    [.. manifest.Permissions.Except(installed.Manifest.Permissions)], [.. installed.Manifest.Permissions.Except(manifest.Permissions)],
                    [.. manifest.Hooks.Select(h => h.Id).Except(installed.Manifest.Hooks.Select(h => h.Id))], [.. installed.Manifest.Hooks.Select(h => h.Id).Except(manifest.Hooks.Select(h => h.Id))]);
                warnings.Add(new("extension.update", "This replaces the installed version. You grant its permissions again; nothing carries over."));
            }
            Guid? token = null;
            if (errors.Count == 0)
            {
                token = Guid.NewGuid();
                KeepFew(_pendingInstalls);
                _pendingInstalls[token.Value] = bytes;
            }
            return new(token, manifest, package.Sha256, [.. manifest.Permissions.Select(p => new PermissionInfo(p, ExtensionPermissions.Describe(p)))], update, errors, warnings);
        }

        /// <summary>
        /// <c>extension.install</c>: installs the file whose preview the user saw, with the permissions they granted (a
        /// subset of what it asks for; a hook whose permissions are not all granted cannot run). The file is kept read-only
        /// at <c>extensions/&lt;sha256&gt;.zip</c>.
        /// </summary>
        public InstalledExtension InstallExtension(Guid token, IReadOnlyList<string>? grants, bool confirm)
        {
            if (!confirm)
                throw new AppValidationException([new("extension.confirm-required", "Show the install preview and confirm it first.")]);
            if (!_pendingInstalls.TryRemove(token, out var bytes))
                throw new AppValidationException([new("extension.not-chosen", "Choose the extension file again.")]);
            var preview = PreviewExtensionInstall(bytes);
            _pendingInstalls.TryRemove(preview.Token ?? Guid.Empty, out _);
            if (!preview.CanInstall)
                throw new AppValidationException(preview.Errors);
            var manifest = preview.Manifest!;
            var granted = (grants ?? []).Distinct().ToList();
            if (granted.FirstOrDefault(g => !manifest.Permissions.Contains(g)) is { } extra)
                throw new AppValidationException([new("extension.grant-unknown", $"'{extra}' is not a permission this extension asks for.")]);

            ExtensionFiles.Write(ExtensionDirectory, preview.Sha256!, bytes);
            var now = _time.GetUtcNow();
            var previous = _store.FindExtension(manifest.Id);
            var installed = new InstalledExtension(manifest.Id, preview.Sha256!, manifest, granted, granted.Count > 0, previous?.InstalledAt ?? now, now);
            _store.InTransaction(() => _store.SaveExtension(installed));
            if (previous is not null && previous.Sha256 != installed.Sha256)
                DeleteExtensionFile(previous.Sha256);
            ForgetRuns(installed.Id); // grants changed: a run previewed before is previewed again (review fix)
            return installed;
        }

        /// <summary>
        /// <c>extension.review</c> (review fix): the install preview of an extension that is already installed, read from its
        /// stored file, so its permissions can be granted again, for example after a full restore brought it back ungranted.
        /// </summary>
        public ExtensionInstallPreview PreviewInstalledExtension(Guid extensionId)
        {
            var installed = _store.FindExtension(extensionId)
                ?? throw new AppValidationException([new("extension.not-found", $"Extension {extensionId} is not installed.")]);
            var bytes = ExtensionFiles.ReadIntact(ExtensionDirectory, installed.Sha256)
                ?? throw new AppValidationException([new("extension.file-changed", $"The file of '{installed.Manifest.Name}' is missing or changed on disk. Install it again from its file.")]);
            return PreviewExtensionInstall(bytes);
        }

        private void ForgetRuns(Guid extensionId)
        {
            foreach (var run in _pendingRuns.Where(r => r.Value.ExtensionId == extensionId).Select(r => r.Key).ToList())
                _pendingRuns.TryRemove(run, out _);
        }

        public InstalledExtension SetExtensionEnabled(Guid extensionId, bool enabled)
        {
            var installed = _store.FindExtension(extensionId)
                ?? throw new AppValidationException([new("extension.not-found", $"Extension {extensionId} is not installed.")]);
            if (enabled && installed.Grants.Count == 0)
                throw new AppValidationException([new("extension.no-grants", "Grant at least one permission first: install it again to review them.")]);
            var updated = installed with { Enabled = enabled, UpdatedAt = _time.GetUtcNow() };
            _store.InTransaction(() => _store.SaveExtension(updated));
            if (!enabled)
                ForgetRuns(extensionId);
            return updated;
        }

        /// <summary><c>extension.remove</c>: revokes everything and removes its file. Drafts it made stay: they are yours now.</summary>
        public void RemoveExtension(Guid extensionId, bool confirm)
        {
            if (!confirm)
                throw new AppValidationException([new("extension.confirm-required", "Confirm removing the extension.")]);
            var installed = _store.FindExtension(extensionId)
                ?? throw new AppValidationException([new("extension.not-found", $"Extension {extensionId} is not installed.")]);
            _store.InTransaction(() => _store.DeleteExtension(extensionId));
            DeleteExtensionFile(installed.Sha256);
            ForgetRuns(extensionId);
        }

        private void DeleteExtensionFile(string sha256)
        {
            if (_store.ExtensionFileInUse(sha256))
                return;
            var path = Path.Combine(ExtensionDirectory, $"{sha256}.zip");
            try
            {
                if (File.Exists(path))
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort (a scanner may hold it); an unused file is harmless and a later removal tries again.
            }
        }

        /// <summary>The installed file, checked against the hash its grants are bound to, and read again.</summary>
        private ExtensionPackage LoadExtension(InstalledExtension installed)
        {
            var bytes = ExtensionFiles.ReadIntact(ExtensionDirectory, installed.Sha256)
                ?? throw new AppValidationException([new("extension.file-changed", $"The file of '{installed.Manifest.Name}' is missing or changed on disk since it was granted, so it does not run. Install it again to review it.")]);
            try
            {
                return ExtensionReader.Read(bytes);
            }
            catch (ExtensionException ex)
            {
                throw new AppValidationException(ex.Errors);
            }
        }

        /// <summary>
        /// <c>extension.runPreview</c>: runs the hook's transform on what the user picked and shows the result. Writes
        /// nothing; the token lets <see cref="ApplyExtensionImport"/> or <see cref="ExtensionExportOutput"/> write exactly this.
        /// </summary>
        public ExtensionRunPreview PreviewExtensionRun(ExtensionRunRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            var installed = _store.FindExtension(request.ExtensionId)
                ?? throw new AppValidationException([new("extension.not-found", $"Extension {request.ExtensionId} is not installed.")]);
            if (!installed.Enabled)
                throw new AppValidationException([new("extension.disabled", $"'{installed.Manifest.Name}' is turned off.")]);
            var hook = installed.Manifest.Hooks.FirstOrDefault(h => h.Id == request.HookId)
                ?? throw new AppValidationException([new("extension.hook-not-found", $"'{installed.Manifest.Name}' has no hook '{request.HookId}'.")]);
            var package = LoadExtension(installed);
            var transform = package.Transforms[hook.Id];
            var used = new List<string>();
            void Need(string permission)
            {
                if (!installed.Grants.Contains(permission))
                    throw new AppValidationException([new("extension.permission-not-granted", $"'{installed.Manifest.Name}' needs the {permission} permission for this, and you did not grant it.")]);
                used.Add(permission);
            }

            try
            {
                return hook.Kind == HookKind.Export ? PreviewExport(installed, hook, transform, request, Need, used) : PreviewImport(installed, hook, transform, request, Need, used);
            }
            catch (InvalidOperationException)
            {
                // A value the writer cannot write (defence in depth: the transform's own output check refuses these first).
                throw new AppValidationException([new("transform.too-deep", $"'{installed.Manifest.Name}', hook '{hook.Id}': the output could not be written; it nests too deep.")]);
            }
            catch (TransformException ex)
            {
                // The message names the bound or the problem, never the data.
                throw new AppValidationException([new(ex.Code, $"'{installed.Manifest.Name}', hook '{hook.Id}': {ex.Message}")]);
            }
        }

        private ExtensionRunPreview PreviewExport(InstalledExtension installed, ExtensionHook hook, DeclarativeTransform transform, ExtensionRunRequest request, Action<string> need, IReadOnlyList<string> used)
        {
            need(ExtensionPermissions.ExportFile);
            if (request.CharacterId is null && (request.SourceIds is null || request.SourceIds.Count == 0))
                throw new AppValidationException([new("extension.input-required", "Pick a character, or sources, for this export.")]);
            var root = new JsonObject();
            var dropped = new List<SheetDropped>();
            var notices = new List<SheetNotice>();
            string baseName = "content";
            if (request.CharacterId is { } characterId)
            {
                need(ExtensionPermissions.ReadSheet);
                var sheet = SheetExportFor(characterId, request.Purpose);
                root["sheet"] = JsonSerializer.SerializeToNode(sheet, RulesJson.Compact);
                dropped.AddRange(sheet.Dropped);
                notices.AddRange(sheet.Notices);
                baseName = sheet.Character.Name;
            }
            if (request.SourceIds is { Count: > 0 } sourceIds)
            {
                need(ExtensionPermissions.ReadContent);
                var content = new JsonArray();
                foreach (var sourceId in sourceIds.Distinct())
                {
                    var source = _store.FindSource(sourceId)
                        ?? throw new AppValidationException([new("extension.source-missing", $"Source {sourceId} is not installed.")]);
                    var revisions = _store.ListRevisionsInOrder().Where(r => r.Provenance.SourceId == sourceId && r.Status == RevisionStatus.Published).ToList();
                    var letsOut = SheetExportBuilder.LetsOut(source, request.Purpose, _bundledSources);
                    notices.Add(new(source.Title, source.Publisher, source.License, source.Redistributable, source.Attribution, source.ModificationNotice, !letsOut));
                    if (!letsOut)
                    {
                        // ADR-007 item 11: filtered by the run's purpose whatever the extension's permissions.
                        dropped.Add(new(source.Title, source.Publisher, revisions.Count));
                        continue;
                    }
                    foreach (var revision in revisions)
                        content.Add(JsonSerializer.SerializeToNode(revision, RulesJson.Compact));
                }
                root["content"] = content;
            }

            var result = transform.Run(root);
            string text;
            if (hook.Produces == "text")
            {
                text = DeclarativeTransform.AsText(result) is { } asText && result is JsonValue
                    ? asText
                    : throw new AppValidationException([new("extension.output-invalid", $"'{installed.Manifest.Name}', hook '{hook.Id}' must produce text, and produced something else.")]);
            }
            else
                text = result?.ToJsonString(new JsonSerializerOptions { WriteIndented = true, MaxDepth = 256 }) ?? "null";
            if (text.Length > DeclarativeTransform.MaxOutputChars)
                throw new AppValidationException([new("extension.output-too-large", "The output is larger than 5 MB, so nothing was written.")]);
            if (OutputScan.Leaks(text, SensitivePaths(), [ScanIdentity().UserName]))
                throw new AppValidationException([new("extension.output-refused", $"The output of '{installed.Manifest.Name}' contains a local path or your Windows user name, so nothing was written.")]);
            // ADR-011: "Every consumer must carry them." Each notice's title and license must be in the file (review fix:
            // the notices were only shown in the preview). JSON output is read as its string values, escapes decoded.
            var written = hook.Produces == "text" ? text : string.Join("\n", StringsOf(result));
            if (notices.FirstOrDefault(n => !written.Contains(n.Title, StringComparison.Ordinal) || !written.Contains(n.License, StringComparison.Ordinal)) is { } missing)
                throw new AppValidationException([new("extension.notices-missing", $"The output of '{installed.Manifest.Name}' leaves out the license notice of '{missing.Title}' ({missing.License}), so nothing was written. An export must carry every notice.")]);

            var bytes = Encoding.UTF8.GetBytes(text);
            var extension = hook.FileExtension ?? (hook.Produces == "json" ? ".json" : ".txt");
            var fileName = $"{SafeName(baseName)}-{hook.Id}{extension}";
            var token = Remember(new PendingRun(installed.Id, installed.Sha256, HookKind.Export, fileName, bytes, null, [], [.. used]));
            var warnings = new List<Diagnostic>();
            if (request.Purpose == SheetPurpose.Personal)
                warnings.Add(new("export.personal", "Personal copy: includes your own homebrew. Do not share it."));
            return new(token, HookKind.Export, request.Purpose, fileName, bytes.LongLength, text.Length > 2_000 ? text[..2_000] : text, null, [], dropped, notices, warnings);
        }

        private static IEnumerable<string> StringsOf(JsonNode? node) => node switch
        {
            JsonObject obj => obj.SelectMany(p => StringsOf(p.Value).Prepend(p.Key)),
            JsonArray array => array.SelectMany(StringsOf),
            JsonValue value when value.TryGetValue<string>(out var s) => [s],
            _ => [],
        };

        private ExtensionRunPreview PreviewImport(InstalledExtension installed, ExtensionHook hook, DeclarativeTransform transform, ExtensionRunRequest request, Action<string> need, IReadOnlyList<string> used)
        {
            need(ExtensionPermissions.ImportFile);
            need(ExtensionPermissions.WriteDrafts);
            if (request.Input is null)
                throw new AppValidationException([new("extension.input-required", "Pick the file to import.")]);
            var family = request.RulesFamily ?? RulesFamilies.Srd521;
            if (!RulesFamilies.IsKnown(family))
                throw new AppValidationException([new("rules-family.unknown", $"Rules family '{family}' is not supported.")]);
            var title = string.IsNullOrWhiteSpace(request.SourceTitle) ? $"{installed.Manifest.Name} import" : request.SourceTitle.Trim();
            if (title.Length > MaxSourceTitleLength)
                throw new AppValidationException([new("source.title-required", $"A source needs a title of 1 to {MaxSourceTitleLength} characters.")]);

            var input = ExtensionInput.Parse(request.Input, hook.Accepts!);
            var result = transform.Run(input);
            if (result is not JsonArray items)
                throw new AppValidationException([new("extension.output-invalid", $"'{installed.Manifest.Name}', hook '{hook.Id}' must produce a list of drafts.")]);
            if (items.Count is 0 or > MaxDraftsPerRun)
                throw new AppValidationException([new("extension.output-invalid", $"An import creates 1 to {MaxDraftsPerRun:N0} drafts; this one produced {items.Count:N0}.")]);

            // ADR-011 write.drafts: a new source of its own, never shareable, import-derived for good (M6 slice 1).
            var now = _time.GetUtcNow();
            var source = new SourceRecord
            {
                Id = Guid.NewGuid(),
                Title = title,
                Publisher = $"Imported with {installed.Manifest.Name}".Length > 200 ? "Imported with an extension" : $"Imported with {installed.Manifest.Name}",
                RulesFamilies = [family],
                EditionVersion = "homebrew",
                License = "Personal homebrew",
                Redistributable = false,
                ImportedAt = now,
                Origin = SourceOrigin.Local,
                ImportDerived = true,
            };
            var catalog = new WithSource(_store, source);
            var drafts = new List<ContentRevision>();
            var previews = new List<ExtensionDraft>();
            var warnings = new List<Diagnostic>();
            var index = 0;
            foreach (var item in items)
            {
                index++;
                var draft = DraftFrom(item, source.Id, family, index, warnings);
                if (draft is null)
                    continue;
                var empty = ContentValidator.EmptyEntries(draft);
                var report = empty.Count > 0 ? null : ContentValidator.Validate(draft, catalog);
                previews.Add(new(draft.Name, draft.Kind, empty.Count > 0 ? empty : report!.Errors, report?.Warnings ?? []));
                if (empty.Count == 0)
                    drafts.Add(draft);
            }
            if (drafts.Count == 0)
                throw new AppValidationException([new("extension.output-invalid", $"'{installed.Manifest.Name}', hook '{hook.Id}' produced no draft this TomeStack can read."), .. warnings.Take(20)]);
            var token = Remember(new PendingRun(installed.Id, installed.Sha256, HookKind.Import, null, null, source, drafts, [.. used]));
            warnings.Add(new("extension.import-derived", $"The drafts go into a new source, '{title}', which is marked as holding imported material: it is never shared. Nothing is active until you publish it."));
            return new(token, HookKind.Import, SheetPurpose.Personal, null, null, null, title, previews, [], [], warnings);
        }

        /// <summary>
        /// One output item as a draft revision. TomeStack sets the ids, the source, the status and the schema version; the
        /// item gives <c>kind</c>, <c>name</c>, <c>summary</c> and <c>effects</c>, and may give <c>rulesFamilies</c>. Anything
        /// else is ignored with a warning. Effects are read by the normal content reader and checked like any draft.
        /// </summary>
        private static ContentRevision? DraftFrom(JsonNode? item, Guid sourceId, string family, int index, List<Diagnostic> warnings)
        {
            if (item is not JsonObject obj)
            {
                warnings.Add(new("extension.draft-skipped", $"Item {index} is not an object, so it was skipped."));
                return null;
            }
            var kind = DeclarativeTransform.AsText(obj["kind"]);
            if (kind is not ("feat" or "spell" or "item" or "feature"))
            {
                warnings.Add(new("extension.draft-skipped", $"Item {index} has no kind TomeStack imports (feat, spell, item or feature), so it was skipped."));
                return null;
            }
            var name = DeclarativeTransform.AsText(obj["name"])?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 200)
            {
                warnings.Add(new("extension.draft-skipped", $"Item {index} needs a name of 1 to 200 characters, so it was skipped."));
                return null;
            }
            foreach (var extra in obj.Select(p => p.Key).Where(k => k is not ("kind" or "name" or "summary" or "effects" or "rulesFamilies")))
                warnings.Add(new("extension.field-ignored", $"Item {index}: the field '{(extra.Length > 40 ? extra[..40] : extra)}' is not imported."));
            var families = obj["rulesFamilies"] is JsonArray given && given.Count > 0 ? given.DeepClone() : new JsonArray(family);
            var document = new JsonObject
            {
                ["contentId"] = Guid.NewGuid().ToString("D"),
                ["revisionId"] = Guid.NewGuid().ToString("D"),
                ["schemaVersion"] = ContentRevision.CurrentSchemaVersion,
                ["kind"] = kind,
                ["name"] = name,
                ["rulesFamilies"] = families,
                ["provenance"] = new JsonObject { ["sourceId"] = sourceId.ToString("D") },
                ["status"] = "draft",
                ["summary"] = DeclarativeTransform.AsText(obj["summary"]) is { } summary ? summary[..Math.Min(summary.Length, 10_000)] : null,
                ["effects"] = obj["effects"] is JsonArray effects ? effects.DeepClone() : new JsonArray(),
            };
            try
            {
                return document.Deserialize<ContentRevision>(RulesJson.Options);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException or FormatException or ArgumentException or KeyNotFoundException)
            {
                warnings.Add(new("extension.draft-skipped", $"Item {index} ('{name}') is not a content entry TomeStack can read, so it was skipped."));
                return null;
            }
        }

        /// <summary><c>extension.runImport</c>: saves the previewed drafts into their new source, in one transaction.</summary>
        public ExtensionImportResult ApplyExtensionImport(Guid token, bool confirm)
        {
            var run = TakeRun(token, HookKind.Import, confirm);
            var source = run.Source!;
            _store.InTransaction(() =>
            {
                _store.UpsertSource(source);
                foreach (var draft in run.Drafts)
                    _store.AddRevision(draft);
            });
            return new(source.Id, source.Title, run.Drafts.Count);
        }

        /// <summary><c>extension.runExport</c>: the previewed output, once.</summary>
        public (string FileName, byte[] Bytes) ExtensionExportOutput(Guid token)
        {
            var run = TakeRun(token, HookKind.Export, confirm: true);
            return (run.FileName!, run.Output!);
        }

        /// <summary>
        /// <c>extension.runSaveAs</c>, step 1: the previewed output, checked but kept, so cancelling the Save dialog loses
        /// nothing (review fix). <see cref="CompleteExtensionOutput"/> uses the token up once the file is written.
        /// </summary>
        public (string FileName, byte[] Bytes) PeekExtensionOutput(Guid token)
        {
            if (!_pendingRuns.TryGetValue(token, out var run) || run.Kind != HookKind.Export)
                throw new AppValidationException([new("extension.run-expired", "Run the preview again.")]);
            CheckRun(run);
            return (run.FileName!, run.Output!);
        }

        public void CompleteExtensionOutput(Guid token) => _pendingRuns.TryRemove(token, out _);

        private PendingRun TakeRun(Guid token, HookKind kind, bool confirm)
        {
            if (!confirm)
                throw new AppValidationException([new("extension.confirm-required", "Show the run's preview and confirm it first.")]);
            if (!_pendingRuns.TryRemove(token, out var run) || run.Kind != kind)
                throw new AppValidationException([new("extension.run-expired", "Run the preview again.")]);
            CheckRun(run);
            return run;
        }

        /// <summary>Revoked, turned off, replaced, or a permission it used withdrawn since the preview: nothing is written.</summary>
        private void CheckRun(PendingRun run)
        {
            if (_store.FindExtension(run.ExtensionId) is not { Enabled: true } installed || installed.Sha256 != run.Sha256
                || run.Needed.Any(p => !installed.Grants.Contains(p)))
            {
                throw new AppValidationException([new("extension.run-expired", "The extension or its permissions changed, or it was turned off, after the preview. Run the preview again.")]);
            }
        }

        private Guid Remember(PendingRun run)
        {
            KeepFew(_pendingRuns);
            var token = Guid.NewGuid();
            _pendingRuns[token] = run;
            return token;
        }

        /// <summary>A few previews at a time: an abandoned one is dropped rather than kept in memory.</summary>
        private static void KeepFew<T>(ConcurrentDictionary<Guid, T> pending)
        {
            while (pending.Count >= 8 && pending.Keys.FirstOrDefault() is var oldest && oldest != Guid.Empty)
                pending.TryRemove(oldest, out _);
        }

        /// <summary>The sheet export model v1 of a character (ADR-011, ADR-007 item 11), for extensions and adapters.</summary>
        internal SheetExport SheetExportFor(Guid characterId, SheetPurpose purpose)
        {
            var character = _store.FindCharacter(characterId)
                ?? throw new AppValidationException([new("character.not-found", $"Character {characterId} does not exist.")]);
            var sheet = CharacterCalculator.Calculate(character, _store);
            return SheetExportBuilder.Build(character, sheet, _store, purpose, _bundledSources, _time.GetUtcNow());
        }

        /// <summary>
        /// The paths the output scan looks for (ADR-011): the data folder, the profile and every linked PDF. The Windows user
        /// name is searched for separately, as a path segment (<see cref="OutputScan.Leaks"/>).
        /// </summary>
        internal IEnumerable<string?> SensitivePaths()
        {
            yield return Path.GetFullPath(DataDirectory);
            yield return ScanIdentity().Profile;
            foreach (var linked in _store.ListAttachments().Where(a => a.Mode == AttachmentMode.Linked))
                yield return linked.LinkedPath;
        }

        private static string SafeName(string name)
        {
            var cleaned = new string([.. name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-')]).Trim('-');
            return cleaned.Length == 0 ? "export" : cleaned[..Math.Min(cleaned.Length, 60)];
        }

        /// <summary>The store, plus the new source an import run would create (not stored until the run is applied).</summary>
        private sealed class WithSource(IContentCatalog store, SourceRecord source) : IContentCatalog
        {
            public ContentRevision? FindRevision(ContentReference reference) => store.FindRevision(reference);

            public SourceRecord? FindSource(Guid sourceId) => sourceId == source.Id ? source : store.FindSource(sourceId);

            public IEnumerable<ContentRevision> ChoiceExtensions(Guid contentId, string choiceId) => store.ChoiceExtensions(contentId, choiceId);

            public IEnumerable<ContentRevision> RevisionsOf(Guid contentId) => store.RevisionsOf(contentId);
        }
    }
}
