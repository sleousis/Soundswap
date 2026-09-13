using Soundswap.Core.Model;

namespace Soundswap.Core.Tests;

/// <summary>Test names are sentences about behaviour a player would notice.</summary>
public class StacksTests
{
    [Fact]
    public void Two_part_stacks_that_fit_in_one_are_found()
    {
        var items = new[] { new ItemStack(0, 0, 5, 400, false), new ItemStack(0, 1, 5, 300, false) };

        var splits = Stacks.Splits(items, _ => 999);

        Assert.Single(splits);
    }

    [Fact]
    public void Full_stacks_are_left_alone()
    {
        var items = new[] { new ItemStack(0, 0, 5, 999, false), new ItemStack(0, 1, 5, 999, false) };

        Assert.Empty(Stacks.Splits(items, _ => 999));
    }
}
