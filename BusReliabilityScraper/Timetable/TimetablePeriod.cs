using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BusReliabilityScraper.Timetable
{
    internal record TimetablePeriod(string XmlPath, int ServiceIndex, DateOnly StartDate, DateOnly? EndDate)
    {
        //public string XmlPath { get; } = xmlPath;
        //public int ServiceIndex { get; } = serviceIndex;
        //public DateOnly StartDate { get; } = startDate;
        //public DateOnly? EndDate { get; } = endDate;

        public bool IsValidAtDate(DateOnly date) => date >= StartDate && (!EndDate.HasValue || date <= EndDate);
    }
}
