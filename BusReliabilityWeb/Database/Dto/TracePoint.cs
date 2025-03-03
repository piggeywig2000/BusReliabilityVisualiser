namespace BusReliabilityWeb.Database.Dto
{
    public class TracePoint(
        DateTime recordedAt,
        string vehicleRef,
        string serviceCode,
        string lineId,
        string ticketMachineServiceCode,
        string ticketMachineJourneyCode,
        string direction,
        double easting,
        double northing,
        double? bearing)
    {
        public DateTime RecordedAt { get; set; } = recordedAt;
        public string VehicleRef { get; set; } = vehicleRef;
        public string ServiceCode { get; set; } = serviceCode;
        public string LineId { get; set; } = lineId;
        public string TicketMachineServiceCode { get; set; } = ticketMachineServiceCode;
        public string TicketMachineJourneyCode { get; set; } = ticketMachineJourneyCode;
        public string Direction { get; set; } = direction;
        public double Easting { get; set; } = easting;
        public double Northing { get; set; } = northing;
        public double? Bearing { get; set; } = bearing;
    }
}
