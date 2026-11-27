using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using RetroIde.Workbench;
using Xunit;

namespace RetroIde.IntegrationTests;

/// <summary>Verifies that empty dock groups yield their layout allocation to the document tabs.</summary>
public sealed class DockAutoHideLayoutTests
{
    /// <remarks>TEST-RETRO-001#verify-ac-behavior-desktop-1; TEST-RETRO-001#verify-ac-behavior-desktop-2.</remarks>
    [AvaloniaTheory]
    [InlineData("explorer", false)]
    [InlineData("registers", false)]
    [InlineData("output", true)]
    public async Task Auto_hidden_group_releases_space_and_unpinning_restores_its_size(string firstTool, bool vertical)
    {
        using var session = new WorkbenchSession(Guid.NewGuid());
        var window = Open(session);
        try
        {
            var layout = session.Layout;
            var tool = layout.Tool(firstTool);
            var owner = Assert.IsAssignableFrom<IDock>(tool.Owner);
            var members = owner.VisibleDockables!.OfType<ShellTool>().ToArray();
            var original = await DocumentExtentAsync(window, vertical);
            layout.Factory.PinDockable(tool);
            Assert.InRange(await DocumentExtentAsync(window, vertical), original - 35, original + 2);

            foreach (var remaining in members.Where(item => !ReferenceEquals(item, tool)))
                layout.Factory.PinDockable(remaining);
            var expanded = await DocumentExtentAsync(window, vertical);
            Assert.True(expanded > original + 80,
                $"After auto-hiding {owner.Id}, the document extent should grow. Before {original:0}, after {expanded:0}.");
            Assert.All(members, item => Assert.True(layout.Factory.IsDockablePinned(item)));

            var saved = layout.Capture();
            using (var restored = new WorkbenchSession(session.View.WorkspaceId, saved))
            {
                var reopened = Open(restored);
                try
                {
                    Assert.InRange(await DocumentExtentAsync(reopened, vertical), expanded - 2, expanded + 2);
                    UnpinAll(restored.Layout, members.Select(item => item.Id));
                    Assert.InRange(await DocumentExtentAsync(reopened, vertical), original - 2, original + 2);
                }
                finally { reopened.Close(); }
            }

            UnpinAll(layout, members.Select(item => item.Id));
            Assert.InRange(await DocumentExtentAsync(window, vertical), original - 2, original + 2);
        }
        finally { window.Close(); }
    }

    private static WorkbenchWindow Open(WorkbenchSession session)
    {
        var window = new WorkbenchWindow(session) { Width = 1200, Height = 800 };
        window.Show();
        session.Layout.SelectDocument("test-results");
        return window;
    }

    private static void UnpinAll(DockLayout layout, IEnumerable<string> ids)
    {
        foreach (var id in ids)
            layout.Factory.UnpinDockable(layout.Tool(id));
    }

    private static async Task<double> DocumentExtentAsync(Window window, bool vertical)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        while (true)
        {
            window.UpdateLayout();
            if (window.GetVisualDescendants().OfType<DocumentDockControl>().SingleOrDefault(candidate =>
                candidate.IsEffectivelyVisible && candidate.Bounds.Width > 0 && candidate.Bounds.Height > 0) is { } control)
                return vertical ? control.Bounds.Height : control.Bounds.Width;

            await Task.Delay(25, deadline.Token);
        }
    }
}
