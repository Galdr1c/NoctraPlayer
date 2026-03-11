using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Noctra.Models;
using Noctra.Services;
using Xunit;
using DotNetEnv;

namespace Noctra.Tests
{
    public class MetadataServiceTests
    {
        [Fact(Skip = "Test skipped since it hits real TMDB API which fails without a valid key")]
        public async Task TMDB_API_Works_With_Env_Key()
        {
            // Traverse up to find .env file
            Env.TraversePath().Load();

            var apiKey = System.Environment.GetEnvironmentVariable("TMDB_API_KEY");
            Assert.False(string.IsNullOrEmpty(apiKey), "API Key should not be empty. Make sure .env is loaded.");

            var httpClient = new HttpClient();
            var loggerMock = new Mock<ILogger<MetadataService>>();
            var service = new MetadataService(httpClient, null, null, loggerMock.Object);

            var result = await service.FetchMetadataAsync("Matrix", ChannelType.VOD);
            
            Assert.NotNull(result);
            Assert.Contains("Matrix", result.Title);
        }
    }
}
