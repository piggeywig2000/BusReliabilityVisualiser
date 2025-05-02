# BusReliabilityVisualiser

The completed system can be used at https://piggeywig2000.dev/busvisualiser.
Note that this version is showing a snapshot of the visualisation from 04/04/2025, and new data is not still being collected.

## Instructions to Build and Run the System
Create a MySQL database. To create the required users and tables, run the SQL in the sql.txt file, replacing the created user's password with your own. This is where the bus location data and the calculated lateness values will be stored.

Create the file BusReliabilityWeb/appsettings.Private.json with the following contents:
```json
{
  "ConnectionStrings": {
    "bus_visualiser": "server=localhost;user=busvisualiserwebapp;password=PASSWORD;database=bus_visualiser"
  },
  "BodsApiKey": "BUS_OPEN_DATA_SERVICE_KEY",
  "StadiaApiKey": "STADIA_MAPS_API_KEY"
}
```
Replace `PASSWORD` with the SQL user's password, `BUS_OPEN_DATA_SERVICE_KEY` with your [BODS Account API Key](https://data.bus-data.dft.gov.uk/account/settings/), and `STADIA_MAPS_API_KEY` with your [Stadia Maps API Key](https://client.stadiamaps.com/dashboard).

Build the project with the [.NET 9.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/9.0), either by using the CLI or by opening the project in Visual Studio 2022.