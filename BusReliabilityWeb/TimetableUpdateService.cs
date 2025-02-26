
using BusReliabilityWeb.Timetable;

namespace BusReliabilityWeb
{
    public class TimetableUpdateService : IHostedService, IDisposable
    {
        private readonly ILogger<TimetableUpdateService> logger;
        private readonly TimetableFileManager timetableFileManager;
        private readonly TimeOnly timeOfDayUpdate;
        private Timer? timer;

        public TimetableUpdateService(ILogger<TimetableUpdateService> logger, IConfiguration configuration, TimetableFileManager timetableFileManager)
        {
            this.logger = logger;
            this.timetableFileManager = timetableFileManager;
            timeOfDayUpdate = TimeOnly.Parse(configuration["TimetableUpdateTimeOfDay"] ?? throw new InvalidOperationException("No timetable update time of day provided"));
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("Starting timetable file manager");

            await timetableFileManager.UpdateTimetables(cancellationToken);

            timer = new(async (state) => await timetableFileManager.UpdateTimetables(cancellationToken), null,
                TimeSpan.FromDays(1) + timeOfDayUpdate.ToTimeSpan() - DateTime.UtcNow.TimeOfDay,
                TimeSpan.FromDays(1));
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            logger.LogInformation("Stopping timetable file manager");

            timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            await (timer != null ? timer.DisposeAsync() : ValueTask.CompletedTask);
            timer = null;
        }

        public void Dispose()
        {
            timer?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
