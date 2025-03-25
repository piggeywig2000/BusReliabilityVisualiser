interface Window {
    AppConfig: {
        STADIA_API_KEY: string;
    };
}

// Create map
const map: L.Map = L.map("map").setView([51.3776019, -2.3567216], 14);

//L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
//    maxZoom: 19,
//    attribution: `&copy; <a href="http://www.openstreetmap.org/copyright">OpenStreetMap</a>`,
//    className: "tile-layer-greyscale"
//}).addTo(map); // OpenStreetMap layer

L.tileLayer(`https://tiles.stadiamaps.com/tiles/alidade_smooth/{z}/{x}/{y}{r}.png?api_key=${window.AppConfig.STADIA_API_KEY}`, {
    maxZoom: 20,
    minZoom: 12,
    attribution: `&copy; <a href="https://stadiamaps.com/" target="_blank">Stadia Maps</a>, &copy; <a href="https://openmaptiles.org/" target="_blank">OpenMapTiles</a> &copy; <a href="https://www.openstreetmap.org/copyright" target="_blank">OpenStreetMap</a>`,
}).addTo(map); // Stadia maps layers

class BusData implements DataResponse {
    lines: { [name: string]: BusLine; };

    private constructor(dataResponse: DataResponse) {
        this.lines = {};
        for (const lineName in dataResponse.lines) {
            this.lines[lineName] = BusLine.fromData(dataResponse.lines[lineName]);
        }
    }

    static fromData(dataResponse: DataResponse): BusData {
        return new BusData(dataResponse);
    }
}

class BusLine implements DataLine {
    name: string;
    busStops: { [stopPointRef: string]: DataBusStop };
    lineSections: BusLineSection[];

    private constructor(dataLine: DataLine) {
        this.name = dataLine.name;
        this.busStops = dataLine.busStops;
        this.lineSections = dataLine.lineSections.map(dls => BusLineSection.fromData(dls, this.busStops));
    }

    static fromData(dataLine: DataLine): BusLine {
        return new BusLine(dataLine);
    }
}

class BusLineSection implements DataLineSection {
    fromStopPointRef: string;
    toStopPointRef: string;
    track: DataTrack[];

    fromStopPoint: DataBusStop;
    toStopPoint: DataBusStop;

    private constructor(dataLineSection: DataLineSection, busStops: { [stopPointRef: string]: DataBusStop }) {
        this.fromStopPointRef = dataLineSection.fromStopPointRef;
        this.toStopPointRef = dataLineSection.toStopPointRef;
        this.track = dataLineSection.track;
        this.fromStopPoint = busStops[this.fromStopPointRef];
        this.toStopPoint = busStops[this.toStopPointRef];
    }

    static fromData(dataLineSection: DataLineSection, busStops: { [stopPointRef: string]: DataBusStop }): BusLineSection {
        return new BusLineSection(dataLineSection, busStops);
    }
}

function latenessToColour(lateness: number): string {
    const GRN = 0;
    const RED = 10;
    const hue = Math.max(0, Math.min(120, 120 - ((lateness - GRN) / (RED - GRN)) * 120));
    return `hsl(${hue}, 100%, 50%)`;
}

async function init() {
    const response: Response = await fetch("api/data");
    const rawData: DataResponse = JSON.parse(await response.text(), dataReviver) as DataResponse;
    const data: BusData = BusData.fromData(rawData);
    for (const lineSection of data.lines["U1"].lineSections) {
        // Get lateness values for both end of the line
        let fromTotal = 0;
        let fromLateness = lineSection.fromStopPoint.latenessValues
            .filter(dl => dl.date.getDay() != 0 && dl.date.getDay() != 6)
            .map(dl => dl.lateness)
            .reduce((accumulator, current) => {
            if (current !== null) {
                fromTotal++;
                return (accumulator ?? 0) + current;
            }
            return accumulator;
        }, 0);
        let toTotal = 0;
        let toLateness = lineSection.toStopPoint.latenessValues
            .filter(dl => dl.date.getDay() != 0 && dl.date.getDay() != 6)
            .map(dl => dl.lateness)
            .reduce((accumulator, current) => {
            if (current !== null) {
                toTotal++;
                return (accumulator ?? 0) + current;
            }
            return accumulator;
        }, 0);

        // Calculate average lateness
        if (fromTotal === 0 && toTotal === 0) {
            continue; // No data on both sides
        }
        fromLateness = fromTotal === 0 ? null : (fromLateness ?? 0) / fromTotal;
        toLateness = toTotal === 0 ? null : (toLateness ?? 0) / toTotal;

        // Generate lines on the map
        for (let i = 0; i < lineSection.track.length - 1; i++) {
            let colour: string | null = null;
            if (fromLateness !== null && toLateness !== null) {
                let proportion = (i + 1) / (lineSection.track.length + 1);
                colour = latenessToColour(((1 - proportion) * fromLateness) + (proportion * toLateness));
            } else if (fromLateness !== null) {
                colour = latenessToColour(fromLateness);
            } else if (toLateness !== null) {
                colour = latenessToColour(toLateness);
            }
            L.polyline(
                lineSection.track
                    .slice(i, i + 2)
                    .map(track => L.latLng(track.latitude, track.longitude)),
                {
                    color: colour ?? "black",
                    weight: 6,
                    offset: -3
                }).addTo(map);
        }
    }
}

init();