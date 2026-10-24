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
                // Information only: no Content or EffectId, so it stays out of the feature's own list and never downgrades its automation.
                diagnostics.Add(new("toggle.group-absent", $"Toggle '{item.Toggle.ToggleId}' joins group '{group}', whose content is not on this character; it is a group of one."));
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
                var allowed = OverlayAllowed(overlay.Toggle.AllowedWith, overlay.Toggle.AllowedInBase, overlay.ContentId, kept is null ? null : (kept.ContentId, kept.Toggle.ToggleId));
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

    /// <summary>
    /// The one rule for an overlay: allowed when no primary of its group is on and <c>allowedInBase</c> is true, or when the primary that
    /// is on is one <c>allowedWith</c> names (a reference without a content means the overlay's own).
    /// </summary>
    private static bool OverlayAllowed(IReadOnlyList<ToggleRef>? allowedWith, bool? allowedInBase, Guid overlayContent, (Guid ContentId, string ToggleId)? primary) =>
        primary is not { } on
            ? allowedInBase == true
            : allowedWith?.Any(r => (r.ContentId ?? overlayContent) == on.ContentId && r.ToggleId == on.ToggleId) == true;

    /// <summary>
    /// The primaries of <paramref name="toggle"/>'s group that are on beside it, which switching it on turns off (the S1 switch), in
    /// <paramref name="toggles"/> order. Overlays are never occupants. <paramref name="groupPresent"/> is false when the content that declares
    /// the group is not on the character (a group of one: nothing is exclusive with it). This is the rule the toggle command and a
    /// damage response's <c>toggleOn</c> share.
    /// </summary>
    public static IReadOnlyList<ToggleValue> Occupants(ToggleValue toggle, IReadOnlyList<ToggleValue> toggles, bool groupPresent)
    {
        ArgumentNullException.ThrowIfNull(toggle);
        ArgumentNullException.ThrowIfNull(toggles);
        return !groupPresent
            ? []
            : [.. toggles.Where(t => t.On && t.Group == toggle.Group && t.GroupContent == toggle.GroupContent && t.Layer != ToggleLayer.Overlay
                && !(t.Content.ContentId == toggle.Content.ContentId && t.ToggleId == toggle.ToggleId))];
    }

    /// <summary>
    /// True when <paramref name="overlay"/> may be on with the form that is on now in <paramref name="toggles"/> (none: the base character).
    /// A toggle that is not an overlay of a group is always allowed.
    /// </summary>
    public static bool OverlayAllowedNow(ToggleValue overlay, IReadOnlyList<ToggleValue> toggles)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(toggles);
        if (overlay.Group is null || overlay.Layer != ToggleLayer.Overlay)
            return true;
        var on = toggles.FirstOrDefault(t => t.On && t.Group == overlay.Group && t.GroupContent == overlay.GroupContent && t.Layer != ToggleLayer.Overlay);
        return OverlayAllowed(overlay.AllowedWith, overlay.AllowedInBase, overlay.Content.ContentId, on is null ? null : (on.Content.ContentId, on.ToggleId));
    }

    /// <summary>
    /// The overlays of <paramref name="primary"/>'s group that are on (or only stored, when <paramref name="stored"/> is given) and are not
    /// allowed with <paramref name="primary"/>: they end when it is switched on in place of the form that was on.
    /// </summary>
    public static IReadOnlyList<ToggleValue> OverlaysEndedBySwitchingOn(ToggleValue primary, IReadOnlyList<ToggleValue> toggles, IReadOnlySet<(Guid ContentId, string ToggleId)>? stored)
    {
        ArgumentNullException.ThrowIfNull(primary);
        return EndingWith(primary, toggles, stored, primary);
    }

    /// <summary>
    /// The overlays that are on and no longer allowed once <paramref name="ending"/> is off, in <paramref name="toggles"/> order. Only a
    /// primary of a group takes overlays with it. <paramref name="toggles"/> is the state now (the <c>On</c> flags): the primary of the
    /// group that is still on beside <paramref name="ending"/>, if any, is the one the overlays must be allowed with; none means the
    /// base character. This is the rule the toggle command and the turn planner share.
    /// </summary>
    public static IReadOnlyList<ToggleValue> EndingWith(ToggleValue ending, IReadOnlyList<ToggleValue> toggles) => EndingWith(ending, toggles, null);

    /// <summary>
    /// As <see cref="EndingWith(ToggleValue, IReadOnlyList{ToggleValue})"/>, and an overlay that is only stored (not on, because it was
    /// not allowed with the form that was on) counts too, so it cannot come back on silently later. <paramref name="stored"/> holds the
    /// (content id, toggle id) of every stored toggle.
    /// </summary>
    public static IReadOnlyList<ToggleValue> EndingWith(ToggleValue ending, IReadOnlyList<ToggleValue> toggles, IReadOnlySet<(Guid ContentId, string ToggleId)>? stored) =>
        EndingWith(ending, toggles, stored, null);

    /// <summary>
    /// As the overload with <c>stored</c>, with the primary the overlays must be allowed with given explicitly: <paramref name="replacement"/>,
    /// the form that takes the place of <paramref name="ending"/> (a switch). Null means the primary of the group still on beside
    /// <paramref name="ending"/>, if any, else the base character.
    /// </summary>
    public static IReadOnlyList<ToggleValue> EndingWith(ToggleValue ending, IReadOnlyList<ToggleValue> toggles, IReadOnlySet<(Guid ContentId, string ToggleId)>? stored, ToggleValue? replacement)
    {
        ArgumentNullException.ThrowIfNull(ending);
        ArgumentNullException.ThrowIfNull(toggles);
        if (ending.Group is null || ending.Layer == ToggleLayer.Overlay)
            return [];
        bool Same(ToggleValue t) => t.Content.ContentId == ending.Content.ContentId && t.ToggleId == ending.ToggleId;
        bool InGroup(ToggleValue t) => t.Group == ending.Group && t.GroupContent == ending.GroupContent;
        var remaining = replacement ?? toggles.FirstOrDefault(t => t.On && InGroup(t) && t.Layer != ToggleLayer.Overlay && !Same(t));
        (Guid, string)? primary = remaining is null ? null : (remaining.Content.ContentId, remaining.ToggleId);
        return [.. toggles.Where(t => (t.On || stored?.Contains((t.Content.ContentId, t.ToggleId)) == true) && InGroup(t) && t.Layer == ToggleLayer.Overlay && !OverlayAllowed(t.AllowedWith, t.AllowedInBase, t.Content.ContentId, primary))];
    }
}
