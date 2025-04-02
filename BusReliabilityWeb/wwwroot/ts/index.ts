interface Window {
    AppConfig: {
        STADIA_API_KEY: string;
    };
}

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
    usage: DataLineUsage[];

    fromStopPoint: DataBusStop;
    toStopPoint: DataBusStop;

    private constructor(dataLineSection: DataLineSection, busStops: { [stopPointRef: string]: DataBusStop }) {
        this.fromStopPointRef = dataLineSection.fromStopPointRef;
        this.toStopPointRef = dataLineSection.toStopPointRef;
        this.track = dataLineSection.track;
        this.usage = dataLineSection.usage;
        this.fromStopPoint = busStops[this.fromStopPointRef];
        this.toStopPoint = busStops[this.toStopPointRef];
    }

    static fromData(dataLineSection: DataLineSection, busStops: { [stopPointRef: string]: DataBusStop }): BusLineSection {
        return new BusLineSection(dataLineSection, busStops);
    }
}

// References to elements
const linesList: HTMLDivElement = document.getElementById("lines-list") as HTMLDivElement;
const lineEles: { [name: string]: HTMLInputElement; } = {};

// Data
let data: BusData | null = null;
let currentLine: string | null = "all";

// Create map
const map: L.Map = L.map("map").setView([51.3776019, -2.3567216], 14);
//L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
//    maxZoom: 19,
//    attribution: `&copy; <a href="http://www.openstreetmap.org/copyright">OpenStreetMap</a>`,
//    className: "tile-layer-greyscale"
//}).addTo(map); // OpenStreetMap layer
//L.tileLayer(`https://tiles.stadiamaps.com/tiles/alidade_smooth/{z}/{x}/{y}{r}.png?api_key=${window.AppConfig.STADIA_API_KEY}`, {
//    maxZoom: 20,
//    minZoom: 12,
//    attribution: `&copy; <a href="https://stadiamaps.com/" target="_blank">Stadia Maps</a>, &copy; <a href="https://openmaptiles.org/" target="_blank">OpenMapTiles</a> &copy; <a href="https://www.openstreetmap.org/copyright" target="_blank">OpenStreetMap</a>`,
//}).addTo(map); // Stadia maps layer
L.tileLayer(`https://tiles.stadiamaps.com/tiles/alidade_smooth/{z}/{x}/{y}{r}.png`, {
    maxZoom: 20,
    minZoom: 12,
    attribution: `&copy; <a href="https://stadiamaps.com/" target="_blank">Stadia Maps</a>, &copy; <a href="https://openmaptiles.org/" target="_blank">OpenMapTiles</a> &copy; <a href="https://www.openstreetmap.org/copyright" target="_blank">OpenStreetMap</a>`,
}).addTo(map); // Stadia maps layer no key

let linesLayer: L.LayerGroup<any> = L.layerGroup().addTo(map);

async function init(): Promise<void> {
    await initData();
    initLines();
    redrawMap();
}

async function initData(): Promise<void> {
    const response: Response = await fetch("api/data");
    const rawData: DataResponse = JSON.parse(await response.text(), dataReviver) as DataResponse;
    data = BusData.fromData(rawData);
}

function initLines(): void {
    if (data === null) return;

    createLineElement("All Bus Lines", "all");

    Object.keys(data.lines).sort().forEach(lineName => {
        if (data === null) return;
        const line: BusLine = data.lines[lineName];
        createLineElement(line.name, line.name);
    });

    lineEles["all"].checked = true;
}

function createLineElement(name: string, id: string): void {
    const lineInput: HTMLInputElement = document.createElement("input");
    lineInput.type = "checkbox";
    lineInput.checked = false;
    lineInput.id = `line-item-${id}`;
    lineInput.name = `line-item-${id}`;
    lineInput.value = id;
    const lineLabel: HTMLLabelElement = document.createElement("label");
    lineLabel.htmlFor = `line-item-${id}`;
    lineLabel.classList.add("line-item");
    const lineText: HTMLSpanElement = document.createElement("span");
    lineText.innerText = name;
    lineLabel.appendChild(lineInput).addEventListener("change", onLineChange);
    lineLabel.appendChild(lineText);
    linesList.appendChild(lineLabel);
    lineEles[id] = lineInput;
}

