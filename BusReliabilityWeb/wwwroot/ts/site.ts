for (const helpButton of document.getElementsByClassName("button-help") as HTMLCollectionOf<HTMLButtonElement>) {
    helpButton.addEventListener("click", () => {
        const contentsToInsert = helpButton.querySelector("template") as HTMLTemplateElement;
        const clonedContent = contentsToInsert.content.cloneNode(true);
        const popupContent = document.getElementById("popup-content") as HTMLDivElement;
        popupContent.replaceChildren(...clonedContent.childNodes);
        document.getElementById("popup-background")?.style.removeProperty("display");
    });
}