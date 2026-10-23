using Bunker;
using Xunit;

public class SingleInstanceTests
{
    private static string Folder() => @"C:\Test\" + Guid.NewGuid();

    // A Windows mutex can be taken again by the thread that already holds it, so a second launch is played from another thread.
    private static T OnOtherThread<T>(Func<T> work)
    {
        T result = default!;
        var t = new Thread(() => result = work());
        t.Start();
        t.Join();
        return result;
    }

    [Fact]
    public void The_same_data_folder_gives_the_same_name_whatever_the_case_or_trailing_slash()
    {
        Assert.Equal(SingleInstance.Name(@"C:\Users\Me\AppData\Roaming\Bunker"), SingleInstance.Name(@"c:\users\me\appdata\roaming\bunker\"));
        Assert.NotEqual(SingleInstance.Name(@"C:\One"), SingleInstance.Name(@"C:\Two"));
    }

    [Fact]
    public void A_second_copy_is_turned_away_and_the_first_is_told()
    {
        var folder = Folder();
        using var first = SingleInstance.TryStart(folder);
        Assert.NotNull(first);

        using var told = new ManualResetEventSlim();
        first!.Listen(() => told.Set());

        Assert.Null(OnOtherThread(() => SingleInstance.TryStart(folder)));
        Assert.True(told.Wait(TimeSpan.FromSeconds(5)), "the running copy should hear about the second launch");
    }

    [Fact]
    public void A_copy_using_another_data_folder_is_a_separate_Bunker()
    {
        using var first = SingleInstance.TryStart(Folder());
        using var other = OnOtherThread(() => SingleInstance.TryStart(Folder()));
        Assert.NotNull(first);
        Assert.NotNull(other);
    }

    [Fact]
    public void Once_the_first_copy_has_quit_Bunker_can_be_started_again()
    {
        var folder = Folder();
        SingleInstance.TryStart(folder)!.Dispose();
        using var again = OnOtherThread(() => SingleInstance.TryStart(folder));
        Assert.NotNull(again);
    }
}
