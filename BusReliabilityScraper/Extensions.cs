using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusReliabilityScraper
{
    internal static class DoubleExtensions
    {
        public static double AngleDifference(this double thisAngle, double otherAngle)
        {
            double diff = Math.Abs(thisAngle - otherAngle);
            return diff <= 180 ? diff : 360.0 - diff;
        }

        public static double Clamp(this double value, double min, double max)
            => Math.Min(Math.Max(value, min), max);
        public static double Clamp01(this double value)
            => Math.Min(Math.Max(value, 0), 1);

        public static double LerpToUnclamped(this double thisVal, double other, double t)
            => thisVal + ((other - thisVal) * t);
        public static double LerpTo(this double thisVal, double other, double t)
            => thisVal.LerpTo(other, t.Clamp01());
    }
}
