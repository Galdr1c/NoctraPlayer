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
        [InlineData("#EXTINF:-1,Test Series S01E01\nhttp://test.com/series/user/pass/123.mp4", ChannelType.Series)]
        [InlineData("#EXTINF:-1 group-title=\"Live TV\",Channel 1\nhttp://example.com/1.ts", ChannelType.Live)]
        [InlineData("#EXTINF:-1 group-title=\"Movies\",Movie 1\nhttp://example.com/movie1.mkv", ChannelType.VOD)]
        [InlineData("#EXTINF:-1 group-title=\"Movies\",MovieSmart Turk (576p)\nhttps://example.com/moviesmart/index.m3u8", ChannelType.Live)]
        [InlineData("#EXTINF:-1 group-title=\"Series\",Series 1 S01E01\nhttp://example.com/series1.mp4", ChannelType.Series)]
        [InlineData("#EXTINF:-1 group-title=\"Movies\",Film Channel\nhttp://example.com/live/user/pass/12345", ChannelType.Live)]
        [InlineData("#EXTINF:-1 group-title=\"Movies\",Breaking Bad S01E01\nhttp://example.com/live/user/pass/12345", ChannelType.Live)]
        [InlineData("#EXTINF:-1 group-title=\"Movies\",Plain Movie\nhttp://example.com/series/user/pass/12345", ChannelType.Series)]
        [InlineData("#EXTINF:-1 group-title=\"Documentary;Series\",GZT ()\nhttps://example.com/gzt/index.m3u8", ChannelType.Live)]
        [InlineData("#EXTINF:-1 group-title=\"Business;Series\",e\nhttps://example.com/cnbce/master.m3u8?token=1", ChannelType.Live)]
        [InlineData("#EXTINF:-1 group-title=\"Series\",48 Hours\nhttps://dai.google.com/linear/hls/event/JUr94WL2QAiVpGNHY5n5dA/master.m3u8", ChannelType.Live)]
        [InlineData("#EXTINF:-1 group-title=\"Movies\",24 Hour Free Movies (720p)\nhttps://example.com/free-movies/master.m3u8", ChannelType.Live)]
        [InlineData("#EXTINF:-1,Game of Thrones S01E01\nhttp://example.com/got.mp4", ChannelType.Series)]
        [InlineData("#EXTINF:-1,Breaking Bad 1x01\nhttp://example.com/bb.mp4", ChannelType.Series)]
        [InlineData("#EXTINF:-1,Avatar (2009) 1080p\nhttp://example.com/avatar.mp4", ChannelType.VOD)]
        [InlineData("#EXTINF:-1 group-title=\"Belgesel\",Planet Earth\nhttp://example.com/doc.ts", ChannelType.Live)]
        [InlineData("#EXTINF:-1 group-title=\"Belgesel Serisi\",Cosmos S01E01\nhttp://example.com/cosmos.mp4", ChannelType.Series)]
        [InlineData("#EXTINF-1 group-title=Live,No Colon Channel\nhttp://test.com/1.ts", ChannelType.Live)]
        [InlineData("#EXTINF:-1 tvg-id=123 tvg-logo=http://logo.com/1.png,No Quotes\nhttp://test.com/2.ts", ChannelType.Live)]
        [InlineData(" #EXTM3U\n#EXTINF:-1,Leading Space\nhttp://test.com/3.ts", ChannelType.Live)]
        // ── /ts yol segmenti → kesinlikle Live (gerçek veri: bmnew26 sağlayıcısı)
        [InlineData("#EXTINF:-1 group-title=\"♦️  GLOBO | CAPITAIS\",GLOBO SP\nhttp://bmnew26.site:80/play/7Rdycmc/ts", ChannelType.Live)]
        [InlineData("#EXTINF:-1 group-title=\"♦️  FILMES E SÉRIES\",AMC\nhttp://bmnew26.site:80/play/7Rdycmc2/ts", ChannelType.Live)]
        [InlineData("#EXTINF:-1 group-title=\"♦️  VARIEDADES\",ARTE 1\nhttp://bmnew26.site:80/play/7Rdycmc3/ts", ChannelType.Live)]
        // ── Uzantısız proxy URL, /ts yok → VOD (yıl olsun olmasın, dizi pattern'i yoksa)
        [InlineData("#EXTINF:-1 group-title=\"⭐ ReelsShorts\",30 Anos Congelada 3 Irmaos\nhttp://bmnew26.site:80/play/tE-icPf9jCprKLppYTgq9kuyM_PcMiPy", ChannelType.VOD)]
        [InlineData("#EXTINF:-1 group-title=\"♦️ Comedia ✔️\",Um Tio Quase Perfeito 2\nhttp://bmnew26.site:80/play/tE-icPf9jCprKLppYTgq9kuyM_PcMiPy2", ChannelType.VOD)]
        [InlineData("#EXTINF:-1 group-title=\"♦️[HOT] Adultos\",Voce Tera Que Aprender A Licao\nhttp://bmnew26.site:80/play/tE-icPf9jCprKLppYTgq9kuyM_PcMiPy3", ChannelType.VOD)]
        [InlineData("#EXTINF:-1 group-title=\"Filmes | Ficcao\",É Quase Verdade (2026)\nhttp://provider.test/stream/12345", ChannelType.VOD)]
        [InlineData("#EXTINF:-1 group-title=\"Animação\",Your Name (2016)\nhttp://provider.test/stream/67890", ChannelType.VOD)]
        // ── Uzantısız proxy URL, dizi pattern'i var → Series
        [InlineData("#EXTINF:-1 group-title=\"Series\",Breaking Bad S02E05\nhttp://provider.test/stream/99999", ChannelType.Series)]
        [InlineData("#EXTINF:-1 group-title=\"VOD Turkey Series - Söz\",Söz - S01 E01 [TR]\nhttp://provider.test/movie/user/pass/14627.mp4", ChannelType.VOD)]
        [InlineData("#EXTINF:-1 group-title=\"|PK| Geo TV Dramas\",Dao Episode 15\nhttp://provider.test/movie/user/pass/123174.mp4", ChannelType.VOD)]
        [InlineData("#EXTINF:-1 group-title=\"|PK| Geo TV Dramas\",Makafat Season 6 Khwahish Part 1\nhttp://provider.test/movie/user/pass/123204.mp4", ChannelType.VOD)]
        [InlineData("#EXTINF:-1 group-title=\"|PK| Geo TV Dramas\",Khaie Last Episode\nhttp://provider.test/movie/user/pass/123205.mp4", ChannelType.VOD)]
        // ── Adult başlıklar: s16/s14 gibi token'lar dizi pattern'i değil → VOD
        [InlineData("#EXTINF:-1 group-title=\"♦️[HOT] Adultos\",TitTorture06 s16 ChanelPreston JadaStevens [Adulto]\nhttp://bmnew26.site:80/play/tE-icPf9jCprKLppYTgq9kuyM_PcMiPyABC", ChannelType.VOD)]
        [InlineData("#EXTINF:-1 group-title=\"♦️[HOT] Adultos\",Spanking03 s14 JohnStagliano KellyDivine [Adulto]\nhttp://bmnew26.site:80/play/tE-icPf9jCprKLppYTgq9kuyM_PcMiPyDEF", ChannelType.VOD)]
        [InlineData("#EXTINF:-1 group-title=\"♦️[HOT] Adultos\",StretchClass14Scene02 s02 JohnStagliano AnikkaAlbrite [Adulto]\nhttp://bmnew26.site:80/play/tE-icPf9jCprKLppYTgq9kuyM_PcMiPyGHI", ChannelType.VOD)]
        [InlineData("#EXTINF:-1 group-title=\"Adulti XXX Rocco\",SIFFREDI LATE NIGHT 1x01 Xxx\nhttp://provider.test/movie/user/pass/143338.mkv", ChannelType.VOD)]
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
        public async Task ParseAsync_SolanaflixStyleCountryPrefix_InfersLiveCountryGroupWithoutChangingDisplayName()
        {
            var m3u = @"#EXTM3U
#EXTINF:-1,|GB| Sky Sports Main Event FHD
http://291819.solanaflix.com/live/TV-18612627/184193995007/461627.m3u";

            var channels = await _parser.ParseAsync(m3u);

            Assert.Single(channels);
            Assert.Equal(ChannelType.Live, channels[0].Type);
            Assert.Equal("|GB| Sky Sports Main Event FHD", channels[0].Name);
            Assert.Equal("Live / GB", channels[0].GroupTitle);
            Assert.Equal("GB", channels[0].Country);
        }

        [Fact]
        public async Task ParseAsync_SolanaflixStyleEventWithoutCountry_UsesGenericLiveGroup()
        {
            var m3u = @"#EXTM3U
#EXTINF:-1,Sky Sports+ | Event 60
http://291819.solanaflix.com/live/TV-18612627/184193995007/439225.m3u";

            var channels = await _parser.ParseAsync(m3u);

            Assert.Single(channels);
            Assert.Equal(ChannelType.Live, channels[0].Type);
            Assert.Equal("Sky Sports+ | Event 60", channels[0].Name);
            Assert.Equal("Live / Others", channels[0].GroupTitle);
        }

        [Fact]
        public async Task ParseAsync_SolanaflixStyleSeriesWithoutGroup_UsesGenericSeriesGroup()
        {
            var m3u = @"#EXTM3U
#EXTINF:-1,Virgin River S1 E1
http://291819.solanaflix.com/play/TV-18612627/184193995007/53776";

            var channels = await _parser.ParseAsync(m3u);

            Assert.Single(channels);
            Assert.Equal(ChannelType.Series, channels[0].Type);
            Assert.Equal("Virgin River S1 E1", channels[0].Name);
            Assert.Equal("Series / Others", channels[0].GroupTitle);
        }

        [Theory]
        [InlineData("|TR| Kanal D HD", "Live / TR", "|TR| Kanal D HD", "TR")]
        [InlineData("|IT| RAI 1 FHD", "Live / IT", "|IT| RAI 1 FHD", "IT")]
        [InlineData("|FR| Canal+ Sport 4K", "Live / FR", "|FR| Canal+ Sport 4K", "FR")]
        [InlineData("|DE| ZDF Neo HD", "Live / DE", "|DE| ZDF Neo HD", "DE")]
        public async Task ParseAsync_GlobalCountryPrefix_UsesCountryGroupAndPreservesDisplayName(
            string displayName,
            string expectedGroup,
            string expectedName,
            string expectedCountry)
        {
            var m3u = $@"#EXTM3U
#EXTINF:-1,{displayName}
http://global.example/live/user/pass/12345.m3u";

            var channels = await _parser.ParseAsync(m3u);

            Assert.Single(channels);
            Assert.Equal(ChannelType.Live, channels[0].Type);
            Assert.Equal(expectedName, channels[0].Name);
            Assert.Equal(expectedGroup, channels[0].GroupTitle);
            Assert.Equal(expectedCountry, channels[0].Country);
        }

        [Fact]
        public async Task ParseAsync_ArabicSeriesWithCountryPrefix_UsesGenericSeriesGroup()
        {
            var m3u = @"#EXTM3U
#EXTINF:-1,AR: الناجية الوحيدة S01 E01
http://provider.example/stream/50902";

            var channels = await _parser.ParseAsync(m3u);

            Assert.Single(channels);
            Assert.Equal(ChannelType.Series, channels[0].Type);
            Assert.Equal("Series / Others", channels[0].GroupTitle);
            Assert.Equal("AR", channels[0].Country);
            Assert.Equal("AR: الناجية الوحيدة S01 E01", channels[0].Name);
        }

        [Fact]
        public async Task ParseAsync_ArabicLiveCategoryPrefix_UsesArabicLanguageGroup()
        {
            var m3u = @"#EXTM3U
#EXTINF:-1,Quran: سورة الكهف القارئ اسلام صبحي
http://provider.example/live/1.m3u";

            var channels = await _parser.ParseAsync(m3u);

            Assert.Single(channels);
            Assert.Equal(ChannelType.Live, channels[0].Type);
            Assert.Equal("AR", channels[0].GroupTitle);
            Assert.Equal("Quran: سورة الكهف القارئ اسلام صبحي", channels[0].Name);
        }

        [Fact]
        public async Task ParseAsync_QualityPrefixMovie_DoesNotUseQualityAsGroup()
        {
            var m3u = @"#EXTM3U
#EXTINF:-1,4K: No Time To Die
http://provider.example/movie/user/pass/12345";

            var channels = await _parser.ParseAsync(m3u);

            Assert.Single(channels);
            Assert.Equal(ChannelType.VOD, channels[0].Type);
            Assert.Equal("Movies / Others", channels[0].GroupTitle);
            Assert.Equal("4K: No Time To Die", channels[0].Name);
        }

        [Fact]
        public async Task ParseAsync_BareNumericXNumericLiveName_IsNotSeries()
        {
            var m3u = @"#EXTM3U
#EXTINF:-1,RUS: 2x2
http://provider.example/live/user/pass/12345.m3u";

            var channels = await _parser.ParseAsync(m3u);

            Assert.Single(channels);
            Assert.Equal(ChannelType.Live, channels[0].Type);
            Assert.Equal("Live / RUS", channels[0].GroupTitle);
            Assert.Equal("RUS", channels[0].Country);
            Assert.Equal("RUS: 2x2", channels[0].Name);
        }

        [Fact]
        public async Task ParseAsync_SpaceSeparatedCountryPrefix_PreservesDisplayName()
        {
            var m3u = @"#EXTM3U
#EXTINF:-1,EN BBC News HD
http://provider.example/live/user/pass/22222.m3u";

            var channels = await _parser.ParseAsync(m3u);

            Assert.Single(channels);
            Assert.Equal(ChannelType.Live, channels[0].Type);
            Assert.Equal("Live / EN", channels[0].GroupTitle);
            Assert.Equal("EN", channels[0].Country);
            Assert.Equal("EN BBC News HD", channels[0].Name);
        }

        [Fact]
        public async Task ParseAsync_BracketProviderPrefix_IsNotUsedAsGroup()
        {
            var m3u = @"#EXTM3U
#EXTINF:-1,[DirectTV] CNN
http://provider.example/live/user/pass/33333.m3u";

            var channels = await _parser.ParseAsync(m3u);

            Assert.Single(channels);
            Assert.Equal(ChannelType.Live, channels[0].Type);
            Assert.Equal("Live / Others", channels[0].GroupTitle);
            Assert.Equal("[DirectTV] CNN", channels[0].Name);
        }

        [Theory]
        [InlineData("TR | BELGESEL", "TR: National Geographic HD")]
        [InlineData("SP | PELICULAS-SERIES", "ES: AXN MOVIES HD")]
        [InlineData("DE | KINO", "DE: Cinedome Action HD+")]
        public async Task ParseAsync_ProvidedGroupTitleWithCountryPrefix_PreservesGroupNameAndCountryUntouched(
            string groupTitle,
            string displayName)
        {
            var m3u = $@"#EXTM3U
#EXTINF:-1 group-title=""{groupTitle}"",{displayName}
http://provider.example/user/pass/12345";

            var channels = await _parser.ParseAsync(m3u);

            Assert.Single(channels);
            Assert.Equal(ChannelType.Live, channels[0].Type);
            Assert.Equal(groupTitle, channels[0].GroupTitle);
            Assert.Null(channels[0].Country);
            Assert.Equal(displayName, channels[0].Name);
        }

        [Fact]
        public async Task ParseAsync_IptvOrgLinearChannels_PreservesOriginalNames()
        {
            var m3u = @"#EXTM3U
#EXTINF:-1 tvg-id=""CNBCe.tr"" tvg-name=""CNBC-e"" group-title=""Business;Series"",CNBC-e
https://example.com/cnbce/master.m3u8
#EXTINF:-1 tvg-id=""GZT.tr"" tvg-name=""GZT ()"" group-title=""Documentary;Series"",GZT ()
https://example.com/gzt/index.m3u8
#EXTINF:-1 tvg-id=""MovieSmartTurk.tr"" tvg-name=""MovieSmart Turk"" group-title=""Movies"",MovieSmart Turk (576p)
https://example.com/moviesmart/master.m3u8";

            var channels = await _parser.ParseAsync(m3u);

            Assert.Equal(3, channels.Count);
            Assert.Equal("CNBC-e", channels[0].Name);
            Assert.Equal(ChannelType.Live, channels[0].Type);
            Assert.Equal("GZT ()", channels[1].Name);
            Assert.Equal(ChannelType.Live, channels[1].Type);
            Assert.Equal("MovieSmart Turk (576p)", channels[2].Name);
            Assert.Equal(ChannelType.Live, channels[2].Type);
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
