using System.Text.RegularExpressions;
using Noctra.Models;

namespace Noctra.Services;

public static partial class ChildSafetyHelper
{
    public static string[] GetSafeCategories() => new[] { 
        "çocuk", "cocuk", "çizgi", "cizgi", "bebek", "baby", "minika", "trt çocuk", "trt cocuk", 
        "kids", "kid", "kinder", "enfant", "niños", "infantil", "bebe", "bambini", "cartoon", "cbeebies", "moonbug", "pbs kids", "dreamworks", "pixar", 
        "dibujos animados", "dessin animé", "zeichentrick", "animation", "animasyon", "animated",
        "nick jr", "nickelodeon junior", "disney jr", "cartoon network", "cartoonito", "boomerang", "duck tv", "baby tv", "minika go", "minika çocuk"
    };

    public static string[] GetDangerousCategories() => new[] {
        "18+", "+18", "adult", "xxx", "porn", "mature", "erotik", "sex", "yetişkin", "poker", "casino", "haber", "news", "politika", "haberler", "belgesel", "documentary", 
        "attack", "america", "amerika", "horror", "korku", "thriller", "gerilim", "crime", "suç", "violence"
    };

    public static string[] GetCriticalBlacklist() => new[] {
        "xxx", "porn", "erotic", "sex", "porno", "gay", "lesbian", "hardcore", "cinsel", "mature", "18+", "+18", "yeşilçam", "yesilcam", "erotizm", "romantizm", "nostalji", "nostalgia",
        "adult", "sex", "porn", "xxx",
        "9-1-1", "alien", "castlevania", "south park", "family guy", "rick and morty", "the boys", "deadpool", "lucifer", "dexter", "sayko", "sycophant", "syco", "mcgregor", "yerli", "burn",
        "blood", "kan", "şiddet", "düşman", "düşmanlar", "enemies", "enemy", "crossing", "pazar", "marvel", "marvels", "dc", ".kill", "kill", "anemone", "amar", "avangers", "korku", "korkunç", 
        "korku kapanı", "maymunlar cehennemi", "megalodon", "person", "rising", "risk", "embarass", "embarassing", "kadın", "woman"," yaşamaya", "sağ", "batman", "ghost", "lara", "lara croft", "Kabus",
        "nightmare"
    };

    public static string[] GetKidFriendlyTitles() => new[] {
        "mickey", "minnie", "donald duck", "goofy", "toy story", "nemo", "dori", "frozen", "elsa", "olaf", "moana", "dumbo", "bambi", "lion king", "shrek", "madagascar", "ice age", "buzz lightyear", "cars", "mcqueen",
        "peppa", "paw patrol", "pj masks", "cocomelon", "dora", "diego", "bluey", "pupa", "barbie", "winx", "ben 10", "spiderman", "batman", "superman", "avengers", "marvel", "pikachu", "pokemon", "yu-gi-oh", "digimon", "naruto", "dragon ball",
        "abby hatcher", "all hail king julien", "alex & co", "bing", "lara", "maya", "teletubbies", "sesame street", "maşa ve koca ayı", "masha and the bear", "arı maya", "scooby", "tom and jerry", "looney tunes", "bugs bunny", "tweety",
        "daffy duck", "popeye", "smurfs", "şirinler", "garfield", "spongebob", "sünger bob", "ninja turtles", "powerpuff girls", "dexter's laboratory", "johnny bravo", "adventure time", "gumball", "phineas and ferb", "gravity falls",
        "miraculous", "ladybug", "kara kedi", "transformers rescue", "my little pony", "thomas & friends", "bob the builder", "caillou", "pocoyo", "tayo", "robocar poli", "super wings", "harika kanatlar", "octonauts", "bubble guppies",
        "afacan", "yumurcak", "niloya", "kukuli", "tosbik", "rafadan tayfa", "aslan", "kaptan pengu", "maysa", "bulut", "köstebekgiller", "pırıl", "kuzucuk", "akıllı tavşan", "momo", "canım kardeşim", "pepee", "lele", "keloğlan",
        "nasreddin hoca", "doris", "pisi", "elof", "heidi", "çocuk kalbi", "küçük prens", "arı maya", "maya the bee"
    };

    public static bool IsSafeRating(string? rating)
    {
        if (string.IsNullOrWhiteSpace(rating)) return true; 

        var safeRatings = new[] { "G", "PG", "TV-Y", "TV-Y7", "TV-G", "TV-PG", "U", "7", "6", "0", "A", "GENEL" };
        var dangerousRatings = new[] { "R", "NC-17", "TV-MA", "18", "16", "15", "X", "YETİŞKİN", "ADULT", "PG-13" };

        var upperRating = rating.ToUpperInvariant();

        if (dangerousRatings.Any(dr => upperRating.Contains(dr))) return false;
        if (safeRatings.Any(sr => upperRating.Contains(sr))) return true;

        return false;
    }

    [GeneratedRegex(@"\b(19\d{2}|2000)\b")]
    private static partial Regex OldYearRegex();

    public static bool IsOldContent(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return OldYearRegex().IsMatch(name);
    }
}
