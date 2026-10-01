using System.Collections.Generic;
using System.Linq;

namespace KinokoTAS.Core;

public sealed class BookmarkLayoutHistory
{
    private readonly Dictionary<TasProject, Dictionary<long, (FrameBookmark[] Forward, FrameBookmark[] Backward)>>
        _projects = [];

    public void Clear() => _projects.Clear();

    public FrameBookmark[] Apply(TasProject project, IEnumerable<FrameBookmark> bookmarks, FrameLayoutChange change)
    {
        if (!_projects.TryGetValue(project, out var history))
        {
            history = [];
            _projects.Add(project, history);
        }

        history.TryGetValue(change.Id, out var saved);
        var removed = change.Forward ? change.Removed : change.Inserted;
        var inserted = change.Forward ? change.Inserted : change.Removed;
        var current = bookmarks.ToArray();
        var lost = current.Where(mark => mark.Frame >= change.First && mark.Frame < change.First + removed).ToArray();
        var retained = current.Except(lost).Select(mark =>
                mark.Frame >= change.First + removed ? mark with { Frame = mark.Frame + inserted - removed } : mark)
            .ToList();
        retained.AddRange((change.Forward ? saved.Backward : saved.Forward) ?? []);
        history[change.Id] = change.Forward ? (lost, saved.Backward ?? []) : (saved.Forward ?? [], lost);
        return [.. retained.OrderBy(mark => mark.Frame)];
    }
}
