using MeetSpace.Client.App.Auth;
using MeetSpace.Client.Bootstrap;
using MeetSpace.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace MeetSpace
{
    public sealed partial class LoginPage : Page
    {
        private bool _isBusy;

        public LoginPage()
        {
            InitializeComponent();
            WindowService.Initialize(AppTitleBar, AppTitle);

            Loaded += LoginPage_Loaded;
            KeyDown += LoginPage_KeyDown;

            PasswordRevealToggle.Checked += PasswordRevealToggle_Checked;
            PasswordRevealToggle.Unchecked += PasswordRevealToggle_Unchecked;
        }

        private void LoginPage_Loaded(object sender, RoutedEventArgs e)
        {
            NameBox.Focus(FocusState.Programmatic);
            SetBusy(false);
            HideInfoBar();
            SetStatus(string.Empty, string.Empty);
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            await PerformAuthAsync();
        }

        private async void LoginPage_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter && !_isBusy)
            {
                e.Handled = true;
                await PerformAuthAsync();
            }
        }

        private void PasswordRevealToggle_Checked(object sender, RoutedEventArgs e)
        {
            KeyBox.PasswordRevealMode = PasswordRevealMode.Visible;
        }

        private void PasswordRevealToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            KeyBox.PasswordRevealMode = PasswordRevealMode.Hidden;
        }

        private async Task PerformAuthAsync()
        {
            if (_isBusy)
                return;

            HideInfoBar();

            var email = (NameBox.Text ?? string.Empty).Trim();
            var password = KeyBox.Password ?? string.Empty;

            if (string.IsNullOrWhiteSpace(email))
            {
                ShowError("Введите email.");
                NameBox.Focus(FocusState.Programmatic);
                return;
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                ShowError("Введите пароль.");
                KeyBox.Focus(FocusState.Programmatic);
                return;
            }

            SetBusy(true);
            SetStatus("Авторизация...", "Сначала пробуем вход. Если аккаунта нет — создаём.");

            try
            {
                var authClient = App.Current.Services.GetRequiredService<ISupabaseAuthClient>();

                AuthResult result;

                try
                {
                    result = await authClient.SignInAsync(email, password);
                }
                catch (SupabaseAuthException ex) when (IsInvalidCredentials(ex.Message))
                {
                    SetStatus("Регистрация...", "Аккаунт не найден. Создаём нового пользователя.");
                    result = await authClient.SignUpAsync(email, password);
                }

                if (result.RequiresEmailConfirmation)
                {
                    ShowInfo(
                        "Подтвердите email",
                        "На почту отправлена ссылка. Подтвердите регистрацию и затем нажмите Login ещё раз.");
                    return;
                }

                if (!result.IsAuthenticated || result.Tokens is null)
                {
                    ShowError(result.Message ?? "Не удалось получить сессию.");
                    return;
                }

                await CompleteSuccessfulLoginAsync(result.Tokens);
            }
            catch (Exception ex)
            {
                ShowError(MapLoginError(ex));
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task CompleteSuccessfulLoginAsync(AuthTokens tokens)
        {
            var authStore = App.Current.Services.GetRequiredService<AuthSessionStore>();
            var startupService = App.Current.Services.GetRequiredService<RealtimeStartupService>();
            var authBinder = App.Current.Services.GetRequiredService<RealtimeAuthBinder>();

            authStore.SetSession(tokens);

            SetStatus("Вход выполнен", "Подключаем realtime...");

            try
            {
                var connectResult = await startupService.EnsureConnectedAsync("ws://127.0.0.1:9002");
                if (connectResult.IsSuccess)
                    await authBinder.BindAsync();
            }
            catch
            {
                // auth успешен, realtime не должен ломать вход
            }

            Frame?.Navigate(typeof(MainPage));
        }

        private static bool IsInvalidCredentials(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            return message.Contains("Invalid login credentials", StringComparison.OrdinalIgnoreCase);
        }

        private void SetBusy(bool isBusy)
        {
            _isBusy = isBusy;

            LoginButton.IsEnabled = !isBusy;
            NameBox.IsEnabled = !isBusy;
            KeyBox.IsEnabled = !isBusy;
            PasswordRevealToggle.IsEnabled = !isBusy;

            LoginBar.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetStatus(string title, string text)
        {
            StatusTitle.Text = title ?? string.Empty;
            StatusText.Text = text ?? string.Empty;
        }

        private void ShowError(string message)
        {
            Errorbar.Severity = InfoBarSeverity.Error;
            Errorbar.Title = "Ошибка входа";
            Errorbar.Message = message;
            Errorbar.IsOpen = true;

            SetStatus("Ошибка", message);
        }

        private void ShowInfo(string title, string message)
        {
            Errorbar.Severity = InfoBarSeverity.Informational;
            Errorbar.Title = title;
            Errorbar.Message = message;
            Errorbar.IsOpen = true;

            SetStatus(title, message);
        }

        private void HideInfoBar()
        {
            Errorbar.IsOpen = false;
        }

        private static string MapLoginError(Exception ex)
        {
            var message = ex.Message ?? string.Empty;

            if (message.Contains("Invalid login credentials", StringComparison.OrdinalIgnoreCase))
                return "Неверный email или пароль.";

            if (message.Contains("Email not confirmed", StringComparison.OrdinalIgnoreCase))
                return "Подтвердите email по ссылке из письма и нажмите Login ещё раз.";

            if (message.Contains("Email logins are disabled", StringComparison.OrdinalIgnoreCase))
                return "В Supabase выключен вход по email/password. Включи Email provider.";

            if (message.Contains("User already registered", StringComparison.OrdinalIgnoreCase))
                return "Пользователь уже зарегистрирован. Просто войдите после подтверждения почты.";

            return string.IsNullOrWhiteSpace(message)
                ? "Не удалось выполнить вход."
                : message;
        }
    }
}