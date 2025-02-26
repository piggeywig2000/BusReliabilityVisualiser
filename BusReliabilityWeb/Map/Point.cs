using System.Diagnostics.CodeAnalysis;
using GeoUK.Coordinates;

namespace BusReliabilityWeb.Map
{
    internal readonly struct Point(double easting, double northing) : IEquatable<Point>
    {
        public double Easting { get; } = easting;
        public double Northing { get; } = northing;

        public double Dot(Point other) => Easting * other.Easting + Northing * other.Northing;
        public double DistanceSqr() => Easting * Easting + Northing * Northing;
        public double DistanceSqrTo(Point other) => (this - other).DistanceSqr();
        public double Distance() => Math.Sqrt(DistanceSqr());
        public double DistanceTo(Point other) => (this - other).Distance();

        public static Point LerpUnclamped(Point from, Point to, double t)
            => new(from.Easting.LerpToUnclamped(to.Easting, t), from.Northing.LerpToUnclamped(to.Northing, t));
        public static Point Lerp(Point from, Point to, double t)
            => new(from.Easting.LerpTo(to.Easting, t), from.Northing.LerpTo(to.Northing, t));
        public Point LerpToUnclamped(Point to, double t) => LerpUnclamped(this, to, t);
        public Point LerpTo(Point to, double t) => Lerp(this, to, t);

        public static double CalculateBearing(Point from, Point to)
            // Bearing is 0 when north, goes clockwise to 360. ATan2 is -PI when west, goes anticlockwise to PI.
            => ((-Math.Atan2(to.Northing - from.Northing, to.Easting - from.Easting) + (2.5 * Math.PI)) % (2 * Math.PI)) * (180 / Math.PI);
        public double BearingTo(Point other) => CalculateBearing(this, other);

        public (double, double) ToLonLat()
        {
            Osgb36 easNor = new(Easting, Northing);
            LatitudeLongitude latLong = GeoUK.OSTN.Transform.OsgbToEtrs89(easNor);
            return (latLong.Longitude, latLong.Latitude);
        }

        public override bool Equals([NotNullWhen(true)] object? obj) => obj is Point point && Equals(point);
        public bool Equals(Point other) => Easting == other.Easting && Northing == other.Northing;
        public override int GetHashCode() => (Easting, Northing).GetHashCode();

        public static bool operator ==(Point left, Point right) => left.Equals(right);
        public static bool operator !=(Point left, Point right) => !(left == right);

        public static Point operator +(Point left, Point right) => new(left.Easting + right.Easting, left.Northing + right.Northing);
        public static Point operator +(Point left, double right) => new(left.Easting + right, left.Northing + right);
        public static Point operator +(double left, Point right) => new(left + right.Easting, left + right.Northing);
        public static Point operator -(Point left, Point right) => new(left.Easting - right.Easting, left.Northing - right.Northing);
        public static Point operator -(Point left, double right) => new(left.Easting - right, left.Northing - right);
        public static Point operator -(double left, Point right) => new(left - right.Easting, left - right.Northing);
        public static Point operator -(Point pnt) => new(-pnt.Easting, -pnt.Northing);
        public static Point operator *(Point left, Point right) => new(left.Easting * right.Easting, left.Northing * right.Northing);
        public static Point operator *(Point left, double right) => new(left.Easting * right, left.Northing * right);
        public static Point operator *(double left, Point right) => new(left * right.Easting, left * right.Northing);
        public static Point operator /(Point left, Point right) => new(left.Easting / right.Easting, left.Northing / right.Northing);
        public static Point operator /(Point left, double right) => new(left.Easting / right, left.Northing / right);
        public static Point operator /(double left, Point right) => new(left / right.Easting, left / right.Northing);
    }
}
