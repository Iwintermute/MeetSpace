using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using FishyFlip.Lexicon.App.Bsky.Feed;
using MeetSpace.Client.App.Auth;
using MeetSpace.Client.App.Session;
using MeetSpace.Client.Realtime.Abstractions;
using MeetSpace.UI.Services;
using MeetSpace.Views.Temporary;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
// Документацию по шаблону элемента "Пустая страница" см. по адресу https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x419

namespace MeetSpace
{
    /// <summary>
    /// Пустая страница, которую можно использовать саму по себе или для перехода внутри фрейма.
    /// </summary>
    [INotifyPropertyChanged]
    public sealed partial class MainPage : Page
    {
        //[ObservableProperty]
        //private MainViewModel viewModel;
        private readonly Dictionary<Type, Type> viewModelsToViews = new();
        private readonly AuthSessionStore _authStore;
        private readonly ISupabaseAuthClient _authClient;
        private readonly IRealtimeGateway _realtimeGateway;
        private bool _logoutInProgress;
        //private ObservableCollection<ErrorMessage> errors = new ObservableCollection<ErrorMessage>();

        /*
         * Used to determine if primary pane is collapsed or expanded
         * If colapsed then the columnspan is 1, otherwise if expanded it is 3
         */
        [ObservableProperty]
        private bool primaryPaneCollapsed = true;
        private bool CollapseByDefault = true;
        private int BoolToColumnSpan(bool value) => value ? 1 : 3; // Bound by primary pane

        public MainPage()
        {
            this.InitializeComponent();

            if (AppNavigation.MenuItems.Count > 0)
                AppNavigation.SelectedItem = AppNavigation.MenuItems[0];

            _authStore = App.Current.Services.GetRequiredService<AuthSessionStore>();
            _authClient = App.Current.Services.GetRequiredService<ISupabaseAuthClient>();
            _realtimeGateway = App.Current.Services.GetRequiredService<IRealtimeGateway>();
            Loaded += MainPage_Loaded;
            Unloaded += MainPage_Unloaded;

            if (AppNavigation.MenuItems.Count > 0)
                AppNavigation.SelectedItem = AppNavigation.MenuItems[0];
            //PrimaryPaneCollapsed = CollapseByDefault;
            //AppNavigation.SelectedItem = AppNavigation.MenuItems[0];
            //Bindings.Update();
            /*
             * Navigate the secondary page
             * The SecondaryNavigationMessage contains a "ViewModel" Type and a "payload" object
             * The ViewModel type is mapped to a Page that is navigated to
             */
            //viewModelsToViews[typeof(PostViewModel)] = typeof(PostPage);
            //viewModelsToViews[typeof(ProfileViewModel)] = typeof(ProfilePage);
            //viewModelsToViews[typeof(ListViewModel)] = typeof(ListPage);
            //viewModelsToViews[typeof(FeedViewModel)] = typeof(FeedPage);
            //    WeakReferenceMessenger.Default.Register<SecondaryNavigationMessage>(this, (r, m) =>
            //    {
            //        if (m.Value is not null)
            //        {
            //            // Show these items in the left sidebar
            //            if (m.Value.payload is ProfileViewModel || m.Value.payload is ListViewModel || m.Value.payload is FeedViewModel)
            //            {
            //                PrimaryPane.Navigate(viewModelsToViews[m.Value.ViewModel], m.Value.payload);
            //                AppNavigation.SelectedItem = null;
            //            }
            //            else
            //            {
            //                SecondaryPaneContainer.Visibility = Visibility.Visible;
            //                PrimaryPaneCollapsed = true;
            //                SecondaryPane.Navigate(viewModelsToViews[m.Value.ViewModel], m.Value.payload);
            //            }
            //        }
            //        else //new SecondaryNavigation(null) go to null{
            //        {
            //            SecondaryPaneContainer.Visibility = Visibility.Collapsed;
            //            PrimaryPaneCollapsed = CollapseByDefault; // expand if user chooses too
            //        }
            //    });

            //    WeakReferenceMessenger.Default.Register<ErrorMessage>(this, async (r, m) =>
            //    {
            //        await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
            //        {
            //            ErrorButton.Visibility = Visibility.Visible;
            //            errors.Add(m);
            //        });
            //    });
        }
        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            _authStore.StateChanged += AuthStore_StateChanged;
        }

