using System;
public class Program
{
    public static void Main()
    {
        string cat = "EU  MULTI NETFLIX SERIES";
        bool m1 = cat.TrimStart().StartsWith("EU ", StringComparison.OrdinalIgnoreCase);
        bool m2 = cat.TrimStart().StartsWith("EU|", StringComparison.OrdinalIgnoreCase);
        bool m3 = cat.TrimStart().Equals("EU", StringComparison.OrdinalIgnoreCase);
        Console.WriteLine(m1 || m2 || m3);
    }
}
