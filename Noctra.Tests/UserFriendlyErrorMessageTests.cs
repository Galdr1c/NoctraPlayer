using Moq;
using Noctra.Services;
using Noctra.Services.Interfaces;

namespace Noctra.Tests;

public sealed class UserFriendlyErrorMessageTests
{
    [Theory]
    [InlineData("android_getaddrinfo failed: EAI_NODATA (No address associated with hostname)")]
    [InlineData("No address associated with hostname")]
    [InlineData("NameResolutionFailure")]
    public void FromText_DnsResolutionErrors_ReturnsFriendlyDnsMessage(string rawMessage)
    {
        var localization = new Mock<ILocalizationService>();
        localization
            .Setup(service => service.GetString(It.IsAny<string>()))
            .Returns((string key) => key);
        UserFriendlyErrorMessage.Initialize(localization.Object);

        var message = UserFriendlyErrorMessage.FromText(rawMessage);

        Assert.Equal("Error.Network.Dns", message);
        Assert.DoesNotContain("android_getaddrinfo", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EAI_NODATA", message, StringComparison.OrdinalIgnoreCase);
    }
}
