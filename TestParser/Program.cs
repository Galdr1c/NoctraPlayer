using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Noctra.Data;
using Noctra.Models;
using Noctra.Services;

namespace Debugger 
{
    class Program 
    {
        static async Task Main(string[] args) 
        {
            var url = "##";
            
            Console.WriteLine($"Testing URL: {url}");
            
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            
            var parser = new M3UParser(httpClient);
            
            try {
                var channels = await parser.ParseFromUrlAsync(url);
                Console.WriteLine($"SUCCESS: Found {channels.Count} channels.");
                
                if (channels.Count > 0) {
                    var live = channels.Count(c => c.Type == ChannelType.Live);
                    var vod = channels.Count(c => c.Type == ChannelType.VOD);
                    var series = channels.Count(c => c.Type == ChannelType.Series);
                    Console.WriteLine($"Live: {live}, VOD: {vod}, Series: {series}");
                    
                    Console.WriteLine("First 5 channels:");
                    foreach(var c in channels.Take(5)) {
                        Console.WriteLine($"- {c.Name} ({c.GroupTitle}) [{c.StreamUrl}]");
                    }
                }
            }
            catch (Exception ex) {
                Console.WriteLine($"ERROR: {ex.Message}");
                if (ex.InnerException != null) {
                    Console.WriteLine($"Inner: {ex.InnerException.Message}");
                }
            }
        }
    }
}
