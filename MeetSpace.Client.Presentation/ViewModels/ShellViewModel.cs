using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MeetSpace.Client.Presentation.Navigation;

namespace MeetSpace.Client.Presentation.ViewModels;

public partial class ShellViewModel : ViewModelBase
{
    private readonly IShellNavigationService _navigationService;
    private readonly ShellNavigationStore _navigationStore;

    [ObservableProperty]
    private object? _currentContent;

    [ObservableProperty]
    private ShellRegion _currentRegion;

    public ShellViewModel(
        IShellNavigationService navigationService,
        ShellNavigationStore navigationStore)
    {
        _navigationService = navigationService;
        _navigationStore = navigationStore;

        _navigationStore.StateChanged += OnNavigationStateChanged;

        CurrentRegion = _navigationStore.Current.CurrentRegion;
        CurrentContent = _navigationStore.Current.CurrentViewModel;
    }

    [RelayCommand]
    private Task OpenConferenceAsync()
        => _navigationService.NavigateAsync(ShellRegion.Conference);

    [RelayCommand]
    private Task OpenCallsAsync()
        => _navigationService.NavigateAsync(ShellRegion.Calls);

    [RelayCommand]
    private Task OpenChatAsync()
        => _navigationService.NavigateAsync(ShellRegion.Chat);

    [RelayCommand]
    private Task OpenPresenceAsync()
        => _navigationService.NavigateAsync(ShellRegion.Presence);

    [RelayCommand]
    private Task OpenSettingsAsync()
        => _navigationService.NavigateAsync(ShellRegion.Settings);

    private void OnNavigationStateChanged(object? sender, ShellNavigationState state)
    {
        CurrentRegion = state.CurrentRegion;
        CurrentContent = state.CurrentViewModel;
    }
}