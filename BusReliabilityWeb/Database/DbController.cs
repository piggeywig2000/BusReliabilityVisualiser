using System.Text;
using BusReliabilityWeb.Database.Dto;
using MySqlConnector;

namespace BusReliabilityWeb.Database
{
    public class DbController
    {
        private readonly MySqlConnection db;

        public DbController(MySqlConnection db)
        {
            this.db = db;
        }

        public async Task<TracePoint?> GetLatestTracePoint(string vehicleRef, CancellationToken cancellationToken)
        {
            if (db.State == System.Data.ConnectionState.Closed)
                await db.OpenAsync(cancellationToken);
            await using MySqlCommand command = db.CreateCommand();
            command.CommandText = @"
                SELECT `trace_points`.`id`,
                    `trace_points`.`recorded_at`,
                    `trace_points`.`vehicle_ref`,
                    `trace_points`.`service_code`,
                    `trace_points`.`line_id`,
                    `trace_points`.`ticket_machine_service_code`,
                    `trace_points`.`ticket_machine_journey_code`,
                    `trace_points`.`direction`,
                    `trace_points`.`easting`,
                    `trace_points`.`northing`,
                    `trace_points`.`bearing`
                FROM `bus_visualiser`.`trace_points`
                WHERE `trace_points`.`vehicle_ref` = @vehicle_ref
                ORDER BY `trace_points`.`recorded_at` DESC
                LIMIT 1;";
            command.Parameters.AddWithValue("vehicle_ref", vehicleRef);
            await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            TracePoint? tp = await reader.ReadAsync(cancellationToken) ? new(
                reader.GetDateTime("recorded_at"),
                reader.GetString("vehicle_ref"),
                reader.GetString("service_code"),
                reader.GetString("line_id"),
                reader.GetString("ticket_machine_service_code"),
                reader.GetString("ticket_machine_journey_code"),
                reader.GetString("direction"),
                reader.GetDouble("easting"),
                reader.GetDouble("northing"),
                reader.IsDBNull(reader.GetOrdinal("bearing")) ? null : reader.GetDouble("bearing")) : null;
            return tp;
        }

        public async Task AddTracePoint(TracePoint tracePoint, CancellationToken cancellationToken)
        {
            if (db.State == System.Data.ConnectionState.Closed)
                await db.OpenAsync(cancellationToken);
            await using MySqlTransaction transaction = await db.BeginTransactionAsync(cancellationToken);
            await using MySqlCommand command = db.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
                INSERT INTO `bus_visualiser`.`trace_points`
                    (`recorded_at`,
                    `vehicle_ref`,
                    `service_code`,
                    `line_id`,
                    `ticket_machine_service_code`,
                    `ticket_machine_journey_code`,
                    `direction`,
                    `easting`,
                    `northing`,
                    `bearing`)
                VALUES
                    (@recorded_at,
                     @vehicle_ref,
                     @service_code,
                     @line_id,
                     @ticket_machine_service_code,
                     @ticket_machine_journey_code,
                     @direction,
                     @easting,
                     @northing,
                     @bearing);";
            command.Parameters.AddWithValue("recorded_at", tracePoint.RecordedAt);
            command.Parameters.AddWithValue("vehicle_ref", tracePoint.VehicleRef);
            command.Parameters.AddWithValue("service_code", tracePoint.ServiceCode);
            command.Parameters.AddWithValue("line_id", tracePoint.LineId);
            command.Parameters.AddWithValue("ticket_machine_service_code", tracePoint.TicketMachineServiceCode);
            command.Parameters.AddWithValue("ticket_machine_journey_code", tracePoint.TicketMachineJourneyCode);
            command.Parameters.AddWithValue("direction", tracePoint.Direction);
            command.Parameters.AddWithValue("easting", tracePoint.Easting);
            command.Parameters.AddWithValue("northing", tracePoint.Northing);
            command.Parameters.AddWithValue("bearing", (object?)tracePoint.Bearing ?? DBNull.Value);
            int rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
            if (rowsAffected != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                throw new Exception($"Failed to insert trace point. Rows affected was {rowsAffected}.");
            }
            await transaction.CommitAsync(cancellationToken);
        }

        public async Task AddTracePoints(IReadOnlyCollection<TracePoint> tracePoints, CancellationToken cancellationToken)
        {
            if (db.State == System.Data.ConnectionState.Closed)
                await db.OpenAsync(cancellationToken);
            await using MySqlTransaction transaction = await db.BeginTransactionAsync(cancellationToken);
            await using MySqlCommand command = db.CreateCommand();
            command.Transaction = transaction;
            StringBuilder sb = new();
            sb.Append(@"
                INSERT INTO `bus_visualiser`.`trace_points`
                    (`recorded_at`,
                    `vehicle_ref`,
                    `service_code`,
                    `line_id`,
                    `ticket_machine_service_code`,
                    `ticket_machine_journey_code`,
                    `direction`,
                    `easting`,
                    `northing`,
                    `bearing`)
                VALUES
");
            for (int i = 0; i < tracePoints.Count; i++)
            {
                if (i > 0)
                    sb.AppendLine(",");
                sb.Append($@"
                    (@recorded_at_{i},
                     @vehicle_ref_{i},
                     @service_code_{i},
                     @line_id_{i},
                     @ticket_machine_service_code_{i},
                     @ticket_machine_journey_code_{i},
                     @direction_{i},
                     @easting_{i},
                     @northing_{i},
                     @bearing_{i})");
            }
            sb.Append(';');
            command.CommandText = sb.ToString();
            foreach ((int i, TracePoint tracePoint) in tracePoints.Index())
            {
                command.Parameters.AddWithValue($"recorded_at_{i}", tracePoint.RecordedAt);
                command.Parameters.AddWithValue($"vehicle_ref_{i}", tracePoint.VehicleRef);
                command.Parameters.AddWithValue($"service_code_{i}", tracePoint.ServiceCode);
                command.Parameters.AddWithValue($"line_id_{i}", tracePoint.LineId);
                command.Parameters.AddWithValue($"ticket_machine_service_code_{i}", tracePoint.TicketMachineServiceCode);
                command.Parameters.AddWithValue($"ticket_machine_journey_code_{i}", tracePoint.TicketMachineJourneyCode);
                command.Parameters.AddWithValue($"direction_{i}", tracePoint.Direction);
                command.Parameters.AddWithValue($"easting_{i}", tracePoint.Easting);
                command.Parameters.AddWithValue($"northing_{i}", tracePoint.Northing);
                command.Parameters.AddWithValue($"bearing_{i}", (object?)tracePoint.Bearing ?? DBNull.Value);
            }
            int rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
            if (rowsAffected != tracePoints.Count)
            {
                await transaction.RollbackAsync(cancellationToken);
                throw new Exception($"Failed to insert trace points. Rows affected was {rowsAffected}, should've been {tracePoints.Count}.");
            }
            await transaction.CommitAsync(cancellationToken);
        }
    }
}
