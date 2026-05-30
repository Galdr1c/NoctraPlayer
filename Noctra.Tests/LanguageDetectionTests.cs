using System.Collections.Generic;
using System.Linq;
using Noctra.Models;
using Noctra.Services;
using Xunit;

namespace Noctra.Tests
{
    public class LanguageDetectionTests
    {
        [Fact]
        public void DetectCountry_WithFullCountryName_ReturnsCorrectCode()
        {
            var service = new LanguageDetectionService();
            var names = new List<string> { "ⓣⓥ | FRANCE ULTRA HD", "FRANCE 2", "FRANCE 3" };
            var channels = names.Select(n => new Channel { Name = n }).ToList();
            
            var result = service.DetectCountry(channels);
            
            Assert.Equal("FR", result);
        }

        [Fact]
        public void DetectCountry_WithCircledLetters_NormalizesAndDetects()
        {
            var service = new LanguageDetectionService();
            // ⓣⓡ -> TR
            var names = new List<string> { "ⓣⓡ | KANAL D", "ⓣⓡ | STAR TV" };
            var channels = names.Select(n => new Channel { Name = n }).ToList();
            
            var result = service.DetectCountry(channels);
            
            Assert.Equal("TR", result);
        }

        [Fact]
        public void DetectCountry_WithBrackets_ReturnsCorrectCode()
        {
            var service = new LanguageDetectionService();
            var names = new List<string> { "[DE] RTL", "[DE] PROSIEBEN" };
            var channels = names.Select(n => new Channel { Name = n }).ToList();
            
            var result = service.DetectCountry(channels);
            
            Assert.Equal("DE", result);
        }

        [Fact]
        public void DetectCountries_ReturnsMultiCountryList()
        {
            var service = new LanguageDetectionService();
            var names = new List<string> 
            { 
                "FRANCE 2", "FRANCE 3", 
                "TRT 1", "KANAL D", "ATV",
                "BBC ONE", "BBC TWO"
            };
            var channels = names.Select(n => new Channel { Name = n }).ToList();
            
            var results = service.DetectCountries(channels).ToList();
            
            // TR (3), FR (2), GB (2)
            Assert.Equal("TR", results[0].CountryCode);
            Assert.Contains(results, r => r.CountryCode == "FR");
            Assert.Contains(results, r => r.CountryCode == "GB");
        }

        [Fact]
        public void DetectCountry_WithSameChannelNameDifferentCountries_DistinguishesByPrefix()
        {
            var service = new LanguageDetectionService();
            var channels = new List<Channel>
            {
                new Channel { Name = "|TR| NOW HD" },
                new Channel { Name = "|DE| NOW ! HD" },
                new Channel { Name = "|GB| NOW TV" }
            };
            
            var trChannels = channels.Where(c => c.Name.Contains("|TR|")).ToList();
            var deChannels = channels.Where(c => c.Name.Contains("|DE|")).ToList();
            var gbChannels = channels.Where(c => c.Name.Contains("|GB|")).ToList();
            
            Assert.Equal("TR", service.DetectCountry(trChannels));
            Assert.Equal("DE", service.DetectCountry(deChannels));
            Assert.Equal("GB", service.DetectCountry(gbChannels));
        }

        [Theory]
        [InlineData("|TR| Kanal D", "TR")]
        [InlineData("[DE] RTL", "DE")]
        [InlineData("(FR) TF1", "FR")]
        [InlineData("{UK} BBC", "GB")]
        [InlineData("TR: Star TV", "TR")]
        [InlineData("DE- ProSieben", "DE")]
        [InlineData("FR/ Canal+", "FR")]
        [InlineData("IT| Rai 1", "IT")]
        [InlineData("ES > TVE", "ES")]
        [InlineData("NL » NPO", "NL")]
        [InlineData("RU . Первый", "RU")]
        [InlineData("TR | ATV", "TR")]
        [InlineData("ⓣⓡ | TRT", "TR")]
        [InlineData("TR|Kanal7", "TR")]
        [InlineData("[TR]Show TV", "TR")]
        [InlineData("(TR)FOX", "TR")]
        [InlineData("TR-TV8", "TR")]
        [InlineData("TR:NTV", "TR")]
        [InlineData("TR/Haber", "TR")]
        [InlineData("TR.CNN", "TR")]
        [InlineData("TR NOW HD", "TR")]
        [InlineData("|DE|NOW HD", "DE")]
        [InlineData("DE: nickelodeon [SAT]", "DE")]
        [InlineData("DE: Nicktoons [SAT] [VIP]", "DE")]
        [InlineData("DE: Super RTL [SAT] [VIP]", "DE")]
        [InlineData("TR: Kanal D [HD]", "TR")]
        [InlineData("【TR】Kanal D", "TR")]
        [InlineData("〔DE〕RTL", "DE")]
        [InlineData("Random Channel", "US")] // Fallback
        public void DetectCountry_WithVariousPrefixFormats_ExtractsCorrectly(string channelName, string expectedCountry)
        {
            var service = new LanguageDetectionService();
            var channels = new List<Channel> { new Channel { Name = channelName } };
            
            var result = service.DetectCountry(channels);
            
            Assert.Equal(expectedCountry, result);
        }
    }
}
