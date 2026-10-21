namespace TomeStack.RulesCore;

/// <summary>
/// A toggle some active content declares, as the group resolution sees it. <paramref name="Reference"/> (optional) lets a diagnostic
/// name the revision it concerns.
/// </summary>
public sealed record DeclaredToggle(Guid ContentId, string ContentName, ToggleEffect Toggle, ContentReference? Reference = null);

/// <summary>
/// Content v11 (D43): which toggles are on once the form groups are applied. Ungrouped toggles are on exactly when stored.
/// </summary>
public sealed class EffectiveToggles
{
    private readonly HashSet<(Guid ContentId, string ToggleId)> _on = [];
    private readonly HashSet<(Guid ContentId, string ToggleId)> _notAllowed = [];

    internal EffectiveToggles() { }

    internal void SetOn(Guid contentId, string toggleId) => _on.Add((contentId, toggleId));

    internal void SetNotAllowed(Guid contentId, string toggleId) => _notAllowed.Add((contentId, toggleId));

    /// <summary>True when the toggle is on and takes effect. A toggle nothing declares is never on.</summary>
    public bool IsOn(Guid contentId, string toggleId) => _on.Contains((contentId, toggleId));

    /// <summary>False for an overlay that may not be on with the form that is on now (or with none), whether or not it is stored.</summary>
    public bool IsAllowed(Guid contentId, string toggleId) => !_notAllowed.Contains((contentId, toggleId));
}

/// <summary>
/// Content v11 (D43): a form group holds at most one primary toggle (the last one stored wins) and the overlays that are allowed
/// with it. Never throws: stored toggles naming content or toggles that are not declared are ignored.
/// </summary>
public static class ToggleGroups
{
    /// <param name="play">The stored play state (toggles in the order they were switched on).</param>
    /// <param name="declared">The toggles of the active content that can be listed; automatic toggles follow their condition and are left out.</param>
    /// <param name="diagnostics">Receives <c>toggle.group-conflict</c>, <c>toggle.overlay-not-allowed</c> and the info <c>toggle.group-absent</c>.</param>
    /// <param name="activeContent">
    /// The content ids active on the character. A grouped toggle whose group content is not among them is a group of one (C6).
    /// Null means every group content is present.
    /// </param>
    public static EffectiveToggles Resolve(PlayState play, IReadOnlyList<DeclaredToggle> declared, List<Diagnostic> diagnostics, IReadOnlySet<Guid>? activeContent = null)
    {
        ArgumentNullException.ThrowIfNull(play);
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(diagnostics);

        // The last position of each stored toggle decides which primary wins.
        var storedAt = new Dictionary<(Guid, string), int>();
        for (var i = 0; i < play.Toggles.Count; i++)
            storedAt[(play.Toggles[i].ContentId, play.Toggles[i].ToggleId)] = i;

        var effective = new EffectiveToggles();
        var groups = new Dictionary<(Guid Content, string Group), List<DeclaredToggle>>();
        var seen = new HashSet<(Guid, string)>();
        foreach (var item in declared)
        {
            if (!seen.Add((item.ContentId, item.Toggle.ToggleId)))
                continue;
            if (item.Toggle.Group is not { } group)
            {
                if (storedAt.ContainsKey((item.ContentId, item.Toggle.ToggleId)))
                    effective.SetOn(item.ContentId, item.Toggle.ToggleId);
                continue;
            }
            var groupContent = item.Toggle.GroupContent ?? item.ContentId;
            if (activeContent is not null && !activeContent.Contains(groupContent))
            {
                // C6: the content that declares the group is not on the character, so this toggle is exclusive with nothing.
                diagnostics.Add(new("toggle.group-absent", $"Toggle '{item.Toggle.ToggleId}' joins group '{group}', whose content {groupContent} is not on this character; it is a group of one.", item.Reference, item.Toggle.Id));
                if (storedAt.ContainsKey((item.ContentId, item.Toggle.ToggleId)))
                    effective.SetOn(item.ContentId, item.Toggle.ToggleId);
                continue;
            }
            if (!groups.TryGetValue((groupContent, group), out var members))
                groups[(groupContent, group)] = members = [];
            members.Add(item);
        }

        foreach (var ((_, group), members) in groups)
        {
            var primaries = members
                .Where(m => m.Toggle.Layer != ToggleLayer.Overlay && storedAt.ContainsKey((m.ContentId, m.Toggle.ToggleId)))
                .OrderBy(m => storedAt[(m.ContentId, m.Toggle.ToggleId)])
                .ToList();
            var kept = primaries.Count > 0 ? primaries[^1] : null;
            if (kept is not null)
                effective.SetOn(kept.ContentId, kept.Toggle.ToggleId);
            foreach (var dropped in primaries.Take(primaries.Count - 1))
            {
                diagnostics.Add(new(
                    "toggle.group-conflict",
                    $"Group '{group}' has {primaries.Count} primaries on; only '{kept!.Toggle.ToggleId}' applies, so '{dropped.Toggle.ToggleId}' does not.",
                    dropped.Reference, dropped.Toggle.Id));
            }

            foreach (var overlay in members.Where(m => m.Toggle.Layer == ToggleLayer.Overlay))
            {
                var allowed = kept is null
                    ? overlay.Toggle.AllowedInBase == true
                    : overlay.Toggle.AllowedWith?.Any(r => (r.ContentId ?? overlay.ContentId) == kept.ContentId && r.ToggleId == kept.Toggle.ToggleId) == true;
                var isStored = storedAt.ContainsKey((overlay.ContentId, overlay.Toggle.ToggleId));
                if (allowed)
                {
                    if (isStored)
                        effective.SetOn(overlay.ContentId, overlay.Toggle.ToggleId);
                    continue;
                }
                effective.SetNotAllowed(overlay.ContentId, overlay.Toggle.ToggleId);
                if (isStored)
                {
                    diagnostics.Add(new(
                        "toggle.overlay-not-allowed",
                        kept is null
                            ? $"Overlay '{overlay.Toggle.ToggleId}' is not allowed with no form on in group '{group}', so it does not apply."
                            : $"Overlay '{overlay.Toggle.ToggleId}' is not allowed with '{kept.Toggle.ToggleId}' in group '{group}', so it does not apply.",
                        overlay.Reference, overlay.Toggle.Id));
                }
            }
        }
        return effective;
    }
}
