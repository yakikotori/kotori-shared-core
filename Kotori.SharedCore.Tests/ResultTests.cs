using FluentAssertions;

namespace Kotori.SharedCore.Tests;

public class ResultTests
{
    [Fact]
    public void Result_CorrectBools_WhenOk()
    {
        var result = Result<TextError>.Ok();

        result.IsOk.Should().BeTrue();
        result.IsFail.Should().BeFalse();
    }

    [Fact]
    public void Result_CorrectBools_WhenFail()
    {
        var result = Result<TextError>.Fail(new TextError("Fail"));

        result.IsOk.Should().BeFalse();
        result.IsFail.Should().BeTrue();
    }

    [Fact]
    public void Result_ImplicitConversion_FromErrorToFail()
    {
        Result<TextError> result = new TextError("Implicit fail");

        result.IsOk.Should().BeFalse();
        result.IsFail.Should().BeTrue();
    }

    [Fact]
    public void ResultWithData_CorrectBools_WhenOk()
    {
        var result = Result<string, TextError>.Ok("Cats");

        result.IsOk.Should().BeTrue();
        result.IsFail.Should().BeFalse();
    }

    [Fact]
    public void ResultWithData_CorrectBools_WhenFail()
    {
        var result = Result<string, TextError>.Fail(new TextError("Fail"));

        result.IsOk.Should().BeFalse();
        result.IsFail.Should().BeTrue();
    }

    [Fact]
    public void ResultWithData_ReturnsData_WhenUnwrapOk()
    {
        var result = Result<string, TextError>.Ok("Cats");

        var data = result.Unwrap();

        data.Should().Be("Cats");
    }

    [Fact]
    public void ResultWithData_Throws_WhenUnwrapFail()
    {
        var result = Result<string, TextError>.Fail(new TextError("Fail"));

        result.Invoking(x => x.Unwrap())
            .Should()
            .Throw<InvalidOperationException>();
    }

    [Fact]
    public void ResultWithData_ImplicitConversion_FromDataToOk()
    {
        Result<string, TextError> result = "Implicit cats";

        result.IsOk.Should().BeTrue();
        result.IsFail.Should().BeFalse();
        result.Unwrap().Should().Be("Implicit cats");
    }

    [Fact]
    public void ResultWithData_ImplicitConversion_FromErrorToFail()
    {
        Result<string, TextError> result = new TextError("Implicit fail");

        result.IsOk.Should().BeFalse();
        result.IsFail.Should().BeTrue();
    }
}