using BodsDotNet.Schemas.TransXChange;

namespace BusReliabilityWeb.Timetable
{
    public record TimetableLineSection(string FromStopPointRef, string ToStopPointRef, TimetablePoint[] Track, IReadOnlyCollection<TimetableLineUsage> Usage)
    {
        public TimetableLineSection(RouteLink routeLink, Map.Route[] journeys) :
            this(routeLink.From.StopPointRef.Value, routeLink.To.StopPointRef.Value, [], [])
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

            Dictionary<(DayOfWeek dow, int hour), int> usageDict = [];
            foreach (Map.Route journey in journeys)
            {
                // Does this journey go over this route section?
                for (int iStop = 0; iStop < journey.StopCount - 1; iStop++)
                {
                    Map.Stop fromStop = journey.Stops[iStop];
                    if (fromStop.Naptan == FromStopPointRef && journey.Stops[iStop + 1].Naptan == ToStopPointRef)
                    {
                        DateOnly date = DateOnly.FromDateTime(fromStop.DepartureTime);
                        TimeOnly time = TimeOnly.FromDateTime(fromStop.DepartureTime);
                        if (time >= new TimeOnly(04, 00) && time < new TimeOnly(05, 00))
                            continue; // Don't bother if between 4am and 5am (timetable switches to next day, I can't be bothered to deal with this)
                        else if (time < new TimeOnly(04, 00))
                            date = date.AddDays(-1); // Use yesterday's timetable if before 4am

                        int hour = time < new TimeOnly(04, 00) ? time.Hour + 24 : time.Hour;

                        if (!usageDict.TryGetValue((date.DayOfWeek, hour), out int count))
                            count = 0;
                        usageDict[(date.DayOfWeek, hour)] = count + 1;
                    }
                }
            }

            Usage = usageDict.Select(kvp => new TimetableLineUsage(kvp.Key.dow, kvp.Key.hour, kvp.Value)).ToArray();
        }
    }
}
