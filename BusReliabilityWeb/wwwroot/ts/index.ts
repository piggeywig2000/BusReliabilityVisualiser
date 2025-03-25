interface Window {
    AppConfig: {
        STADIA_API_KEY: string;
    };
}

// Create map
let map: L.Map = L.map("map").setView([51.3776019, -2.3567216], 14);

//L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
//    maxZoom: 19,
//    attribution: `&copy; <a href="http://www.openstreetmap.org/copyright">OpenStreetMap</a>`,
//    className: "tile-layer-greyscale"
//}).addTo(map); // OpenStreetMap layer

L.tileLayer(`https://tiles.stadiamaps.com/tiles/alidade_smooth/{z}/{x}/{y}{r}.png?api_key=${window.AppConfig.STADIA_API_KEY}`, {
    maxZoom: 20,
    minZoom: 12,
    attribution: `&copy; <a href="https://stadiamaps.com/" target="_blank">Stadia Maps</a>, &copy; <a href="https://openmaptiles.org/" target="_blank">OpenMapTiles</a> &copy; <a href="https://www.openstreetmap.org/copyright" target="_blank">OpenStreetMap</a>`,
}).addTo(map); // Stadia maps layer