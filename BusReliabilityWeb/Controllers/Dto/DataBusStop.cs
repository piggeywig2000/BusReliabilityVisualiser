using BusReliabilityWeb.Timetable;

namespace BusReliabilityWeb.Controllers.Dto
{
    public record DataBusStop(string StopPointRef, string Name, IEnumerable<DataLateness> LatenessValues) : TimetableBusStop(StopPointRef, Name)
    {
    }
}
