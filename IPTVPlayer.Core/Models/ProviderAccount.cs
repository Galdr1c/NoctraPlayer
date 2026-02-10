using System.ComponentModel.DataAnnotations;

using CommunityToolkit.Mvvm.ComponentModel;

namespace IPTVPlayer.Models;

public partial class ProviderAccount : ObservableObject
{
    [Key]
    public int Id { get; set; }

    private string _name = string.Empty;
    [Required]
    public string Name 
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    private ProfileType _type;
    public ProfileType Type 
    {
        get => _type;
        set => SetProperty(ref _type, value);
    }

    private string _url = string.Empty;
    public string Url 
    {
        get => _url;
        set => SetProperty(ref _url, value);
    }

    private string? _username;
    public string? Username 
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }

    private string? _password;
    public string? Password 
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }
    
    private DateTime? _expirationDate;
    public DateTime? ExpirationDate 
    {
        get => _expirationDate;
        set => SetProperty(ref _expirationDate, value);
    }

    public ICollection<Profile> Profiles { get; set; } = new List<Profile>();
}
