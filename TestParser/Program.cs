using System;
using Noctra.Services;

class Program 
{
    static void Main() 
    {
        string[] tests = {
            "TR | Breaking Bad S01E01 1080p Dual",
            "Breaking Bad (2008) - 1. Sezon 1. Bölüm [TR Dublaj]",
            "EN. Breaking Bad 4K HEVC",
            "Breaking Bad S01 E01 Altyazılı",
            "The Walking Dead (2010)",
            "The Walking Dead 720p HD x264",
            "Kanal D | The Walking Dead S01E03 TR Dublaj",
            "(FR) La Casa De Papel Season 1"
        };

        foreach(var t in tests) 
        {
            var key = SeriesInfoParser.NormalizeKey(t);
            Console.WriteLine($"{t}  =>  [{key}]");
        }
    }
}
