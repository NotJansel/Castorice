using System.Text.Json;
using System.Text.Json.Serialization;

namespace Castorice.Core.Osu.Models;

/// <summary>
/// Reads a score's mods in either shape the osu! API uses: the legacy list of acronyms
/// (<c>["HD","DT"]</c>) or the current list of mod objects (<c>[{"acronym":"HD"},
/// {"acronym":"DT","settings":{"speed_change":1.3}}]</c>). Which one arrives depends on the
/// <c>x-api-version</c> header, so the model accepts both rather than betting on one.
/// </summary>
public sealed class OsuModListConverter : JsonConverter<IReadOnlyList<string>>
{
    /// <summary>
    /// Lazer tags every score set on stable with Classic. It describes where the score came from,
    /// not a choice the player made, so it is left out of what the profile shows.
    /// </summary>
    private const string ClassicMarker = "CL";

    /// <summary>
    /// Without this, System.Text.Json assigns a JSON <c>null</c> straight to the property and never
    /// calls <see cref="Read"/> — leaving a null list that the display code would trip over.
    /// </summary>
    public override bool HandleNull => true;

    public override IReadOnlyList<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.Null)
        {
            return [];
        }

        if (reader.TokenType is not JsonTokenType.StartArray)
        {
            throw new JsonException($"Expected a list of mods, found {reader.TokenType}.");
        }

        var acronyms = new List<string>();

        while (reader.Read() && reader.TokenType is not JsonTokenType.EndArray)
        {
            string? acronym = reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.StartObject => ReadAcronymFromObject(ref reader),
                _ => throw new JsonException($"Unexpected {reader.TokenType} in a mod list."),
            };

            if (!string.IsNullOrWhiteSpace(acronym) &&
                !acronym.Equals(ClassicMarker, StringComparison.OrdinalIgnoreCase))
            {
                acronyms.Add(acronym.ToUpperInvariant());
            }
        }

        return acronyms;
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var acronym in value)
        {
            writer.WriteStringValue(acronym);
        }

        writer.WriteEndArray();
    }

    /// <summary>Takes <c>acronym</c> from a mod object and skips everything else, settings included.</summary>
    private static string? ReadAcronymFromObject(ref Utf8JsonReader reader)
    {
        string? acronym = null;

        while (reader.Read() && reader.TokenType is not JsonTokenType.EndObject)
        {
            if (reader.TokenType is not JsonTokenType.PropertyName)
            {
                continue;
            }

            var name = reader.GetString();
            reader.Read();

            if (name == "acronym" && reader.TokenType is JsonTokenType.String)
            {
                acronym = reader.GetString();
            }
            else
            {
                // Settings are an object of their own; Skip walks past the whole value.
                reader.Skip();
            }
        }

        return acronym;
    }
}
