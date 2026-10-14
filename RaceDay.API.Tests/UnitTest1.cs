using Xunit;

namespace RaceDay.API.Tests;

public class UnitTest1
{
    [Fact]
    public void Addition_TwoNumbers_ReturnsCorrectResult()
    {
        // Arrange
        int number1 = 10;
        int number2 = 5;

        // Act
        int result = number1 + number2;

        // Assert
        Assert.Equal(15, result);
    }

    [Fact]
    public void ResultStatus_DefaultValue_IsFinished()
    {
        // Arrange
        var request = new RaceDay.API.Controllers.ResultsController.ResultRequest();

        // Assert
        Assert.Equal("Finished", request.ResultStatus);
    }
}