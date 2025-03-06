namespace BusReliabilityWeb.Database.Dto
{
    public class LatenessValue(string serviceCode, string lineId, string stopNaptan, DateOnly timetableDate, int hour, double? lateness)
    {
        public string ServiceCode { get; set; } = serviceCode;
        public string LineId { get; set; } = lineId;
        public string StopNaptan { get; set; } = stopNaptan;
        public DateOnly TimetableDate { get; set; } = timetableDate;
        public int Hour { get; set; } = hour;
        public double? Lateness { get; set; } = lateness;
    }
}
