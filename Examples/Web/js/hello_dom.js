const MaldaApp = (() => {
    if (typeof globalThis.mlRuntime === "undefined") {
        throw new Error("mlRuntime is not available. Include malda-js-runtime.js before running generated MALDA JavaScript.");
    }
    const mlRuntime = globalThis.mlRuntime;

    function main() {
        let root = mlRuntime.dom.query("#app");
        if (mlRuntime.isTruthy(mlRuntime.equals(root, null)))
        {
            mlRuntime.builtins.println("No #app container found.");
        }
        else
        {
            mlRuntime.dom.clear(root);
            function addParagraph(parent, textValue) {
                let p = mlRuntime.dom.create("p");
                mlRuntime.dom.setText(p, textValue);
                mlRuntime.dom.append(parent, p);
            }
            let title = mlRuntime.dom.create("h1");
            mlRuntime.dom.setText(title, "Hello from MALDA JS backend");
            mlRuntime.dom.append(root, title);
            addParagraph(root, "This page is running MALDA code compiled to JavaScript.");
            let note = mlRuntime.dom.create("div");
            mlRuntime.dom.html(note, "Rendered via <strong>dom.query/create/append/clear/setText/html/on</strong> runtime helpers.");
            mlRuntime.dom.append(root, note);
        }
    }

    return { main };
})();

if (typeof module !== "undefined" && module.exports) {
    module.exports = MaldaApp;
}

if (typeof require !== "undefined" && require.main === module) {
    MaldaApp.main();
}
