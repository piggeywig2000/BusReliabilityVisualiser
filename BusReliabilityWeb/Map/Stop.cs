namespace BusReliabilityWeb.Map
{
    internal class Stop(string @ref, string name, double routeDistance, Point point, DateTime departureTime)
    {
        public string Ref { get; } = @ref;
        public string Name { get; } = name;
        public double RouteDistance { get; } = routeDistance;
        public Point Point { get; set; } = point;
        public DateTime DepartureTime { get; set; } = departureTime;
    }
}
