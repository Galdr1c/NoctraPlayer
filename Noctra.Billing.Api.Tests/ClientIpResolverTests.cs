using Noctra.Billing.Api;
using Xunit;

namespace Noctra.Billing.Api.Tests;

public class ClientIpResolverTests
{
    [Fact]
    public void UsesLastForwardedForValue()
    {
        // Cloud Run/GFE, istemcinin gönderdiği değerlerin SONUNA gerçek IP'yi
        // ekler; bu yüzden son değer platform güvenilir bilgisidir.
        Assert.Equal("203.0.113.7", ClientIpResolver.GetClientIp("10.0.0.1, 203.0.113.7", "10.0.0.1"));
    }

    [Fact]
    public void IgnoresSpoofedFirstValue()
    {
        // Sahte ilk değer (10.0.0.1) yok sayılır; GFE'nin eklediği son değer kullanılır.
        Assert.Equal("198.51.100.9", ClientIpResolver.GetClientIp("10.0.0.1, 198.51.100.9", "10.0.0.1"));
    }

    [Fact]
    public void TrimsWhitespaceAroundValues()
    {
        Assert.Equal("198.51.100.9", ClientIpResolver.GetClientIp("10.0.0.1 ,   198.51.100.9 ", "10.0.0.1"));
    }

    [Fact]
    public void SkipsTrailingEmptySegments()
    {
        Assert.Equal("198.51.100.9", ClientIpResolver.GetClientIp("10.0.0.1, 198.51.100.9, ", "10.0.0.1"));
    }

    [Fact]
    public void FallsBackToRemoteIpWhenNoForwardedHeader()
    {
        Assert.Equal("172.16.0.5", ClientIpResolver.GetClientIp(null, "172.16.0.5"));
        Assert.Equal("172.16.0.5", ClientIpResolver.GetClientIp("", "172.16.0.5"));
        Assert.Equal("172.16.0.5", ClientIpResolver.GetClientIp(",", "172.16.0.5"));
    }

    [Fact]
    public void UsesUnknownWhenNeitherPresent()
    {
        Assert.Equal("unknown", ClientIpResolver.GetClientIp(null, null));
    }
}
