namespace BusReliabilityWeb.Controllers.Dto
{
    public record DataLateness(DateOnly Date, int Hour, double? Lateness);
}
