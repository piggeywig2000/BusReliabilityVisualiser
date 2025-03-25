using BodsDotNet.Schemas.TransXChange;

namespace BusReliabilityWeb.Timetable
{
    public record TimetableBusStop(string StopPointRef, string Name)
    {
        public TimetableBusStop(AnnotatedStopPointRef stopPoint) : this(stopPoint.StopPointRef, stopPoint.CommonName.Value)
        {
        }
    }
}
