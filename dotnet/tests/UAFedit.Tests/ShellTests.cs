namespace UAFedit.Tests;

/// <summary>The editor's shell exists and its entry point is reachable.</summary>
public class ShellTests
{
    [Fact]
    public void The_app_builder_is_configurable_without_a_display() =>
        Assert.NotNull(UAFedit.Program.BuildAvaloniaApp());
}
