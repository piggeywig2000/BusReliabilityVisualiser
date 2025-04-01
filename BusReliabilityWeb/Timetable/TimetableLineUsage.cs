using System.Text.Json.Serialization;

namespace BusReliabilityWeb.Timetable
{
    public record TimetableLineUsage([property: JsonConverter(typeof(JsonStringEnumConverter))] DayOfWeek DayOfWeek, int Hour, int BusesPerHour)
    {
    }
}
