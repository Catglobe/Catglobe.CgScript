namespace BlazorWebApp;

/// <summary>One tag entry passed into a tag-cloud script.</summary>
public record TagItem(string Name, int Count);

/// <summary>Result returned by a tag-cloud script.</summary>
public record TagSummary(string TopTag, int TotalCount, bool Truncated);
