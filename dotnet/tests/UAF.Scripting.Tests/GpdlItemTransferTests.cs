using UAF.Scripting;

namespace UAF.Scripting.Tests;

/// <summary>
/// <c>$GIVE_CHAR_ITEM</c> and <c>$TAKE_CHAR_ITEM</c>.
/// </summary>
/// <remarks>
/// Both take an <c>ACTOR</c> and an item name, and both move a <b>whole bundle</b> — the reference
/// passes <c>GetItemBundleQty</c> as the quantity, so giving arrows gives the design's bundle
/// rather than one arrow.
/// </remarks>
public class GpdlItemTransferTests
{
    private sealed class BagHost : GpdlUnhostedEnvironment
    {
        public List<string> Bag { get; } = [];

        /// <summary>The only two items this design has.</summary>
        private static readonly string[] Known = ["Arrow", "Torch"];

        public override bool GiveItem(string actor, string itemId)
        {
            if (!Known.Contains(itemId))
            {
                return false;
            }

            Bag.Add(itemId);
            return true;
        }

        public override bool TakeItem(string actor, string itemId) => Bag.Remove(itemId);
    }

    private static string Run(string body, GpdlUnhostedEnvironment host)
    {
        var compiler = new GpdlCompiler();
        string source = "$PUBLIC $FUNC f() { " + body + " } f;";
        Assert.True(compiler.Compile(source) == 0,
                    "compile failed: " + string.Join("; ", compiler.Errors));

        var vm = new GpdlVirtualMachine(GpdlProgram.FromCompiler(compiler), host);
        string value = vm.Execute("f");
        Assert.Equal(GpdlState.GPDL_IDLE, vm.Status);
        return value;
    }

    /// <summary>Giving an item the design has succeeds and leaves it carried.</summary>
    [Fact]
    public void An_item_the_design_has_can_be_given()
    {
        var host = new BagHost();
        string result = Run(
            """$RETURN $GIVE_CHAR_ITEM($MOST_DAMAGED_ENEMY(), "Torch");""", host);

        Assert.Equal("1", result);
        Assert.Equal(["Torch"], host.Bag);
    }

    /// <summary>
    /// An item the design does not have is refused rather than conjured.
    /// </summary>
    /// <remarks>
    /// The reference locates the item before adding it, so a script cannot invent one by naming
    /// it — the same shape as <c>$SET_CHAR_RACE</c>'s refusal.
    /// </remarks>
    [Fact]
    public void An_unknown_item_is_refused()
    {
        var host = new BagHost();
        string result = Run(
            """$RETURN $GIVE_CHAR_ITEM($MOST_DAMAGED_ENEMY(), "Excalibur");""", host);

        Assert.Equal(string.Empty, result);
        Assert.Empty(host.Bag);
    }

    /// <summary>Taking removes one, and answers whether there was one to take.</summary>
    [Fact]
    public void Taking_removes_one_and_reports()
    {
        var host = new BagHost();
        host.Bag.Add("Arrow");
        host.Bag.Add("Arrow");

        Assert.Equal("1", Run("""$RETURN $TAKE_CHAR_ITEM($MOST_DAMAGED_ENEMY(), "Arrow");""",
                              host));
        Assert.Equal(["Arrow"], host.Bag);

        Assert.Equal("1", Run("""$RETURN $TAKE_CHAR_ITEM($MOST_DAMAGED_ENEMY(), "Arrow");""",
                              host));
        Assert.Empty(host.Bag);

        // And nothing left to take.
        Assert.Equal(string.Empty,
                     Run("""$RETURN $TAKE_CHAR_ITEM($MOST_DAMAGED_ENEMY(), "Arrow");""", host));
    }

    /// <summary>
    /// The two arguments are not interchangeable.
    /// </summary>
    /// <remarks>
    /// Both are on the stack and the item name pops first, so a crossed pair would ask the host to
    /// give an actor-named item to an item-named actor — and the host would refuse, which is why
    /// this asserts the success rather than only the absence of a throw.
    /// </remarks>
    [Fact]
    public void The_actor_and_the_item_are_not_crossed()
    {
        var host = new BagHost();

        Assert.Equal("1", Run("""$RETURN $GIVE_CHAR_ITEM($MOST_DAMAGED_ENEMY(), "Arrow");""",
                              host));
        Assert.Equal(["Arrow"], host.Bag);
    }

    /// <summary>An unhosted environment carries nothing and says so.</summary>
    [Fact]
    public void An_unhosted_environment_gives_nothing()
    {
        var host = new GpdlUnhostedEnvironment();

        Assert.Equal(string.Empty,
                     Run("""$RETURN $GIVE_CHAR_ITEM($MOST_DAMAGED_ENEMY(), "Torch");""", host));
        Assert.Equal(string.Empty,
                     Run("""$RETURN $TAKE_CHAR_ITEM($MOST_DAMAGED_ENEMY(), "Torch");""", host));
    }
}
