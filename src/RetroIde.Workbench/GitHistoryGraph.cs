using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using RetroIde.Application.Git;

namespace RetroIde.Workbench;

/// <summary>Assigns commits and their parent edges to visible graph lanes in topological order.</summary>
internal static class GitHistoryGraph
{
    internal sealed record Row(GitHistoryOperations.Commit Commit, int Lane, bool ConnectedAbove, int[] Through, int[] Parents, int Width);

    internal static Row[] Build(GitHistoryOperations.Commit[] commits)
    {
        var pending = new List<string?>();
        var rows = new List<Row>(commits.Length);
        var width = 24;
        foreach (var commit in commits)
        {
            var lane = pending.IndexOf(commit.Id);
            var connected = lane >= 0;
            if (!connected)
                lane = Put(pending, commit.Id);
            var through = Enumerable.Range(0, pending.Count).Where(i => i != lane && pending[i] is not null).ToArray();
            pending[lane] = null;
            var parents = new List<int>(commit.Parents.Length);
            foreach (var parent in commit.Parents)
            {
                var existing = pending.IndexOf(parent);
                parents.Add(existing >= 0 ? existing : Put(pending, parent, lane));
            }
            width = Math.Max(width, pending.Count * 14 + 10);
            rows.Add(new(commit, lane, connected, through, [.. parents], 0));
        }
        return [.. rows.Select(row => row with { Width = width })];
    }

    private static int Put(List<string?> pending, string id, int preferred = -1)
    {
        var lane = preferred >= 0 && pending[preferred] is null ? preferred : pending.IndexOf(null);
        if (lane < 0)
        {
            lane = pending.Count;
            pending.Add(id);
        }
        else
        {
            pending[lane] = id;
        }
        return lane;
    }

    internal sealed class Cell(Row row) : Control
    {
        private static readonly Pen[] Pens =
        [
            new(Brushes.LightSkyBlue, 1.5), new(Brushes.LightGreen, 1.5), new(Brushes.Plum, 1.5), new(Brushes.Orange, 1.5),
        ];

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            var midpoint = Bounds.Height / 2;
            var center = new Point(X(row.Lane), midpoint);
            foreach (var lane in row.Through)
                context.DrawLine(Color(lane), new Point(X(lane), 0), new Point(X(lane), Bounds.Height));
            if (row.ConnectedAbove)
                context.DrawLine(Color(row.Lane), new Point(center.X, 0), center);
            foreach (var parent in row.Parents)
                context.DrawLine(Color(parent), center, new Point(X(parent), Bounds.Height));
            context.DrawEllipse(Color(row.Lane).Brush, null, center, 3.5, 3.5);
        }

        private static double X(int lane) => 8 + lane * 14;
        private static Pen Color(int lane) => Pens[lane % Pens.Length];
    }
}
