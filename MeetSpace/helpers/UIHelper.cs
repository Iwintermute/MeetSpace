using System;
using System.Globalization;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace MeetSpace.Helpers
{
    public static class UIHelper
    {
        public static string SimpleDate(DateTime? date)
        {
            if (date is null)
                return string.Empty;

            var value = date.Value;
            var timeDifference = DateTime.Now - value;

            if (timeDifference.TotalDays >= 365)
                return value.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

            if (timeDifference.TotalDays >= 30)
                return value.ToString("MMM d", CultureInfo.InvariantCulture);

            if (timeDifference.TotalDays >= 1)
                return $"{(int)timeDifference.TotalDays} {GetSingularOrPlural((int)timeDifference.TotalDays, "day")} ago";

            if (timeDifference.TotalHours >= 1)
                return $"{(int)timeDifference.TotalHours} {GetSingularOrPlural((int)timeDifference.TotalHours, "hour")} ago";

            if (timeDifference.TotalMinutes >= 1)
                return $"{(int)timeDifference.TotalMinutes} {GetSingularOrPlural((int)timeDifference.TotalMinutes, "minute")} ago";

            if (timeDifference.TotalSeconds >= 1)
                return $"{(int)timeDifference.TotalSeconds} {GetSingularOrPlural((int)timeDifference.TotalSeconds, "second")} ago";

            return "just now";
        }

        private static string GetSingularOrPlural(int value, string singular)
        {
            return value == 1 ? singular : singular + "s";
        }

        public static string FormatDate(DateTime? date)
        {
            if (date is null)
                return string.Empty;

            return date.Value.ToString("MMMM dd, yyyy 'at' hh:mm tt", CultureInfo.InvariantCulture);
        }

        public static bool none(object obj) => obj is not null;

        public static bool invert(bool value) => !value;

        public static Visibility BoolToVis(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

        public static Visibility InvertBoolToVis(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

        public static ImageSource Img(string uri)
        {
            if (string.IsNullOrWhiteSpace(uri))
                return new BitmapImage();

            if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsedUri))
                return new BitmapImage();

            var bitmap = new BitmapImage();
            bitmap.UriSource = parsedUri;
            return bitmap;
        }

        public static Thickness Border(bool hasReply)
        {
            return hasReply ? new Thickness(0) : new Thickness(0, 0, 0, 1);
        }
    }
}