using BusReliabilityWeb.Timetable;

namespace BusReliabilityWeb.Controllers.Dto
{
    public record DataLine(string Name, IDictionary<string, DataBusStop> BusStops, IEnumerable<TimetableLineSection> LineSections);
}
