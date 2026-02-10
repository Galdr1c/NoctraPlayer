using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IPTVPlayer.Models;
using IPTVPlayer.Services;
using IPTVPlayer.Services.Interfaces;

namespace IPTVPlayer.ViewModels;

public partial class EditChannelViewModel : ObservableObject
{
    private readonly IChannelService _channelService;
    private readonly IMetadataService _metadataService; // For TMDb search
    private Channel _originalChannel;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string? _groupTitle;

    [ObservableProperty]
    private ChannelType _type;

    [ObservableProperty]
    private string? _logoUrl;

    [ObservableProperty]
    private string? _tvgId;

    [ObservableProperty]
    private string? _metadataSearchQuery;

    [ObservableProperty]
    private bool _isBusy;

    public event EventHandler? RequestClose;

    public EditChannelViewModel(IChannelService channelService, IMetadataService metadataService)
    {
        _channelService = channelService;
        _metadataService = metadataService;
        _originalChannel = new Channel(); // Placeholder
        _name = string.Empty;
    }

    public void Initialize(Channel channel)
    {
        _originalChannel = channel;
        Name = channel.Name;
        GroupTitle = channel.GroupTitle;
        Type = channel.Type;
        LogoUrl = channel.LogoUrl;
        TvgId = channel.TvgId;
        MetadataSearchQuery = channel.Name; // Default search query
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Name)) return;

        IsBusy = true;
        try
        {
            // Update original channel properties
            _originalChannel.Name = Name;
            _originalChannel.GroupTitle = GroupTitle;
            _originalChannel.Type = Type;
            _originalChannel.LogoUrl = LogoUrl;
            _originalChannel.TvgId = TvgId;

            await _channelService.UpdateChannelAsync(_originalChannel);
            RequestClose?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task SearchMetadataAsync()
    {
        if (string.IsNullOrWhiteSpace(MetadataSearchQuery)) return;

        IsBusy = true;
        try
        {
            var metadata = await _metadataService.FetchMetadataAsync(MetadataSearchQuery, Type);
            if (metadata != null)
            {
                // Auto-fill available data if user confirms? 
                // For now, let's just update empty fields or provide a way to apply matching
                // Simpler: Just apply found metadata to current VOD/Series properties of the channel
                
                // We are editing the channel properties here, but metadata is stored on the channel too.
                // Let's apply what we found to the *editing* state if relevant
                
                // If logo was empty, use poster
                if (string.IsNullOrEmpty(LogoUrl) && !string.IsNullOrEmpty(metadata.PosterUrl))
                {
                    LogoUrl = metadata.PosterUrl;
                }

                // We should also update the underlying channel's metadata fields immediately 
                // or store them to be saved on SaveAsync. 
                // Since this ViewModel focuses on basic properties, we might need to expose metadata fields too 
                // or just handle them in the background.

                // Ideally, we'd show the user what we found.
                // For this iteration, let's just toast/notify "Metadata Found: [Title]" and update the Logo/Metadata columns.
                
                _originalChannel.Plot = metadata.Description;
                _originalChannel.Rating = metadata.Rating;
                _originalChannel.ReleaseYear = metadata.ReleaseYear;
                _originalChannel.BackdropUrl = metadata.BackdropUrl;
                _originalChannel.Director = metadata.Director;
                _originalChannel.Cast = metadata.Cast;

                 if (string.IsNullOrEmpty(LogoUrl) && !string.IsNullOrEmpty(metadata.PosterUrl))
                     LogoUrl = metadata.PosterUrl;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}
