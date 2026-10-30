namespace TomeStack.AppService.Packages;

/// <summary>
/// One line of what "Save for an older TomeStack" (D85) changes in an export, shown in the preview and hashed into
/// <c>changesHash</c> so a save presents exactly the list the user confirmed.
/// </summary>
/// <param name="Entry">The package path the change concerns (<c>characters/&lt;id&gt;.json</c>, <c>content/&lt;id&gt;.json</c>).</param>
/// <param name="Kind"><c>character</c> or <c>content</c>.</param>
/// <param name="SourceTitle">For content, the title of the source it belongs to.</param>
/// <param name="Action"><c>dropped</c>, <c>blocks</c>, <c>copied</c> or <c>leftOut</c>.</param>
/// <param name="Code"><c>package.target-drops</c> for a dropped change, <c>package.target-blocked</c> for a blocking one.</param>
public sealed record ExportChange(string Entry, string Kind, Guid Id, string Name, string? SourceTitle,
    string Action, string Code, string Message);
