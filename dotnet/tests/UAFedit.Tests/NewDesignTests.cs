using UAFcore;
using UAFedit.ViewModels;

namespace UAFedit.Tests;

/// <summary>
/// File &gt; New: copying the template into a new folder and opening the copy.
/// </summary>
/// <remarks>
/// <b>The template is committed</b>, so unlike most of this project's corpus tests these really
/// run on a bare checkout.
/// </remarks>
public sealed class NewDesignTests : IDisposable
{
    private readonly string scratch =
        Path.Combine(Path.GetTempPath(), $"uafedit-new-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(scratch))
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    private string Fresh(string name) => Path.Combine(scratch, name);

    /// <summary>The premise: the template can be found at all.</summary>
    [Fact]
    public void The_template_is_found()
    {
        Assert.NotNull(DesignTemplate.Locate());
        Assert.True(DesignTemplate.IsDesign(DesignTemplate.Locate()!));
    }

    /// <summary>A new design is created and opens.</summary>
    [Fact]
    public void A_new_design_is_created_and_opened()
    {
        using var model = new MainWindowViewModel();
        string root = Fresh("MyDesign.dsn");

        Assert.Contains("New design created", model.New(root), StringComparison.Ordinal);

        Assert.True(DesignTemplate.IsDesign(root));
        Assert.NotEmpty(model.Roots);
        Assert.Contains("DefaultDesign", model.Title, StringComparison.Ordinal);
    }

    /// <summary>
    /// The template itself is not touched.
    /// </summary>
    /// <remarks>
    /// <b>The reason the port copies rather than opening in place.</b> The reference points its
    /// runtime at the template and relies on Save As; this editor's Save writes back to the folder
    /// it opened, so opening the template would overwrite it the first time anybody pressed
    /// Ctrl+S — and there is only one of it.
    /// </remarks>
    [Fact]
    public void The_template_is_left_alone()
    {
        string template = DesignTemplate.Locate()!;
        var before = Stamps(template);

        using var model = new MainWindowViewModel();
        Assert.Contains("New design created", model.New(Fresh("Untouched.dsn")),
                        StringComparison.Ordinal);

        Assert.Equal(before, Stamps(template));
    }

    /// <summary>An edit to the new design saves, and does not reach the template.</summary>
    /// <remarks>
    /// The whole loop File &gt; New exists for: create, edit, save. It also proves the copy is
    /// writable, which the template only became once its two legacy shapes were converted.
    /// </remarks>
    [Fact]
    public void The_new_design_can_be_edited_and_saved()
    {
        string template = DesignTemplate.Locate()!;
        var before = Stamps(template);
        string root = Fresh("Edited.dsn");

        using (var model = new MainWindowViewModel())
        {
            Assert.Contains("New design created", model.New(root), StringComparison.Ordinal);

            model.SelectedPane = EditorPane.Items;
            model.ItemsPane!.All[0].IdName += " (edited)";

            Assert.Equal("Saved items.dat.", model.Save());
        }

        Assert.Equal(before, Stamps(template));

        // And the edit is really on disk in the new design.
        using var reopened = new MainWindowViewModel();
        Assert.True(reopened.Open(root));
        reopened.SelectedPane = EditorPane.Items;
        Assert.Contains(reopened.ItemsPane!.All,
                        i => i.IdName.EndsWith("(edited)", StringComparison.Ordinal));
    }

    /// <summary>A folder that already holds something is refused.</summary>
    /// <remarks>
    /// A new design that merged itself into an existing one would be indistinguishable from a
    /// corrupted design afterwards.
    /// </remarks>
    [Fact]
    public void A_non_empty_folder_is_refused()
    {
        string root = Fresh("Occupied.dsn");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "something.txt"), "mine");

        using var model = new MainWindowViewModel();

        Assert.Contains("not empty", model.New(root), StringComparison.Ordinal);
        Assert.Empty(model.Roots);               // and nothing was opened
    }

    private static Dictionary<string, (long Length, DateTime Written)> Stamps(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                 .ToDictionary(
                     f => Path.GetRelativePath(root, f),
                     f => (new FileInfo(f).Length, File.GetLastWriteTimeUtc(f)),
                     StringComparer.OrdinalIgnoreCase);
}
