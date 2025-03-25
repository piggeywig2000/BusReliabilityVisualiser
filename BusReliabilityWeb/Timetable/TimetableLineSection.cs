using BodsDotNet.Schemas.TransXChange;

namespace BusReliabilityWeb.Timetable
{
    public record TimetableLineSection(string FromStopPointRef, string ToStopPointRef, TimetablePoint[] Track)
    {
        public TimetableLineSection(RouteLink routeLink) :
            this(routeLink.From.StopPointRef.Value, routeLink.To.StopPointRef.Value, [])
        {
            List<TimetablePoint> track = [];
            foreach (LocationStructure loc in routeLink.Track.SelectMany(t => t.Mapping))
            {
                TimetablePoint pnt = new(loc);
                if (track.Count > 0 && pnt == track[^1])
                    continue; // Same position as last
                track.Add(pnt);
            }
            Track = [.. track];
        }
    }
}
