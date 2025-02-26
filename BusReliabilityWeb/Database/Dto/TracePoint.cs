namespace BusReliabilityWeb.Database.Dto
{
    public record TracePoint (
        DateTime RecordedAt,
        string VehicleRef,
        string ServiceCode,
        string LineId,
        string TicketMachineServiceCode,
        string TicketMachineJourneyCode,
        double Easting,
        double Northing,
        double? Bearing)
    {
    }
}