        private void MainPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _authStore.StateChanged -= AuthStore_StateChanged;
        }

        private async void AuthStore_StateChanged(object sender, AuthSessionState state)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                if (!state.IsAuthenticated)
                    Frame?.Navigate(typeof(LoginPage));
            });
        }
        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (!_authStore.Current.IsAuthenticated)
            {
                Frame?.Navigate(typeof(LoginPage));
                return;
            }

            WindowService.Initialize(AppTitleBar, AppTitle);

            AppTitleBar.Height = 50;
            AppTitleBar.Height = 48;

            if (PrimaryPane.Content == null)
                PrimaryPane.Navigate(typeof(ChatPage));
        }

        // used by URL
        public ImageSource img(string uri)
        {
            if (uri == null)
                throw new ArgumentNullException(nameof(uri));

            // Create a BitmapImage and set its UriSource to the provided Uri
            var bitmapImage = new BitmapImage(new Uri(uri));
            return bitmapImage;
        }

        private async void AppNavigation_SelectionChanged(Microsoft.UI.Xaml.Controls.NavigationView sender, Microsoft.UI.Xaml.Controls.NavigationViewSelectionChangedEventArgs args)
        {
            //if (args.IsSettingsSelected == true)
            //{
            //    PrimaryPane.Navigate(typeof(SettingsPage), args.RecommendedNavigationTransitionInfo);
            //    return;
            //}
            //if (sender.SelectedItem is null) return;

            if (args.IsSettingsSelected)
                return;

            var selectedItem = sender.SelectedItem as Microsoft.UI.Xaml.Controls.NavigationViewItem;
            if (selectedItem == null)
                return;

            var tag = selectedItem.Tag as string;
            if (string.IsNullOrWhiteSpace(tag))
                return;

            switch (tag)
            {
                case "Chats":
                    if (PrimaryPane.CurrentSourcePageType != typeof(ChatPage))
                        PrimaryPane.Navigate(typeof(ChatPage), null, args.RecommendedNavigationTransitionInfo);
                    break;

                case "New":
                    if (PrimaryPane.CurrentSourcePageType != typeof(MeetSpace.Views.Temporary.MeetingsHomePage))
                        PrimaryPane.Navigate(typeof(MeetSpace.Views.Temporary.MeetingsHomePage), null, args.RecommendedNavigationTransitionInfo);
                    break;

                case "Notifications":
                    // TODO
                    break;

                case "Search":
                    // TODO
                    break;

                case "Profile":
                    // TODO
                    break;
                case "Logout":
                    await LogoutAsync();
                    break;
            }

            //if (sender.SelectedItem == AppNavigation.MenuItems[0])
            //{
            //    PrimaryPane.Navigate(typeof(HomePage), args.RecommendedNavigationTransitionInfo);
            //}
            //else if (sender.SelectedItem == AppNavigation.MenuItems[1])
            //{
            //    PrimaryPane.Navigate(typeof(NotificationPage), args.RecommendedNavigationTransitionInfo);
            //}
            //else if (sender.SelectedItem == AppNavigation.MenuItems[2])
            //{
            //    PrimaryPane.Navigate(typeof(SearchPage), args.RecommendedNavigationTransitionInfo);
            //}
            //else if (sender.SelectedItem == AppNavigation.MenuItems[3])
            //{
            //    PrimaryPane.Navigate(typeof(ChatPage), args.RecommendedNavigationTransitionInfo);
            //}
            //else if (sender.SelectedItem == AppNavigation.MenuItems[4])
            //{
            //    PrimaryPane.Navigate(typeof(FeedsPage), args.RecommendedNavigationTransitionInfo);
            //}
            //else if (sender.SelectedItem == AppNavigation.MenuItems[5])
            //{
            //    PrimaryPane.Navigate(typeof(ListsPage), args.RecommendedNavigationTransitionInfo);
            //}
            //else if (sender.SelectedItem == AppNavigation.FooterMenuItems[1])
            //{
            //    PrimaryPane.Navigate(typeof(ProfilePage), ViewModel.CurrentProfile);
            //}
        }
        private async System.Threading.Tasks.Task LogoutAsync()
        {
            if (_logoutInProgress)
                return;

            _logoutInProgress = true;

            try
            {
                var authState = _authStore.Current;
                var sessionStore = App.Current.Services.GetRequiredService<SessionStore>();

                try
                {
                    if (!string.IsNullOrWhiteSpace(authState.AccessToken))
                    {
                        await _authClient.SignOutAsync(authState.AccessToken);
                    }
                }
                catch
                {
                    // Не блокируем локальный выход, если серверный logout не удался.
                }

                try
                {
                    if (_realtimeGateway != null && _realtimeGateway.IsConnected)
                    {
                        await _realtimeGateway.DisconnectAsync();
                    }
                }
                catch
                {
                    // Не блокируем очистку сессии, если realtime уже отвалился.
                }

                sessionStore.Reset();
                _authStore.ClearSession();
            }
            finally
            {
                _logoutInProgress = false;
            }
        }
        private void AppNavigation_ItemInvoked(Microsoft.UI.Xaml.Controls.NavigationView sender, Microsoft.UI.Xaml.Controls.NavigationViewItemInvokedEventArgs args)
        {
            //if (args.InvokedItem is null) return;
            //if (args.InvokedItem.ToString() == "New Post")
            //{
            //    SecondaryPaneContainer.Visibility = Visibility.Visible;
            //    PrimaryPaneCollapsed = true;
            //    SecondaryPane.Navigate(typeof(CreatePostPage));
            //}
        }

        private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // fix weird titlebar bug
            AppTitleBar.Height = 50;
            AppTitleBar.Height = 48;

            try
            {
                if (e.NewSize.Width > 500)
                {
                    VisualStateManager.GoToState(this, "WideState", true);
                    //PrimaryPaneCollapsed = CollapseByDefault;
                }
                else
                {
                    VisualStateManager.GoToState(this, "NarrowState", true);
                    //PrimaryPaneCollapsed = false;
                }
            }
            catch { }
        }

        private void PrimaryPaneToggle_Click(object sender, RoutedEventArgs e)
        {
            CollapseByDefault = (bool)PrimaryPaneToggle.IsChecked;
        }
    }
}