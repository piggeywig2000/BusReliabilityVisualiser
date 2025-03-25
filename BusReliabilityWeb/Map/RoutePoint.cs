using System;
using GeoUK;
using GeoUK.Coordinates;
using GeoUK.Ellipsoids;
using GeoUK.Projections;

namespace BusReliabilityWeb.Map
{
    public class RoutePoint(double easting, double northing, double bearing) : IEquatable<RoutePoint>
    {
        public RoutePoint(double easting, double northing) : this(easting, northing, 0) { }

        public Point Point { get; private set; } = new(easting, northing);
        public double Easting => Point.Easting;
        public double Northing => Point.Northing;
        public double RouteDistance { get; set; } = 0;
        public double Bearing { get; set; } = bearing;
        public DateTime Time { get; set; } = DateTime.UnixEpoch;

        public static RoutePoint FromWGS84(double longitude, double latitude)
        {
            LatitudeLongitude lonLat = new(latitude, longitude);
            EastingNorthing easNor = GeoUK.OSTN.Transform.Etrs89ToOsgb(lonLat);
            return new RoutePoint(easNor.Easting, easNor.Northing);
        }

        public static RoutePoint FromWGS84(double longitude, double latitude, double bearing)
        {
            RoutePoint point = FromWGS84(longitude, latitude);
            point.Bearing = bearing;
            return point;
        }

        public static RoutePoint FromTransXChange(BodsDotNet.Schemas.TransXChange.LocationStructure location)
        {
            if (!location.LongitudeSpecified || !location.LatitudeSpecified)
            {
                throw new NotImplementedException();
            }

            return FromWGS84((double)location.Longitude, (double)location.Latitude);
        }

        public static RoutePoint FromSiri(BodsDotNet.Schemas.Siri.LocationStructure location)
        {
            if (!location.LongitudeSpecified || !location.LatitudeSpecified)
            {
                throw new NotImplementedException();
            }

            return FromWGS84((double)location.Longitude, (double)location.Latitude);
        }

        public void MatchToRoute(Route route)
        {
            double closestDistanceSqr = double.PositiveInfinity;
            int closestIndex = 0;
            double closestTValue = 0;
            foreach ((int i, (RoutePoint pntFrom, RoutePoint pntTo)) in route.Points.Zip(route.Points.Skip(1), Tuple.Create).Index())
            {
                if (Bearing.AngleDifference(pntFrom.Bearing) > 60)
                    continue; // Angle difference too big

                Point startToEnd = pntTo.Point - pntFrom.Point;
                Point startToHere = Point - pntFrom.Point;
                double t = startToHere.Dot(startToEnd) / startToEnd.DistanceSqr();
                t = t.Clamp01();
                Point snapped = pntFrom.Point.LerpToUnclamped(pntTo.Point, t);

                double distanceSqr = Point.DistanceSqrTo(snapped);
                if (closestDistanceSqr > distanceSqr)
                {
                    closestDistanceSqr = distanceSqr;
                    closestIndex = i;
                    closestTValue = t;
                }
            }

            RoutePoint closestStart = route.Points[closestIndex];
            RoutePoint closestEnd = route.Points[closestIndex + 1];

            Point = closestStart.Point.LerpToUnclamped(closestEnd.Point, closestTValue);
            RouteDistance = closestStart.RouteDistance.LerpToUnclamped(closestEnd.RouteDistance, closestTValue);
        }

        public void MovePositionFromRouteDistance(Route route)
        {
            Point = route.GetPositionAtDistance(RouteDistance);
        }

        public override bool Equals(object? obj) => obj is RoutePoint rp && Equals(rp);
        public bool Equals(RoutePoint? other)
        {
            if (other is null)
                return false; // Null

            if (Object.ReferenceEquals(this, other))
                return true; // Referencial equality

            //if (GetType() != other.GetType())
            //    return false; // Different types

            return Point == other.Point;
        }

        public override int GetHashCode() => Point.GetHashCode();

        public static bool operator ==(RoutePoint? left, RoutePoint? right)
        {
            if (left is null)
            {
                if (right is null)
                    return true; // Both null
                return false; // Only left null
            }
            return left.Equals(right); // Handles right null as well
        }

        public static bool operator !=(RoutePoint? left, RoutePoint? right) => !(left == right);

        public override string ToString() => $"{Easting:D}, {Northing:D}";
    }
}
