using System.Diagnostics;
using Noctra.Services;
using Xunit;
using Xunit.Abstractions;

namespace Noctra.Tests;

public class EpgServiceNormalizationTests
{
    private readonly ITestOutputHelper _output;

    public EpgServiceNormalizationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void NormalizeName_ShouldCorrectlyNormalize()
    {
        // Baseline correctness checks
        Assert.Equal("trshowtv", EpgService.NormalizeName("TR - Show TV"));
        Assert.Equal("ukbbcnews", EpgService.NormalizeName("UK| BBC News HD"));
        Assert.Equal("sport1", EpgService.NormalizeName("Sport 1 (HD)"));
        Assert.Equal("kanald", EpgService.NormalizeName("Kanal D [FHD]"));
        Assert.Equal("atv", EpgService.NormalizeName("ATV HEVC"));
        Assert.Equal("startv", EpgService.NormalizeName("Star TV 4K"));
        Assert.Equal("trt1", EpgService.NormalizeName("TRT 1 1080p"));
        Assert.Equal("foxtv", EpgService.NormalizeName("Fox TV VIP"));
        Assert.Equal("tv8", EpgService.NormalizeName("TV 8 (Live)"));
        Assert.Equal("cinemaaction", EpgService.NormalizeName("Cinema Action"));
        Assert.Equal("beinsports1", EpgService.NormalizeName("BeIN Sports 1"));

        // Turkish character normalization checks
        Assert.Equal("isguc", EpgService.NormalizeName("İş Güç"));
        Assert.Equal("cicek", EpgService.NormalizeName("Çiçek"));
        Assert.Equal("yumusakge", EpgService.NormalizeName("Yumuşak Ge"));
        Assert.Equal("olum", EpgService.NormalizeName("Ölüm"));
        Assert.Equal("uzum", EpgService.NormalizeName("Üzüm"));
    }

    [Fact]
    public void NormalizeName_PerformanceBenchmark()
    {
        // Warmup
        for (int i = 0; i < 1000; i++)
        {
            EpgService.NormalizeName("TR - Show TV HD");
        }

        var sw = Stopwatch.StartNew();
        const int iterations = 100000;

        for (int i = 0; i < iterations; i++)
        {
            EpgService.NormalizeName("TR - Show TV HD");
            EpgService.NormalizeName("UK| BBC News [FHD]");
            EpgService.NormalizeName("Sport 1 (HEVC)");
            EpgService.NormalizeName("Kanal D 4K");
            EpgService.NormalizeName("Cinema Action VIP");
        }

        sw.Stop();
        _output.WriteLine($"Time for {iterations * 5} calls: {sw.ElapsedMilliseconds}ms");
    }
}
