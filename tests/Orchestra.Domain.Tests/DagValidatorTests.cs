using Orchestra.Domain;

namespace Orchestra.Domain.Tests;

public class DagValidatorTests
{
    [Fact]
    public void Valid_diamond_has_no_errors_and_respects_dependencies()
    {
        var tasks = new[] { T.Task("a"), T.Task("b", "a"), T.Task("c", "a"), T.Task("d", "b", "c") };

        Assert.Empty(DagValidator.Validate(tasks));

        var order = DagValidator.TopologicalOrder(tasks).ToList();
        Assert.True(order.IndexOf("a") < order.IndexOf("b"));
        Assert.True(order.IndexOf("a") < order.IndexOf("c"));
        Assert.True(order.IndexOf("b") < order.IndexOf("d"));
        Assert.True(order.IndexOf("c") < order.IndexOf("d"));
    }

    [Fact]
    public void Empty_workflow_is_rejected()
        => Assert.NotEmpty(DagValidator.Validate(Array.Empty<TaskDefinition>()));

    [Fact]
    public void Duplicate_names_are_rejected()
        => Assert.Contains(DagValidator.Validate(new[] { T.Task("a"), T.Task("a") }),
            e => e.Contains("Duplicate"));

    [Fact]
    public void Unknown_dependency_is_rejected()
        => Assert.Contains(DagValidator.Validate(new[] { T.Task("a", "ghost") }),
            e => e.Contains("unknown"));

    [Fact]
    public void Self_dependency_is_rejected()
        => Assert.Contains(DagValidator.Validate(new[] { T.Task("a", "a") }),
            e => e.Contains("itself"));

    [Fact]
    public void Cycle_is_detected()
    {
        var errors = DagValidator.Validate(new[] { T.Task("a", "c"), T.Task("b", "a"), T.Task("c", "b"), T.Task("ok") });
        var error = Assert.Single(errors);
        Assert.Contains("cycle", error);
        Assert.DoesNotContain("ok", error);
    }

    [Fact]
    public void Duplicate_dependency_entries_do_not_break_sorting()
        => Assert.Equal(2, DagValidator.TopologicalOrder(new[] { T.Task("a"), T.Task("b", "a", "a") }).Count);

    [Fact]
    public void TopologicalOrder_throws_for_cycle()
        => Assert.Throws<InvalidOperationException>(() =>
            DagValidator.TopologicalOrder(new[] { T.Task("a", "b"), T.Task("b", "a") }));
}
