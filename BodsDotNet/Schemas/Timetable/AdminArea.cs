using System.Text.Json.Serialization;

namespace BodsDotNet.Schemas.Timetable
{
    public record AdminArea([property:JsonPropertyName("atco_code")] string AtcoCode, string Name);
}