function onLineChange(this: HTMLInputElement, ev: Event): any {
    const lineId: string = this.value;
    // Disable all other lines
    for (const otherLine in lineEles) {
        if (otherLine !== lineId) {
            lineEles[otherLine].checked = false;
        }
    }
    currentLine = this.checked ? lineId : null;
    redrawMap();
}

function redrawMap(): void {
    if (data === null) return;

    map.removeLayer(linesLayer);
    linesLayer = L.layerGroup().addTo(map);

    const activeLines: BusLine[] = currentLine === null ? [] : (currentLine === "all" ? Object.keys(data.lines).map(k => data!.lines[k]) : [data.lines[currentLine]]);

    // Calculate max usage
    const maxUsage = activeLines
        .map(l => l.lineSections
            .map(ls => ls.usage
                .filter(isValidUsageValue)
                .map(u => u.busesPerHour)
                .reduce((maxBph, current) => Math.max(maxBph, current), 0))
            .reduce((maxBph, current) => Math.max(maxBph, current), 0)
        ).reduce((maxBph, current) => Math.max(maxBph, current), 0);

    for (const line of activeLines) {
        for (const lineSection of line.lineSections) {
            // Get lateness values for both end of the line
            let fromTotal = 0;
            let fromLateness = lineSection.fromStopPoint.latenessValues
                .filter(dl => dl.date.getDay() != 0 && dl.date.getDay() != 6 && dl.hour >= 12 && dl.hour < 18)
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
                .filter(dl => dl.date.getDay() != 0 && dl.date.getDay() != 6 && dl.hour >= 12 && dl.hour < 18)
                .map(dl => dl.lateness)
                .reduce((accumulator, current) => {
                    if (current !== null) {
                        toTotal++;
                        return (accumulator ?? 0) + current;
                    }
                    return accumulator;
                }, 0);

            // Calculate average lateness
            if (fromTotal === 0 || toTotal === 0) {
                continue; // No data on both sides
            }
            fromLateness = fromTotal === 0 ? null : (fromLateness ?? 0) / fromTotal;
            toLateness = toTotal === 0 ? null : (toLateness ?? 0) / toTotal;

            // Calculate average usage
            const busPerHoursEntries = lineSection.usage
                .filter(isValidUsageValue)
                .map(ele => ele.busesPerHour);
            const averageUsage = busPerHoursEntries.reduce((accumulator, current) => accumulator + current, 0) / busPerHoursEntries.length;
            if (averageUsage === 0) {
                continue; // This section never gets used
            }

            // Generate lines on the map
            for (let i = 0; i < lineSection.track.length - 1; i++) {
                let colour: string | null = null;
                if (fromLateness !== null && toLateness !== null) {
                    let proportion = (i + 1) / (lineSection.track.length + 1);
                    colour = latenessToColour(((1 - proportion) * fromLateness) + (proportion * toLateness));
                } else if (fromLateness !== null) {
                    colour = latenessToColour(fromLateness, );
                } else if (toLateness !== null) {
                    colour = latenessToColour(toLateness);
                }
                L.polyline(
                    lineSection.track
                        .slice(i, i + 2)
                        .map(track => L.latLng(track.latitude, track.longitude)),
                    {
                        color: colour ?? "black",
                        opacity: usageToOpacity(averageUsage, maxUsage),
                        weight: 6,
                        offset: -3,
                        lineCap: "butt"
                    }).addTo(linesLayer);
            }
        }
    }
}

function latenessToColour(lateness: number): string {
    const GRN = 0;
    const RED = 30;
    const hue = Math.max(0, Math.min(120, 120 - ((lateness - GRN) / (RED - GRN)) * 120));
    return `hsl(${hue}, 100%, 50%)`;
}

function usageToOpacity(averageUsage: number, maxUsage: number): number {
    return (averageUsage / maxUsage) * 0.9;
}

function isValidUsageValue(usage: DataLineUsage): boolean {
    return usage.hour >= 12 && usage.hour < 18 && (
        usage.dayOfWeek === DayOfWeek.Monday ||
        usage.dayOfWeek === DayOfWeek.Tuesday ||
        usage.dayOfWeek === DayOfWeek.Wednesday ||
        usage.dayOfWeek === DayOfWeek.Thursday ||
        usage.dayOfWeek === DayOfWeek.Friday);
}

init();