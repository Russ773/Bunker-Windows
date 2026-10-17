using System.Text.Json;
using Bunker;
using Xunit;

public class WidgetSubmissionTests
{
    private const string Good = """
    {
      "id": "resource-drop",
      "name": "Resource Drop",
      "author": "Example Author",
      "version": "1.0.0",
      "description": "Reward everyone online with a resource.",
      "controls": [
        { "type": "button", "text": "Drop", "commands": [ "say hello", "kick 76561198000000001" ] }
      ]
    }
    """;

    [Fact]
    public void A_complete_widget_passes_and_lists_its_commands_with_their_risk()
    {
        var check = WidgetSubmission.Check(Good);
        Assert.True(check.Ok);
        Assert.Equal("resource-drop", check.Def!.Id);
        Assert.Equal(2, check.Commands.Count);
        Assert.Contains(check.Commands, c => c.Command == "say hello" && c.Risk == CommandRisk.Normal);
        Assert.Contains(check.Commands, c => c.Command.StartsWith("kick") && c.Risk == CommandRisk.Warning);
    }

    [Theory]
    [InlineData("\"author\": \"Example Author\",", "", "author")]
    [InlineData("\"version\": \"1.0.0\",", "\"version\": \"one\",", "version")]
    [InlineData("\"description\": \"Reward everyone online with a resource.\",", "", "description")]
    public void The_library_needs_an_author_a_proper_version_and_a_description(string from, string to, string mentions)
    {
        var check = WidgetSubmission.Check(Good.Replace(from, to));
        Assert.False(check.Ok);
        Assert.Contains(check.Errors, e => e.Contains(mentions, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Widgets_that_break_the_normal_rules_are_refused_with_the_reasons()
    {
        var check = WidgetSubmission.Check(Good.Replace("kick 76561198000000001", "ownerid 765 admin"));
        Assert.False(check.Ok);
        Assert.Contains(check.Errors, e => e.Contains("ownerid"));
        Assert.False(WidgetSubmission.Check("not json at all").Ok);
        Assert.False(WidgetSubmission.Check("").Ok);
    }

    [Fact]
    public void The_address_opens_the_widget_form_with_everything_filled_in()
    {
        var def = WidgetSubmission.Check(Good).Def!;
        var url = WidgetSubmission.IssueUrl(Good, def, "tested on Carbon & Oxide", out var included);
        Assert.True(included);
        Assert.StartsWith("https://github.com/Russ773/Bunker-Windows/issues/new?template=widget-submission.yml", url);
        Assert.Contains("title=Widget%3A%20Resource%20Drop%201.0.0", url);
        Assert.Contains("widget_name=Resource%20Drop", url);
        Assert.Contains("widget_notes=tested%20on%20Carbon%20%26%20Oxide", url);
        Assert.Contains("widget_json=", url);
        Assert.DoesNotContain(" ", url);
        Assert.DoesNotContain("\n", url);
    }

    [Fact]
    public void Notes_are_left_out_when_empty()
    {
        var def = WidgetSubmission.Check(Good).Def!;
        Assert.DoesNotContain("widget_notes", WidgetSubmission.IssueUrl(Good, def, "  ", out _));
    }

    [Fact]
    public void A_widget_too_long_for_an_address_is_left_out_so_it_can_be_pasted()
    {
        var def = WidgetSubmission.Check(Good).Def!;
        var big = Good.Replace("Reward everyone online with a resource.", new string('x', 9000));
        var url = WidgetSubmission.IssueUrl(big, def, null, out var included);
        Assert.False(included);
        Assert.DoesNotContain("widget_json", url);
        Assert.True(url.Length <= WidgetSubmission.MaxUrlLength);
    }

    [Fact]
    public void The_result_for_the_check_on_GitHub_carries_the_widget_and_every_command()
    {
        var json = WidgetSubmission.ResultJson(WidgetSubmission.Check(Good));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("resource-drop", root.GetProperty("id").GetString());
        Assert.Equal("1.0.0", root.GetProperty("version").GetString());
        Assert.Equal(2, root.GetProperty("commands").GetArrayLength());
        Assert.Equal("Warning", root.GetProperty("commands")[1].GetProperty("risk").GetString());
    }

    [Fact]
    public void The_command_line_check_writes_the_result_and_returns_whether_it_passed()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bunker-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var input = Path.Combine(dir, "w.json");
            var output = Path.Combine(dir, "r.json");
            File.WriteAllText(input, Good);
            Assert.Equal(0, WidgetCheck.Run(input, output));
            Assert.True(JsonDocument.Parse(File.ReadAllText(output)).RootElement.GetProperty("ok").GetBoolean());

            File.WriteAllText(input, "{ broken");
            Assert.Equal(1, WidgetCheck.Run(input, output));
            Assert.False(JsonDocument.Parse(File.ReadAllText(output)).RootElement.GetProperty("ok").GetBoolean());

            Assert.Equal(1, WidgetCheck.Run(Path.Combine(dir, "missing.json"), output)); // a file that is not there is a failed check, not a crash
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void The_widgets_that_ship_with_Bunker_all_pass_the_library_checks_except_for_having_an_author_where_one_is_missing()
    {
        var folder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "widgets"));
        if (!Directory.Exists(folder)) return; // the widgets folder is not next to the test run
        foreach (var file in Directory.GetFiles(folder, "*.json").Where(f => !f.EndsWith("index.json")))
            Assert.True(WidgetSubmission.Check(File.ReadAllText(file)).Ok, Path.GetFileName(file));
    }
}
