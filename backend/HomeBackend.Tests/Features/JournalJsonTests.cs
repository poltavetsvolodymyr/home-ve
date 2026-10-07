using System.Text.Json;
using HomeBackend.Features.Logs;

namespace HomeBackend.Tests.Features;

public class JournalJsonTests
{
    [Theory]
    [InlineData(null, "--no-pager -o json -r -n 200")]
    [InlineData("kernel", "--no-pager -o json -r -n 200 -k")]
    [InlineData("dnsmasq", "--no-pager -o json -r -n 200 -u dnsmasq")]
    public void Command_line_selects_the_unit(string? unit, string expected) =>
        Assert.Equal(expected, string.Join(' ', JournalJson.Arguments(unit, 200)));

    [Fact]
    public void Parses_a_journal_line()
    {
        var entry = JournalJson.ParseLine("""
            {"__REALTIME_TIMESTAMP":"1791100000123456","PRIORITY":"3","SYSLOG_IDENTIFIER":"pppd","_COMM":"pppd-bin","MESSAGE":"LCP: timeout"}
            """);

        Assert.Equal(new LogEntry(DateTimeOffset.FromUnixTimeMilliseconds(1791100000123), 3, "pppd", "LCP: timeout"), entry);
    }

    [Fact]
    public void Non_utf8_message_arrives_as_bytes()
    {
        var entry = JournalJson.ParseLine("""{"__REALTIME_TIMESTAMP":"0","_COMM":"dnsmasq","MESSAGE":[72,105,32,226,128,148]}""");

        Assert.Equal("Hi —", entry.Message);
        Assert.Equal("dnsmasq", entry.Source); // no SYSLOG_IDENTIFIER: the process name
        Assert.Equal(6, entry.Priority);       // no PRIORITY: info
    }

    [Fact]
    public void A_line_that_is_not_json_throws() =>
        Assert.ThrowsAny<JsonException>(() => JournalJson.ParseLine("-- No entries --"));
}
