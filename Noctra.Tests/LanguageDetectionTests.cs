using System.Collections.Generic;
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
            
            var result = service.DetectCountry(names);
            
            Assert.Equal("FR", result);
        }

        [Fact]
        public void DetectCountry_WithCircledLetters_NormalizesAndDetects()
        {
            var service = new LanguageDetectionService();
            // ⓣⓡ -> TR
            var names = new List<string> { "ⓣⓡ | KANAL D", "ⓣⓡ | STAR TV" };
            
            var result = service.DetectCountry(names);
            
            Assert.Equal("TR", result);
        }

        [Fact]
        public void DetectCountry_WithBrackets_ReturnsCorrectCode()
        {
            var service = new LanguageDetectionService();
            var names = new List<string> { "[DE] RTL", "[DE] PROSIEBEN" };
            
            var result = service.DetectCountry(names);
            
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
            
            var results = service.DetectCountries(names);
            
            // TR (3), FR (2), GB (2)
            Assert.Equal("TR", results[0].CountryCode);
            Assert.True(results.Any(r => r.CountryCode == "FR"));
            Assert.True(results.Any(r => r.CountryCode == "GB"));
        }
    }
}
