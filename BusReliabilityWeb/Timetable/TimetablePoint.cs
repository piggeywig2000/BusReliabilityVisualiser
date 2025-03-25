using BodsDotNet.Schemas.TransXChange;

namespace BusReliabilityWeb.Timetable
{
    public record TimetablePoint(double Longitude, double Latitude)
    {
        public TimetablePoint(LocationStructure loc) : this((double)loc.Longitude, (double)loc.Latitude) { }
    }
}
