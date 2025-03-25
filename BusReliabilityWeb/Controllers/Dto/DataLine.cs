using BusReliabilityWeb.Timetable;

namespace BusReliabilityWeb.Controllers.Dto
{
    public record DataLine(string Name, IEnumerable<DataBusStop> BusStops, IEnumerable<TimetableLineSection> LineSections);
}
