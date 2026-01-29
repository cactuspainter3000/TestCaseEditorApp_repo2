using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TestCaseEditorApp.MVVM.Domains.TestCaseCreation.Models
{
    /// <summary>
    /// Allows JSON values to be either a string or an array of strings.
    /// Arrays are normalized into a single string joined by "; ".
    /// </summary>
    public sealed class StringOrArrayJsonConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                return reader.GetString() ?? string.Empty;
            }

            if (reader.TokenType == JsonTokenType.Null)
            {
                return string.Empty;
            }

            if (reader.TokenType == JsonTokenType.StartArray)
            {
                var values = new List<string>();
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndArray)
                    {
                        break;
                    }

                    if (reader.TokenType == JsonTokenType.String)
                    {
                        values.Add(reader.GetString() ?? string.Empty);
                        continue;
                    }

                    if (reader.TokenType == JsonTokenType.Null)
                    {
                        continue;
                    }

                    using var doc = JsonDocument.ParseValue(ref reader);
                    values.Add(doc.RootElement.ToString());
                }

                return string.Join("; ", values.Where(v => !string.IsNullOrWhiteSpace(v)));
            }

            using (var doc = JsonDocument.ParseValue(ref reader))
            {
                return doc.RootElement.ToString();
            }
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value ?? string.Empty);
        }
    }
}
