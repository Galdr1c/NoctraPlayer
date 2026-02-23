using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace Debugger 
{
    class Program 
    {
        static async Task Main(string[] args) 
        {
            string filePath = "temp_m3u.txt";
            if (!File.Exists(filePath)) {
                Console.WriteLine("File not found: " + filePath);
                return;
            }

            Console.WriteLine("Starting parse of " + filePath);
            var channels = new List<Channel>();
            
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var reader = new StreamReader(stream);
            
            string? firstLine = null;
            while ((firstLine = await reader.ReadLineAsync()) != null)
            {
                if (!string.IsNullOrWhiteSpace(firstLine))
                    break;
            }

            if (string.IsNullOrWhiteSpace(firstLine) || !firstLine.Trim().StartsWith("#EXTM3U", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("INVALID HEADER: [" + firstLine + "]");
                return;
            }
            Console.WriteLine("Header OK: " + firstLine);

            Channel? currentChannel = null;
            string? line;
            int count = 0;

            while ((line = await reader.ReadLineAsync()) != null)
            {
                line = line.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                if (line.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
                {
                    currentChannel = ParseExtInf(line);
                }
                else if (!line.StartsWith("#") && currentChannel != null)
                {
                    count++;
                    if (count <= 5) Console.WriteLine("Parsed Channel " + count + ": " + currentChannel.Name);
                    currentChannel = null;
                }
            }

            Console.WriteLine("Total Channels Parsed: " + count);
        }

        static Channel ParseExtInf(string line)
        {
            var channel = new Channel();
            var lastCommaIndex = line.LastIndexOf(',');
            if (lastCommaIndex >= 0)
            {
                channel.Name = line.Substring(lastCommaIndex + 1).Trim();
            }
            else
            {
                channel.Name = "No Name";
            }
            return channel;
        }
    }

    class Channel { public string Name { get; set; } }
}
