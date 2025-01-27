using BodsDotNet;

namespace BusReliabilityScraper
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            BodsClient bodsClient = new BodsClient("f3eb2d8601b48191874b770a833b29fc0238e1da");
            BodsDotNet.Schemas.Timetable.Timetable timetable = await bodsClient.GetTimetableById(5813);
            Console.WriteLine(timetable.URL);
        }
    }
}
