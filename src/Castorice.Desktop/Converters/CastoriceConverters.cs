using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Castorice.Core.Chat;
using Castorice.Core.Tournament;

namespace Castorice.Desktop.Converters;

public static class CastoriceConverters
{
    /// <summary>Colours a chat line by its kind.</summary>
    public static readonly IValueConverter MessageKindToBrush =
        new FuncValueConverter<ChatMessageKind, IBrush>(kind => kind switch
        {
            ChatMessageKind.Outgoing => new SolidColorBrush(Color.Parse("#9A94AD")),
            ChatMessageKind.Notice => new SolidColorBrush(Color.Parse("#8C5FC4")),
            ChatMessageKind.System => new SolidColorBrush(Color.Parse("#6F6982")),
            ChatMessageKind.Client => new SolidColorBrush(Color.Parse("#E8B860")),
            ChatMessageKind.Action => new SolidColorBrush(Color.Parse("#E36FA8")),
            _ => new SolidColorBrush(Color.Parse("#EDEAF5")),
        });

    public static readonly IValueConverter TeamToBrush =
        new FuncValueConverter<TeamColour?, IBrush>(team => team switch
        {
            TeamColour.Red => new SolidColorBrush(Color.Parse("#E05A72")),
            TeamColour.Blue => new SolidColorBrush(Color.Parse("#5A9BE0")),
            _ => new SolidColorBrush(Color.Parse("#6F6982")),
        });

    /// <summary>Prefixes channels with their kind so the sidebar reads at a glance.</summary>
    public static readonly IValueConverter TargetGlyph =
        new FuncValueConverter<ChatTargetKind, string>(kind => kind switch
        {
            ChatTargetKind.MultiplayerRoom => "MP",
            ChatTargetKind.Channel => "#",
            ChatTargetKind.PrivateMessage => "@",
            _ => "·",
        });

    public static readonly IValueConverter IsNotEmpty =
        new FuncValueConverter<string?, bool>(text => !string.IsNullOrWhiteSpace(text));

    public static readonly IValueConverter CountToVisible =
        new FuncValueConverter<int, bool>(count => count > 0);

    public static readonly IValueConverter TimestampToShortTime =
        new FuncValueConverter<DateTimeOffset, string>(value =>
            value.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture));

    /// <summary>Renders a rank-history series as a polyline inside a 240x48 box.</summary>
    public static readonly IValueConverter SparklinePoints =
        new FuncValueConverter<IReadOnlyList<double>?, IList<Point>>(series =>
        {
            var points = new List<Point>();
            if (series is not { Count: > 1 })
            {
                return points;
            }

            const double width = 240;
            const double height = 48;
            var step = width / (series.Count - 1);

            for (var i = 0; i < series.Count; i++)
            {
                points.Add(new Point(i * step, height - (series[i] * height)));
            }

            return points;
        });
}
