using Avalonia.Data.Converters;
using Castorice.Desktop.Converters;

namespace Castorice.Desktop.Views;

/// <summary>
/// XAML-visible aliases for the converters. Avalonia's <c>x:Static</c> needs a type it can
/// reference from the view namespace, so the definitions stay in one place and are surfaced here.
/// </summary>
public static class ViewConverters
{
    public static IValueConverter MessageKindToBrush => CastoriceConverters.MessageKindToBrush;

    public static IValueConverter TeamToBrush => CastoriceConverters.TeamToBrush;

    public static IValueConverter TargetGlyph => CastoriceConverters.TargetGlyph;

    public static IValueConverter IsNotEmpty => CastoriceConverters.IsNotEmpty;

    public static IValueConverter CountToVisible => CastoriceConverters.CountToVisible;

    public static IValueConverter TimestampToShortTime => CastoriceConverters.TimestampToShortTime;

    public static IValueConverter SparklinePoints => CastoriceConverters.SparklinePoints;

    /// <summary>"Update" for a pool that is already imported, "Import" otherwise.</summary>
    public static IValueConverter ImportLabel { get; } =
        new Avalonia.Data.Converters.FuncValueConverter<bool, string>(imported => imported ? "Update" : "Import");
}
