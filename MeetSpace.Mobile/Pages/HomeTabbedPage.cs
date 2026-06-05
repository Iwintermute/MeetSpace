namespace MeetSpace.Mobile.Pages;

public sealed class HomeTabbedPage : TabbedPage
{
	public HomeTabbedPage(MeetingsPage meetingsPage, ChatPage chatPage)
	{
		if (meetingsPage == null)
			throw new ArgumentNullException(nameof(meetingsPage));
		if (chatPage == null)
			throw new ArgumentNullException(nameof(chatPage));

		Children.Add(new NavigationPage(meetingsPage)
		{
			Title = "Встречи"
		});

		Children.Add(new NavigationPage(chatPage)
		{
			Title = "Чаты"
		});
	}
}
