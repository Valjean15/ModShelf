using System.Net;

using ModShelf.Presentation.Common;
using ModShelf.Sources.Abstractions.Accounts;

namespace ModShelf.Presentation.Tests.Common;

public sealed class SiteProblemsTests
{
    private const string Failed = "{0} couldn't search: {1}";

    public static TheoryData<Exception> SitesFailing() =>
    [
        new AuthenticationFailedException("Nexus Mods refused the account."),
        new HttpRequestException("Service unavailable.", null, HttpStatusCode.ServiceUnavailable),
        new HttpRequestException("No route to host."),
        new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.", new TimeoutException()),
    ];

    public static TheoryData<Exception> OtherFailures() =>
    [
        new TaskCanceledException(),
        new OperationCanceledException(),
        new InvalidOperationException("Something else."),
        new IOException("The disk is full."),
    ];

    [Theory]
    [MemberData(nameof(SitesFailing))]
    public void TheSiteRefusingFailingOrNotAnsweringInTimeIsTheSitesProblem(Exception exception)
        => Assert.True(SiteProblems.IsSiteProblem(exception));

    [Theory]
    [MemberData(nameof(OtherFailures))]
    public void CancellingOrAnotherFailureIsnt(Exception exception)
    {
        Assert.False(SiteProblems.IsSiteProblem(exception));
        Assert.Null(SiteProblems.Describe(exception, "Nexus Mods", Failed));
    }

    [Fact]
    public void ARefusedAccountSaysToLinkIt()
        => Assert.Equal(
            "Nexus Mods needs your account linked, or refused the one linked. Link it on the Accounts screen, then try again.",
            SiteProblems.Describe(new AuthenticationFailedException("Refused."), "Nexus Mods", Failed));

    [Fact]
    public void AnErrorSaysWhatTheSiteSaid()
        => Assert.Equal(
            "Nexus Mods couldn't search: Service unavailable.",
            SiteProblems.Describe(new HttpRequestException("Service unavailable.", null, HttpStatusCode.ServiceUnavailable), "Nexus Mods", Failed));

    [Fact]
    public void ATimeoutIsToldAsAnError()
        => Assert.Equal(
            "Nexus Mods couldn't search: It didn't answer in time.",
            SiteProblems.Describe(new TaskCanceledException("It didn't answer in time.", new TimeoutException()), "Nexus Mods", Failed));
}
