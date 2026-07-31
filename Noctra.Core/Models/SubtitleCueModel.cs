using System.Collections.Generic;

namespace Noctra.Models;

public sealed record SubtitleCueData(string Text)
{
    public static readonly IReadOnlyList<SubtitleCueData> Empty = [];
}
