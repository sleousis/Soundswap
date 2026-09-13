namespace Soundswap.Core.Model;

/// <summary>
/// One stack as the plugin sees it: plain data copied out of the game, never a pointer. The plugin project
/// fills these from the game; everything in Core works on them and can be tested without the game.
/// </summary>
public sealed record ItemStack(uint ContainerId, int Slot, uint ItemId, int Quantity, bool IsHq);

/// <summary>An example of pure logic: stacks of the same item that could be merged.</summary>
public static class Stacks
{
    public static IReadOnlyList<IGrouping<(uint ItemId, bool IsHq), ItemStack>> Splits(IEnumerable<ItemStack> items, Func<uint, int> stackSize) =>
        items.GroupBy(i => (i.ItemId, i.IsHq))
            .Where(g => g.Count() > 1 && g.Sum(i => i.Quantity) <= stackSize(g.Key.ItemId) * (g.Count() - 1))
            .ToList();
}
