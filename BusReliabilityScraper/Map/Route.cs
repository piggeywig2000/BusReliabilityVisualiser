using System.Text;

namespace BusReliabilityScraper.Map
{
    internal class Route
    {
        private readonly List<RoutePoint> points = [];

        public Route() { }

        public IReadOnlyList<RoutePoint> Points => points;
        public double Distance => points.Count > 0 ? points.Last().RouteDistance : 0;
        public int PointCount => points.Count;

        public void AddPoint(RoutePoint point)
        {
            RoutePoint? lastPoint = points.LastOrDefault();
            if (lastPoint != null)
                point.RouteDistance = lastPoint.RouteDistance + point.Point.DistanceTo(lastPoint.Point);

            points.Add(point);
        }

        public void CalculateBearings()
        {
            if (points.Count <= 1)
                throw new InvalidOperationException("Not enough data points to calculate bearings");

            foreach ((int i, RoutePoint point) in points.Index())
            {
                point.Bearing = i < points.Count - 1 ? point.Point.BearingTo(points[i + 1].Point) : points[i - 1].Point.BearingTo(point.Point);
            }
        }

        public void ResolveOrder(Route plannedRoute)
        {
            int reorderAttemptsRemaining = 10000;
            for (int iGoesBackwards = FindOutOfOrderIndex(); iGoesBackwards >= 0; iGoesBackwards = FindOutOfOrderIndex())
            {
                if (--reorderAttemptsRemaining < 0)
                    throw new InvalidOperationException("Order resolution appears to be looping");

                // Find where it starts going forwards along the route again
                double lastRouteDistance = points[iGoesBackwards].RouteDistance;
                int iGoesForwards = iGoesBackwards + 1;
                while (iGoesForwards < points.Count && lastRouteDistance > points[iGoesForwards].RouteDistance)
                {
                    lastRouteDistance = points[iGoesForwards].RouteDistance;
                    iGoesForwards++;
                }

                // Change to represent where it STARTS going backwards/forwards, not where it just has done
                iGoesBackwards--;
                iGoesForwards--;

                // Try throwing away all points before where it starts going forwards,
                // up until when the next point to throw away comes before where it starts going forwards
                List<int> throwBeforeIndexes = [];
                double distStartsGoingForwards = points[iGoesForwards].RouteDistance;
                for (int i = iGoesForwards - 1; i >= 0 && points[i].RouteDistance > distStartsGoingForwards; i--)
                {
                    throwBeforeIndexes.Add(i);
                }
                throwBeforeIndexes.Sort();
                double throwBeforeCost = throwBeforeIndexes
                    .Zip(throwBeforeIndexes.Skip(1), (iFrom, iTo) => (iFrom, iTo))
                    .Sum(p => Math.Abs(points[p.iTo].RouteDistance - points[p.iFrom].RouteDistance));

                // Try throwing away all points after where it starts going backwards,
                // up until when the next point to throw away comes after where it starts going backwards
                List<int> throwAfterIndexes = [];
                double distStartsGoingBackwards = points[iGoesBackwards].RouteDistance;
                for (int i = iGoesBackwards + 1; i < points.Count && points[i].RouteDistance < distStartsGoingBackwards; i++)
                {
                    throwAfterIndexes.Add(i);
                }
                throwAfterIndexes.Sort();
                double throwAfterCost = throwAfterIndexes
                    .Zip(throwAfterIndexes.Skip(1), (iFrom, iTo) => (iFrom, iTo))
                    .Sum(p => Math.Abs(points[p.iTo].RouteDistance - points[p.iFrom].RouteDistance));

                // Throw away the points with the lowest cost, and re-distribute them along the section based on distance
                int throwRangeStart = (throwBeforeCost < throwAfterCost) ? throwBeforeIndexes[0] : throwAfterIndexes[0];
                int throwRangeEnd = (throwBeforeCost < throwAfterCost) ? throwBeforeIndexes[^1] + 1 : throwAfterIndexes[^1] + 1; // exclusive
                Console.WriteLine($"Throwing away {throwRangeEnd - throwRangeStart} points, from {throwRangeStart} to {throwRangeEnd}");

                // Set up values used for distance-based interpolation, if we're interpolating
                double totalThrowDistance = 0;
                double cumulativeThrowDistance = 0;
                if (throwRangeStart > 0 && throwRangeEnd < points.Count)
                {
                    totalThrowDistance = Enumerable.Range(throwRangeStart, throwRangeEnd - throwRangeStart + 1)
                        .Sum(i => points[i - 1].Point.DistanceTo(points[i].Point));
                }
                if (totalThrowDistance == 0)
                    totalThrowDistance = 1; // Prevent dividing by 0 if all points at same location for whatever reason (hopefully can't happen)

                // Re-distribute points and update position
                for (int i = throwRangeStart; i < throwRangeEnd; i++)
                {
                    if (throwRangeStart == 0)
                        points[i].RouteDistance = points[throwRangeStart].RouteDistance;
                    else if (throwRangeEnd == points.Count)
                        points[i].RouteDistance = points[throwRangeEnd - 1].RouteDistance;
                    else
                    {
                        cumulativeThrowDistance += points[i - 1].Point.DistanceTo(points[i].Point);
                        points[i].RouteDistance = points[throwRangeStart - 1].RouteDistance.LerpTo(points[throwRangeEnd].RouteDistance, cumulativeThrowDistance / totalThrowDistance);
                    }

                    points[i].MovePositionFromRouteDistance(plannedRoute);
                }
            }
        }

