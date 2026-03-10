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
            var channels = new List<Channel> 
            { 
                new Channel { Name = "ⓣⓥ | FRANCE ULTRA HD" }, 
                new Channel { Name = "FRANCE 2" }, 
                new Channel { Name = "FRANCE 3" } 
            };
            
            var result = service.DetectCountry(channels);
            
            Assert.Equal("FR", result);
        }

        [Fact]
        public void DetectCountry_WithCircledLetters_NormalizesAndDetects()
        {
            var service = new LanguageDetectionService();
            // ⓣⓡ -> TR
            var channels = new List<Channel> 
            { 
                new Channel { Name = "ⓣⓡ | KANAL D" }, 
                new Channel { Name = "ⓣⓡ | STAR TV" } 
            };
            
            var result = service.DetectCountry(channels);
            
            Assert.Equal("TR", result);
        }

        [Fact]
        public void DetectCountry_WithBrackets_ReturnsCorrectCode()
        {
            var service = new LanguageDetectionService();
            var channels = new List<Channel> 
            { 
                new Channel { Name = "[DE] RTL" }, 
                new Channel { Name = "[DE] PROSIEBEN" } 
            };
            
            var result = service.DetectCountry(channels);
            
            Assert.Equal("DE", result);
        }

        [Fact]
        public void DetectCountries_ReturnsMultiCountryList()
        {
            var service = new LanguageDetectionService();
            var channels = new List<Channel> 
            { 
                new Channel { Name = "FRANCE 2" }, 
                new Channel { Name = "FRANCE 3" }, 
                new Channel { Name = "TRT 1" }, 
                new Channel { Name = "KANAL D" }, 
                new Channel { Name = "ATV" },
                new Channel { Name = "BBC ONE" }, 
                new Channel { Name = "BBC TWO" }
            };
            
            var results = service.DetectCountries(channels);
            
            // TR (3), FR (2), GB (2)
            Assert.Equal("TR", results[0].CountryCode);
            Assert.Contains(results, r => r.CountryCode == "FR");
            Assert.Contains(results, r => r.CountryCode == "GB");
        }
    }
}
