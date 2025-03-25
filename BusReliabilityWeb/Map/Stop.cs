namespace BusReliabilityWeb.Map
{
    public class Stop(string naptan, string name, double routeDistance, Point point, DateTime departureTime)
    {
        public string Naptan { get; } = naptan;
        public string Name { get; } = name;
        public double RouteDistance { get; } = routeDistance;
        public Point Point { get; set; } = point;
        public DateTime DepartureTime { get; set; } = departureTime;
    }
}
