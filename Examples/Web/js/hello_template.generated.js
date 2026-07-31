const MaldaApp = (() => {
    if (typeof globalThis.mlRuntime === "undefined") {
        throw new Error("mlRuntime is not available. Include malda-js-runtime.js before running generated MALDA JavaScript.");
    }
    const mlRuntime = globalThis.mlRuntime;

    function renderRoot(rootSelector) {
        let __maldaTemplateHtml = "";
        __maldaTemplateHtml = (__maldaTemplateHtml + "<div class=\"hello-template\">\n  <h1>Hello from template mode</h1>\n  <p>User: ");
        __maldaTemplateHtml = (__maldaTemplateHtml + "Andrea");
        __maldaTemplateHtml = (__maldaTemplateHtml + "</p>\n  ");
        let count = 2;
        __maldaTemplateHtml = (__maldaTemplateHtml + "\n  <p>Count value: ");
        __maldaTemplateHtml = (__maldaTemplateHtml + count);
        __maldaTemplateHtml = (__maldaTemplateHtml + "</p>\n</div>\n");
        mlRuntime.dom.html(rootSelector, __maldaTemplateHtml);
    }

    function bootstrap(rootSelector) {
        if (mlRuntime.isTruthy((mlRuntime.isTruthy(mlRuntime.equals(rootSelector, null)) || mlRuntime.isTruthy(mlRuntime.equals(rootSelector, "")))))
        {
            rootSelector = "#app";
        }
        renderRoot(rootSelector);
    }

    function main() {
    }

    return { main, renderRoot, bootstrap };
})();

if (typeof module !== "undefined" && module.exports) {
    module.exports = MaldaApp;
}

if (typeof require !== "undefined" && require.main === module) {
    MaldaApp.main();
}
