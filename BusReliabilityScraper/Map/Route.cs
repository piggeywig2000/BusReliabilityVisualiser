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

        public string GetGPX(string name)
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
            sb.AppendLine("\t\t<trkseg>");
            foreach (RoutePoint point in points)
            {
                (double lon, double lat) = point.ToLonLat();
                sb.AppendLine($"\t\t\t<trkpt lat=\"{lat}\" lon=\"{lon}\">");
                sb.AppendLine($"\t\t\t\t<ele>{point.Bearing}</ele>");
                sb.AppendLine("\t\t\t</trkpt>");
            }
            sb.AppendLine("\t\t</trkseg>");
            sb.AppendLine("\t</trk>");
            sb.AppendLine("</gpx>");
            return sb.ToString();
        }
    }
}
