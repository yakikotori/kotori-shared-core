using FluentAssertions;
using Kotori.SharedCore.Rebus;

namespace Kotori.SharedCore.Tests;

public class IntegrationEventRetryPolicyTests
{
    private static IntegrationEventRetryPolicy Policy(int immediateAttempts, TimeSpan baseDelay, TimeSpan maxDelay, TimeSpan giveUpAfter)
        => IntegrationEventRetryPolicy.Create(immediateAttempts, baseDelay, maxDelay, giveUpAfter).Unwrap();

    private static List<TimeSpan> DeferralsUntilDeadLetter(IntegrationEventRetryPolicy policy)
    {
        var delays = new List<TimeSpan>();

        while (policy.Decide(delays.Count) is DeferIntegrationEvent defer)
        {
            delays.Add(defer.Delay);
        }

        return delays;
    }

    public static TheoryData<int, TimeSpan, TimeSpan, TimeSpan> InvalidSettings => new()
    {
        { 0, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30) },
        { 3, TimeSpan.Zero, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30) },
        { 3, TimeSpan.FromMinutes(-1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30) },
        { 3, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30) },
        { 3, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(-1) }
    };

    [Theory]
    [MemberData(nameof(InvalidSettings))]
    public void CreateRejectsInvalidSettings(int immediateAttempts, TimeSpan baseDelay, TimeSpan maxDelay, TimeSpan giveUpAfter)
    {
        var result = IntegrationEventRetryPolicy.Create(immediateAttempts, baseDelay, maxDelay, giveUpAfter);

        result.Should().BeOfType<Fail<IntegrationEventRetryPolicy, TextError>>();
    }

    [Fact]
    public void CreateKeepsValidSettings()
    {
        var policy = Policy(3, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30));

        policy.ImmediateAttempts.Should().Be(3);
        policy.BaseDelay.Should().Be(TimeSpan.FromSeconds(30));
        policy.MaxDelay.Should().Be(TimeSpan.FromMinutes(5));
        policy.GiveUpAfter.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void DelaysDoubleFromTheBaseDelayUpToTheMaxStep()
    {
        var policy = Policy(3, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60), TimeSpan.FromHours(1));

        Enumerable.Range(0, 6).Select(policy.Decide).Should().Equal(
            new DeferIntegrationEvent(TimeSpan.FromSeconds(10)),
            new DeferIntegrationEvent(TimeSpan.FromSeconds(20)),
            new DeferIntegrationEvent(TimeSpan.FromSeconds(40)),
            new DeferIntegrationEvent(TimeSpan.FromSeconds(60)),
            new DeferIntegrationEvent(TimeSpan.FromSeconds(60)),
            new DeferIntegrationEvent(TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void DeadLettersOnceTheDeferralsHaveWaitedTheGiveUpTime()
    {
        var policy = Policy(3, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(70));

        policy.Decide(2).Should().Be(new DeferIntegrationEvent(TimeSpan.FromSeconds(40)));
        policy.Decide(3).Should().Be(new DeadLetterIntegrationEvent());
        policy.Decide(50).Should().Be(new DeadLetterIntegrationEvent());
    }

    [Fact]
    public void ZeroGiveUpTimeDeadLettersWithoutDeferring()
    {
        var policy = Policy(3, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5), TimeSpan.Zero);

        policy.Decide(0).Should().Be(new DeadLetterIntegrationEvent());
    }

    [Fact]
    public void NegativeDeferralCountIsTreatedAsNoDeferral()
    {
        var policy = Policy(3, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30));

        policy.Decide(-4).Should().Be(new DeferIntegrationEvent(TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void HugeDeferralCountDeadLettersWithoutOverflow()
    {
        var policy = Policy(3, TimeSpan.FromSeconds(30), TimeSpan.FromHours(1), TimeSpan.FromHours(6));

        policy.Decide(int.MaxValue).Should().Be(new DeadLetterIntegrationEvent());
    }

    [Fact]
    public void ShortLanePresetBacksOffUpToTenMinutesAndGivesUpAfterAboutAnHour()
    {
        var policy = Policy(3, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(10), TimeSpan.FromHours(1));

        var delays = DeferralsUntilDeadLetter(policy);

        delays.Take(6).Should().Equal(
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(4),
            TimeSpan.FromMinutes(8),
            TimeSpan.FromMinutes(10));
        delays.Should().HaveCount(10);
        delays.Aggregate(TimeSpan.Zero, (total, delay) => total + delay).Should().Be(TimeSpan.FromMinutes(65.5));
    }

    [Fact]
    public void NotificationLanePresetGivesUpAfterAboutHalfAnHour()
    {
        var policy = Policy(3, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30));

        var delays = DeferralsUntilDeadLetter(policy);

        delays.Should().Equal(
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(4),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5));
        delays.Aggregate(TimeSpan.Zero, (total, delay) => total + delay).Should().Be(TimeSpan.FromMinutes(32.5));
    }

    [Fact]
    public void LongLanePresetBacksOffUpToAnHourAndGivesUpAfterAboutSixHours()
    {
        var policy = Policy(3, TimeSpan.FromSeconds(30), TimeSpan.FromHours(1), TimeSpan.FromHours(6));

        var delays = DeferralsUntilDeadLetter(policy);

        delays.Max().Should().Be(TimeSpan.FromHours(1));
        delays.Should().HaveCount(12);
        delays.Aggregate(TimeSpan.Zero, (total, delay) => total + delay).Should().Be(TimeSpan.FromMinutes(363.5));
    }
}
