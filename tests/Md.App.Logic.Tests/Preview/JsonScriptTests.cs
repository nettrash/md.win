using Md.App.Logic.Preview;

namespace Md.App.Logic.Tests.Preview;

/// <summary>
/// <c>ExecuteScriptAsync</c> answers in JSON, and every port that forgot has written the four
/// characters <c>null</c> into a file. Nothing here throws: no answer is an answer.
/// </summary>
public class JsonScriptTests
{
    [Fact]
    public void TheRenderCompleteFlagIsAStringWithItsQuotes()
    {
        Assert.Equal("1", JsonScript.String("\"1\""));
        Assert.Null(JsonScript.Number("\"1\""));       // a JSON string is not a number
    }

    [Fact]
    public void NullIsTheFourCharactersAndDecodesToNothing()
    {
        Assert.Null(JsonScript.Number("null"));
        Assert.Null(JsonScript.String("null"));
        Assert.Null(JsonScript.ScrollMessage("null"));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("1234", 1234)]
    [InlineData("1234.5", 1234.5)]
    [InlineData("-0.25", -0.25)]
    [InlineData("1e3", 1000)]
    public void NumbersDecodeInvariantly(string json, double expected) =>
        Assert.Equal(expected, JsonScript.Number(json));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("undefined")]          // not JSON: a script that threw
    [InlineData("{")]
    [InlineData("true")]
    [InlineData("{\"a\":1}")]
    public void AnythingElseIsNoAnswer(string? json)
    {
        Assert.Null(JsonScript.Number(json));
        Assert.Null(JsonScript.String(json));
    }

    [Fact]
    public void StringsComeBackUnescaped()
    {
        Assert.Equal("a\"b", JsonScript.String("\"a\\\"b\""));
        Assert.Equal("", JsonScript.String("\"\""));
    }

    [Fact]
    public void TheScrollMessageIsFractionAndEcho()
    {
        var message = JsonScript.ScrollMessage("{\"fraction\":0.42,\"echo\":false}");
        Assert.NotNull(message);
        Assert.Equal(0.42, message!.Value.Fraction);
        Assert.False(message.Value.Echo);

        var echoed = JsonScript.ScrollMessage("{\"echo\":true,\"fraction\":1}");
        Assert.NotNull(echoed);
        Assert.True(echoed!.Value.Echo);
        Assert.Equal(1, echoed.Value.Fraction);
    }

    [Theory]
    [InlineData("{\"echo\":false}")]                    // no fraction
    [InlineData("{\"fraction\":\"0.5\",\"echo\":false}")] // fraction as a string
    [InlineData("[0.5,false]")]
    [InlineData("0.5")]
    public void AMessageThatIsNotOneIsIgnored(string json) => Assert.Null(JsonScript.ScrollMessage(json));

    [Fact]
    public void AMissingEchoIsNotAnEcho()
    {
        var message = JsonScript.ScrollMessage("{\"fraction\":0.5}");
        Assert.NotNull(message);
        Assert.False(message!.Value.Echo);
    }
}
