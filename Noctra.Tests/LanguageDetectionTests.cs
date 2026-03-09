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
            var channels = new List<string> { "ⓣⓥ | FRANCE ULTRA HD", "FRANCE 2", "FRANCE 3" }.Select(n => new Channel { Name = n }).ToList();
            
            var result = service.DetectCountry(channels);
            
            Assert.Equal("FR", result);
        }

        [Fact]
        public void DetectCountry_WithCircledLetters_NormalizesAndDetects()
        {
            var service = new LanguageDetectionService();
            // ⓣⓡ -> TR
            var channels = new List<string> { "ⓣⓡ | KANAL D", "ⓣⓡ | STAR TV" }.Select(n => new Channel { Name = n }).ToList();
            
            var result = service.DetectCountry(channels);
            
            Assert.Equal("TR", result);
        }

        [Fact]
        public void DetectCountry_WithBrackets_ReturnsCorrectCode()
        {
            var service = new LanguageDetectionService();
            var channels = new List<string> { "[DE] RTL", "[DE] PROSIEBEN" }.Select(n => new Channel { Name = n }).ToList();
            
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
            var results = service.DetectCountries(channels);
            
            // TR (3), FR (2), GB (2)
            Assert.Equal("TR", results[0].CountryCode);
            Assert.True(results.Any(r => r.CountryCode == "FR"));
            Assert.True(results.Any(r => r.CountryCode == "GB"));
        }
    }
}
