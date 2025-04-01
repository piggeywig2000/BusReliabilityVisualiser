using BodsDotNet.Schemas.TransXChange;

namespace BusReliabilityWeb
{
    public static class Util
    {
        public static TimeZoneInfo GmtTimeZone => TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
        public static DateOnly GmtNowDate => DateOnly.FromDateTime(GmtNow);
        public static TimeOnly GmtNowTime => TimeOnly.FromDateTime(GmtNow);
        public static DateTime GmtNow => DateTime.UtcNow.ConvertUtcToGmt();
        public static DateTime ConvertUtcToGmt(this DateTime dt) => TimeZoneInfo.ConvertTimeFromUtc(dt.ToUniversalTime(), GmtTimeZone);

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
            => thisVal.LerpToUnclamped(other, t.Clamp01());

        public static bool RunsOnDate(this OperatingProfileStructure operatingProfile, DateOnly date)
        {
            // Special days
            if (operatingProfile.SpecialDaysOperation != null && operatingProfile.SpecialDaysOperation.DaysOfNonOperationSpecified &&
                operatingProfile.SpecialDaysOperation.DaysOfNonOperation.Any(range => DateOnly.FromDateTime(range.StartDate) >= date && DateOnly.FromDateTime(range.EndDate) <= date))
            {
                return false; // Guarenteed to not run (takes precedence if conflicts)
            }
            if (operatingProfile.SpecialDaysOperation != null && operatingProfile.SpecialDaysOperation.DaysOfOperationSpecified &&
                operatingProfile.SpecialDaysOperation.DaysOfOperation.Any(range => DateOnly.FromDateTime(range.StartDate) >= date && DateOnly.FromDateTime(range.EndDate) <= date))
            {
                return true; // Guarenteed to run
            }

            // Bank holidays
            if (operatingProfile.BankHolidayOperation != null && operatingProfile.BankHolidayOperation.DaysOfNonOperation != null &&
                operatingProfile.BankHolidayOperation.DaysOfNonOperation.IsSpecifiedBankHoliday(date))
            {
                return false; // Guarenteed to not run (takes precedence if conflicts)
            }
            if (operatingProfile.BankHolidayOperation != null && operatingProfile.BankHolidayOperation.DaysOfOperation != null &&
                operatingProfile.BankHolidayOperation.DaysOfOperation.IsSpecifiedBankHoliday(date))
            {
                return true; // Guarenteed to run
            }

            // Regular operation days
            bool correctDay = operatingProfile.RegularDayType.CorrectDayOfWeek(date.DayOfWeek) &&
                (!operatingProfile.PeriodicDayTypeSpecified || CorrectWeekOfMonth(operatingProfile.PeriodicDayType, date));

            return correctDay;
        }
        public static void GetRegularDays(this OperatingProfileStructure operatingProfile, out DayOfWeek[] regularDays)
        {
            List<DayOfWeek> days = [];
            if (operatingProfile.RegularDayType.CorrectDayOfWeek(DayOfWeek.Monday))
                days.Add(DayOfWeek.Monday);
            if (operatingProfile.RegularDayType.CorrectDayOfWeek(DayOfWeek.Tuesday))
                days.Add(DayOfWeek.Tuesday);
            if (operatingProfile.RegularDayType.CorrectDayOfWeek(DayOfWeek.Wednesday))
                days.Add(DayOfWeek.Wednesday);
            if (operatingProfile.RegularDayType.CorrectDayOfWeek(DayOfWeek.Thursday))
                days.Add(DayOfWeek.Thursday);
            if (operatingProfile.RegularDayType.CorrectDayOfWeek(DayOfWeek.Friday))
                days.Add(DayOfWeek.Friday);
            if (operatingProfile.RegularDayType.CorrectDayOfWeek(DayOfWeek.Saturday))
                days.Add(DayOfWeek.Saturday);
            if (operatingProfile.RegularDayType.CorrectDayOfWeek(DayOfWeek.Sunday))
                days.Add(DayOfWeek.Sunday);

            regularDays = [.. days];
        }
        private static bool CorrectDayOfWeek(this RegularOperationStructure regularOperation, DayOfWeek dow)
        {
            if (regularOperation.HolidaysOnlySpecified)
                return false; // Only runs on exceptional days

            RegularOperationStructureDaysOfWeek opDow = regularOperation.DaysOfWeek;

            if (regularOperation.DaysOfWeek.MondayToSundaySpecified)
                return true;
            if (regularOperation.DaysOfWeek.MondayToSaturdaySpecified && dow != DayOfWeek.Sunday)
                return true;

            return dow switch
            {
                DayOfWeek.Monday => opDow.MondayToFridaySpecified || opDow.MondaySpecified,
                DayOfWeek.Tuesday => opDow.MondayToFridaySpecified || opDow.TuesdaySpecified,
                DayOfWeek.Wednesday => opDow.MondayToFridaySpecified || opDow.WednesdaySpecified,
                DayOfWeek.Thursday => opDow.MondayToFridaySpecified || opDow.ThursdaySpecified,
                DayOfWeek.Friday => opDow.MondayToFridaySpecified || opDow.FridaySpecified,
                DayOfWeek.Saturday => opDow.WeekendSpecified || opDow.SaturdaySpecified,
                DayOfWeek.Sunday => opDow.WeekendSpecified || opDow.SundaySpecified,
                _ => throw new NotImplementedException()
            };
        }
        private static bool CorrectWeekOfMonth(System.Collections.ObjectModel.Collection<WeekOfMonthStructure> periodicOperation, DateOnly date)
            => periodicOperation.Any(wom => wom.WeekNumber != WeekInMonthEnumeration.Last ?
                (int)wom.WeekNumber == date.Day / 7 : // First/second/third/etc week in month
                date.AddDays(7).Month != date.Month); // Last day of week in month
        private static bool IsSpecifiedBankHoliday(this BankHolidaysStructure holidays, DateOnly date)
        {
            DateOnly easterSunday = CalculateEasterSundayForYear(date.Year);

            if ((holidays.NewYearsDaySpecified || holidays.AllHolidaysExceptChristmasSpecified || holidays.AllBankHolidaysSpecified) && date.Month == 1 && date.Day == 1)
                return true;

            if ((holidays.Jan2NdScotlandSpecified || holidays.AllHolidaysExceptChristmasSpecified || holidays.AllBankHolidaysSpecified) && date.Month == 1 && date.Day == 2)
                return true;

            if ((holidays.GoodFridaySpecified || holidays.AllHolidaysExceptChristmasSpecified || holidays.AllBankHolidaysSpecified) && date == easterSunday.AddDays(-2))
                return true;

            if ((holidays.StAndrewsDaySpecified || holidays.AllHolidaysExceptChristmasSpecified || holidays.AllBankHolidaysSpecified) && date.Month == 11 && date.Day == 30)
                return true;

            // Monday bank holidays
            if ((holidays.EasterMondaySpecified || holidays.HolidayMondaysSpecified || holidays.AllHolidaysExceptChristmasSpecified || holidays.AllBankHolidaysSpecified) && date == easterSunday.AddDays(1))
                return true;

            if ((holidays.MayDaySpecified || holidays.HolidayMondaysSpecified || holidays.AllHolidaysExceptChristmasSpecified || holidays.AllBankHolidaysSpecified) && date.DayOfWeek == DayOfWeek.Monday && date.Month == 5 && date.Day <= 7)
                return true;

            if ((holidays.SpringBankSpecified || holidays.HolidayMondaysSpecified || holidays.AllHolidaysExceptChristmasSpecified || holidays.AllBankHolidaysSpecified) && date.DayOfWeek == DayOfWeek.Monday && date.Month == 5 && date.Day >= 25)
                return true;

            if ((holidays.LateSummerBankHolidayNotScotlandSpecified || holidays.HolidayMondaysSpecified || holidays.AllHolidaysExceptChristmasSpecified || holidays.AllBankHolidaysSpecified) && date.DayOfWeek == DayOfWeek.Monday && date.Month == 8 && date.Day >= 25)
                return true;

            if ((holidays.AugustBankHolidayScotlandSpecified || holidays.HolidayMondaysSpecified || holidays.AllHolidaysExceptChristmasSpecified || holidays.AllBankHolidaysSpecified) && date.DayOfWeek == DayOfWeek.Monday && date.Month == 8 && date.Day <= 7)
                return true;

            // Christmas
            if ((holidays.ChristmasDaySpecified || holidays.ChristmasSpecified || holidays.AllBankHolidaysSpecified) && date.Month == 12 && date.Day == 25)
                return true;

            if ((holidays.BoxingDaySpecified || holidays.ChristmasSpecified || holidays.AllBankHolidaysSpecified) && date.Month == 12 && date.Day == 26)
                return true;

            // Early run off (aka 'eve')
            if ((holidays.ChristmasEveSpecified || holidays.EarlyRunOffDaysSpecified) && date.Month == 12 && date.Day == 23)
                return true;

            if ((holidays.NewYearsEveSpecified || holidays.EarlyRunOffDaysSpecified) && date.Month == 12 && date.Day == 31)
                return true;

            return false;
        }
        private static DateOnly CalculateEasterSundayForYear(int year)
        {
            // Gauss's Easter Algorithm
            int a = year % 19;
            int b = year / 100;
            int c = year % 100;
            int d = b / 4;
            int e = b % 4;
            int f = (b + 8) / 25;
            int g = (b - f + 1) / 3;
            int h = (19 * a + b - d - g + 15) % 30;
            int i = c / 4;
            int k = c % 4;
            int l = (32 + 2 * e + 2 * i - h - k) % 7;
            int m = (a + 11 * h + 22 * l) / 451;
            int month = (h + l - 7 * m + 114) / 31;
            int day = ((h + l - 7 * m + 114) % 31) + 1;

            return new DateOnly(year, month, day);
        }

        public record TransXChangeDicts
        {
            public TransXChangeDicts(TransXChange txc)
            {
                StopPoints = txc.StopPoints.AnnotatedStopPointRef.ToDictionary(sp => sp.StopPointRef);
                RouteLinks = txc.RouteSections.RouteSection.SelectMany(rs => rs.RouteLink).ToDictionary(rl => rl.Id);
                JourneyPatternSections = txc.JourneyPatternSections.JourneyPatternSection.ToDictionary(jps => jps.Id);
            }

            public IReadOnlyDictionary<string, AnnotatedStopPointRef> StopPoints { get; }
            public IReadOnlyDictionary<string, RouteLink> RouteLinks { get; }
            public IReadOnlyDictionary<string, JourneyPatternSection> JourneyPatternSections { get; }
        }
    }
}
