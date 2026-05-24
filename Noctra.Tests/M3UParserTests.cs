using Xunit;
using Noctra.Services;
using Noctra.Models;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace Noctra.Tests
{
    public class M3UParserTests
    {
        private readonly M3UParser _parser;

        public M3UParserTests()
        {
            // HttpClient is not used for ParseAsync(string content), but required by constructor
            _parser = new M3UParser(new HttpClient());
        }

        [Theory]
        [InlineData("#EXTINF:-1,Test Channel\nhttp://test.com/live/user/pass/123.ts", ChannelType.Live)]
        [InlineData("#EXTINF:-1,Test Movie\nhttp://test.com/movie/user/pass/123.mp4", ChannelType.VOD)]
        [InlineData("#EXTINF:-1,Test Series\nhttp://test.com/series/user/pass/123.mp4", ChannelType.Series)]
        [InlineData("#EXTINF:-1 group-title=\"Live TV\",Channel 1\nhttp://example.com/1.ts", ChannelType.Live)]
        [InlineData("#EXTINF:-1 group-title=\"Movies\",Movie 1\nhttp://example.com/movie1.mkv", ChannelType.VOD)]
        [InlineData("#EXTINF:-1 group-title=\"Movies\",MovieSmart Turk (576p)\nhttps://example.com/moviesmart/index.m3u8", ChannelType.Live)]
        [InlineData("#EXTINF:-1 group-title=\"Movies\",MovieSmart Turk (576p)\nhttp://playhdnewjj.xyz:8080/recc121412/KVqfhtdJ2nQ7/174", ChannelType.Live)]
        [InlineData("#EXTINF:-1 group-title=\"Series\",Series 1\nhttp://example.com/series1.mp4", ChannelType.Series)]
        [InlineData("#EXTINF:-1 group-title=\"Documentary;Series\",GZT ()\nhttps://example.com/gzt/index.m3u8", ChannelType.Live)]
        [InlineData("#EXTINF:-1 group-title=\"Business;Series\",e\nhttps://example.com/cnbce/master.m3u8?token=1", ChannelType.Live)]
        [InlineData("#EXTINF:-1,Game of Thrones S01E01\nhttp://example.com/got.mp4", ChannelType.Series)]
        [InlineData("#EXTINF:-1,Breaking Bad 1x01\nhttp://example.com/bb.mp4", ChannelType.Series)]
        [InlineData("#EXTINF:-1,Avatar (2009) 1080p\nhttp://example.com/avatar.mp4", ChannelType.VOD)]
        [InlineData("#EXTINF:-1 group-title=\"Belgesel\",Planet Earth\nhttp://example.com/doc.ts", ChannelType.Live)]
        [InlineData("#EXTINF:-1 group-title=\"Belgesel Serisi\",Cosmos S01E01\nhttp://example.com/cosmos.mp4", ChannelType.Series)]
        [InlineData("#EXTINF-1 group-title=Live,No Colon Channel\nhttp://test.com/1.ts", ChannelType.Live)]
        [InlineData("#EXTINF:-1 tvg-id=123 tvg-logo=http://logo.com/1.png,No Quotes\nhttp://test.com/2.ts", ChannelType.Live)]
        [InlineData(" #EXTM3U\n#EXTINF:-1,Leading Space\nhttp://test.com/3.ts", ChannelType.Live)]
        public async Task ParseAsync_ShouldDetectCorrectType(string m3uEntry, ChannelType expectedType)
        {
            // Arrange
            var content = m3uEntry.Contains("#EXTM3U") ? m3uEntry : "#EXTM3U\n" + m3uEntry;

            // Act
            var channels = await _parser.ParseAsync(content);

            // Assert
            Assert.Single(channels);
            Assert.Equal(expectedType, channels[0].Type);
        }

        [Fact]
        public async Task ParseAsync_ShouldHandleComplexPlaylist()
        {
            var m3u = @"#EXTM3U
#EXTINF:-1 tvg-id=""TRT1"" tvg-name=""TRT 1"" tvg-logo=""http://logo.com/trt1.png"" group-title=""Ulusal"",TRT 1
http://server.com/live/user/pass/101.ts
#EXTINF:-1 tvg-logo=""http://logo.com/matrix.jpg"" group-title=""Action Movies"",The Matrix (1999) 1080p
http://server.com/movie/user/pass/202.mp4
#EXTINF:-1 group-title=""Series"",Friends S10E15
http://server.com/series/user/pass/303.mp4";

            var channels = await _parser.ParseAsync(m3u);

            Assert.Equal(3, channels.Count);
            
            var channel1 = channels[0];
            Assert.Equal("TRT 1", channel1.Name);
            Assert.Equal(ChannelType.Live, channel1.Type);
            Assert.Equal("Ulusal", channel1.GroupTitle);

            var channel2 = channels[1];
            Assert.Contains("Matrix", channel2.Name);
            Assert.Equal(ChannelType.VOD, channel2.Type);

            var channel3 = channels[2];
            Assert.Contains("Friends", channel3.Name);
            Assert.Equal(ChannelType.Series, channel3.Type);
        }

        [Fact]
        public async Task ParseAsync_IptvOrgLinearChannels_PreservesOriginalNames()
        {
            var m3u = @"#EXTM3U
#EXTINF:-1 tvg-id=""CNBCe.tr"" tvg-name=""CNBC-e"" group-title=""Business;Series"",CNBC-e
https://example.com/cnbce/master.m3u8
#EXTINF:-1 tvg-id=""GZT.tr"" tvg-name=""GZT ()"" group-title=""Documentary;Series"",GZT ()
https://example.com/gzt/index.m3u8";

            var channels = await _parser.ParseAsync(m3u);

            Assert.Equal(2, channels.Count);
            Assert.Equal("CNBC-e", channels[0].Name);
            Assert.Equal(ChannelType.Live, channels[0].Type);
            Assert.Equal("GZT ()", channels[1].Name);
            Assert.Equal(ChannelType.Live, channels[1].Type);
        }

        [Fact]
        public async Task ParseAsync_ShouldHandleMalformedLinesGracefully()
        {
            var m3u = @"#EXTM3U
#EXTINF:-1,Valid Channel
http://valid.com/1.ts
NOT_A_TAG: Something
#EXTINF:-1,Next Valid
http://valid.com/2.ts
Garbage line here";

            var channels = await _parser.ParseAsync(m3u);

            Assert.Equal(2, channels.Count);
            Assert.Equal("Valid Channel", channels[0].Name);
            Assert.Equal("Next Valid", channels[1].Name);
        }

        [Fact]
        public async Task ParseAsync_ShouldExtractTvgLogoAndId()
        {
            var m3u = @"#EXTM3U
#EXTINF:-1 tvg-id=""CNN"" tvg-logo=""http://cnn.com/logo.png"" group-title=""News"",CNN International
http://cnn.com/live.m3u8";

            var channels = await _parser.ParseAsync(m3u);

            Assert.Single(channels);
            Assert.Equal("http://cnn.com/logo.png", channels[0].LogoUrl);
            Assert.Equal("CNN International", channels[0].Name);
        }
    }
}