        private int FindOutOfOrderIndex()
        {
            double lastRouteDistance = 0.0;
            foreach ((int i, RoutePoint point) in points.Index())
            {
                if (point.RouteDistance < lastRouteDistance)
                    return i;
                lastRouteDistance = point.RouteDistance;
            }
            return -1;
        }

        public string GetGPX(string name, bool useWaypoints = false)
        {
            StringBuilder sb = new();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<gpx creator=\"https://gpx.studio\" version=\"1.1\" schemaLocation=\"http://www.topografix.com/GPX/1/1 http://www.topografix.com/GPX/1/1/gpx.xsd http://www.garmin.com/xmlschemas/GpxExtensions/v3 http://www.garmin.com/xmlschemas/GpxExtensionsv3.xsd http://www.garmin.com/xmlschemas/TrackPointExtension/v1 http://www.garmin.com/xmlschemas/TrackPointExtensionv1.xsd http://www.garmin.com/xmlschemas/PowerExtension/v1 http://www.garmin.com/xmlschemas/PowerExtensionv1.xsd http://www.topografix.com/GPX/gpx_style/0/2 http://www.topografix.com/GPX/gpx_style/0/2/gpx_style.xsd\" xmlns=\"http://www.topografix.com/GPX/1/1\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:schemaLocation=\"http://www.topografix.com/GPX/1/1 http://www.topografix.com/GPX/1/1/gpx.xsd http://www.garmin.com/xmlschemas/GpxExtensions/v3 http://www.garmin.com/xmlschemas/GpxExtensionsv3.xsd http://www.garmin.com/xmlschemas/TrackPointExtension/v1 http://www.garmin.com/xmlschemas/TrackPointExtensionv1.xsd http://www.garmin.com/xmlschemas/PowerExtension/v1 http://www.garmin.com/xmlschemas/PowerExtensionv1.xsd http://www.topografix.com/GPX/gpx_style/0/2 http://www.topografix.com/GPX/gpx_style/0/2/gpx_style.xsd\" xmlns:gpxtpx=\"http://www.garmin.com/xmlschemas/TrackPointExtension/v1\" xmlns:gpxx=\"http://www.garmin.com/xmlschemas/GpxExtensions/v3\" xmlns:gpxpx=\"http://www.garmin.com/xmlschemas/PowerExtension/v1\" xmlns:gpx_style=\"http://www.topografix.com/GPX/gpx_style/0/2\">");
            sb.AppendLine("\t<metadata>");
            sb.AppendLine($"\t\t<name>{name}</name>");
            sb.AppendLine("\t\t<author>");
            sb.AppendLine("\t\t\t<name>gpx.studio</name>");
            sb.AppendLine("\t\t\t<link href=\"https://gpx.studio\"/>");
            sb.AppendLine("\t\t</author>");
            sb.AppendLine("\t</metadata>");
            sb.AppendLine("\t<trk>");
            sb.AppendLine($"\t\t<name>{name}</name>");
            if (useWaypoints)
                sb.AppendLine("\t</trk>");
            else
                sb.AppendLine("\t\t<trkseg>");
            foreach ((int i, RoutePoint point) in points.Index())
            {
                (double lon, double lat) = point.ToLonLat();
                sb.Append(useWaypoints ? "\t<wpt " : "\t\t\t<trkpt ");
                sb.AppendLine($"lat=\"{lat}\" lon=\"{lon}\">");
                sb.AppendLine($"{(useWaypoints ? "" : "\t\t")}\t\t<ele>{i}</ele>");
                if (useWaypoints)
                    sb.AppendLine($"\t\t<name>{name}-{i}</name>");
                sb.AppendLine(useWaypoints ? "\t</wpt>" : "\t\t\t</trkpt>");
            }
            if (!useWaypoints)
            {
                sb.AppendLine("\t\t</trkseg>");
                sb.AppendLine("\t</trk>");
            }
            sb.AppendLine("</gpx>");
            return sb.ToString();
        }
    }
}
