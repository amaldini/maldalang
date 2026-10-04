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
            let title = mlRuntime.dom.create("h1");
            mlRuntime.dom.setText(title, "Towers of Hanoi");
            mlRuntime.dom.append(root, title);
            let hint = mlRuntime.dom.create("p");
            mlRuntime.dom.setText(hint, "Click a disk to select it, then click a tower to move it. Press A to auto-solve, R to reset.");
            mlRuntime.dom.append(root, hint);
            let canvasWidth = 900;
            let canvasHeight = 500;
            let numDisks = 4;
            let towers = [[4, 3, 2, 1], [], []];
            let towerNames = ["A", "B", "C"];
            let selectedTower = (-mlRuntime.coerceToFloat(1));
            let moveCount = 0;
            let gameWon = false;
            let autoSolving = false;
            let autoMoves = [];
            let autoMoveDelay = 600;
            let autoMoveTimer = 0;
            let towerX = [150, 450, 750];
            let towerWidth = 20;
            let towerHeight = 300;
            let towerBaseY = 400;
            let towerBaseWidth = 180;
            let towerBaseHeight = 20;
            let diskHeight = 25;
            let diskMinWidth = 40;
            let diskWidthStep = 30;
            let diskColors = ["#ff6b6b", "#4ecdc4", "#45b7d1", "#96ceb4", "#ffeaa7", "#dfe6e9"];
            function resetGame() {
                towers = [[4, 3, 2, 1], [], []];
                if (mlRuntime.isTruthy(mlRuntime.equals(numDisks, 3)))
                {
                    towers = [[3, 2, 1], [], []];
                }
                if (mlRuntime.isTruthy(mlRuntime.equals(numDisks, 5)))
                {
                    towers = [[5, 4, 3, 2, 1], [], []];
                }
                selectedTower = (-mlRuntime.coerceToFloat(1));
                moveCount = 0;
                gameWon = false;
                autoSolving = false;
                autoMoves = [];
                autoMoveTimer = 0;
            }
            function getDiskWidth(diskSize) {
                return (diskMinWidth + (mlRuntime.coerceToFloat(diskSize) * mlRuntime.coerceToFloat(diskWidthStep)));
            }
            function getDiskY(towerIndex, positionInTower) {
                return (mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(towerBaseY) - mlRuntime.coerceToFloat(towerBaseHeight))) - mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(positionInTower) * mlRuntime.coerceToFloat(diskHeight))));
            }
            function getDiskRect(towerIndex, positionInTower, diskSize) {
                let diskW = getDiskWidth(diskSize);
                let diskX = (mlRuntime.coerceToFloat(towerX[towerIndex]) - mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(diskW) / mlRuntime.coerceToFloat(2))));
                let diskY = getDiskY(towerIndex, positionInTower);
                return { ["x"]: diskX, ["y"]: diskY, ["w"]: diskW, ["h"]: diskHeight };
            }
            function isPointInRect(px, py, rect) {
                return (mlRuntime.isTruthy((mlRuntime.isTruthy((mlRuntime.isTruthy((mlRuntime.coerceToFloat(px) >= mlRuntime.coerceToFloat(rect.x))) && mlRuntime.isTruthy((mlRuntime.coerceToFloat(px) <= mlRuntime.coerceToFloat((rect.x + rect.w)))))) && mlRuntime.isTruthy((mlRuntime.coerceToFloat(py) >= mlRuntime.coerceToFloat(rect.y))))) && mlRuntime.isTruthy((mlRuntime.coerceToFloat(py) <= mlRuntime.coerceToFloat((rect.y + rect.h)))));
            }
            function getTowerAtX(x) {
                let i = 0;
                while (mlRuntime.isTruthy((mlRuntime.coerceToFloat(i) < mlRuntime.coerceToFloat(3))))
                {
                    let towerLeft = (mlRuntime.coerceToFloat(towerX[i]) - mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(towerBaseWidth) / mlRuntime.coerceToFloat(2))));
                    let towerRight = (towerX[i] + (mlRuntime.coerceToFloat(towerBaseWidth) / mlRuntime.coerceToFloat(2)));
                    if (mlRuntime.isTruthy((mlRuntime.isTruthy((mlRuntime.coerceToFloat(x) >= mlRuntime.coerceToFloat(towerLeft))) && mlRuntime.isTruthy((mlRuntime.coerceToFloat(x) <= mlRuntime.coerceToFloat(towerRight))))))
                    {
                        return i;
                    }
                    i = (i + 1);
                }
                return (-mlRuntime.coerceToFloat(1));
            }
            function canMoveDisk(fromTower, toTower) {
                if (mlRuntime.isTruthy((mlRuntime.isTruthy((mlRuntime.isTruthy((mlRuntime.isTruthy((mlRuntime.coerceToFloat(fromTower) < mlRuntime.coerceToFloat(0))) || mlRuntime.isTruthy((mlRuntime.coerceToFloat(fromTower) >= mlRuntime.coerceToFloat(3))))) || mlRuntime.isTruthy((mlRuntime.coerceToFloat(toTower) < mlRuntime.coerceToFloat(0))))) || mlRuntime.isTruthy((mlRuntime.coerceToFloat(toTower) >= mlRuntime.coerceToFloat(3))))))
                {
                    return false;
                }
                let sourceTower = towers[fromTower];
                if (mlRuntime.isTruthy(mlRuntime.equals(sourceTower.length, 0)))
                {
                    return false;
                }
                let destTower = towers[toTower];
                let movingDisk = sourceTower[(mlRuntime.coerceToFloat(sourceTower.length) - mlRuntime.coerceToFloat(1))];
                if (mlRuntime.isTruthy(mlRuntime.equals(destTower.length, 0)))
                {
                    return true;
                }
                let topDisk = destTower[(mlRuntime.coerceToFloat(destTower.length) - mlRuntime.coerceToFloat(1))];
                return (mlRuntime.coerceToFloat(movingDisk) < mlRuntime.coerceToFloat(topDisk));
            }
            function moveDisk(fromTower, toTower) {
                if (mlRuntime.isTruthy((!mlRuntime.isTruthy(canMoveDisk(fromTower, toTower)))))
                {
                    return false;
                }
                let sourceTower = towers[fromTower];
                let disk = mlRuntime.callArrayMethod(sourceTower, "pop");
                let destTower = towers[toTower];
                mlRuntime.arrayAppend(destTower, disk);
                towers[fromTower] = sourceTower;
                towers[toTower] = destTower;
                moveCount = (moveCount + 1);
                let goalTower = towers[2];
                if (mlRuntime.isTruthy(mlRuntime.equals(goalTower.length, numDisks)))
                {
                    gameWon = true;
                }
                return true;
            }
            function generateSolution(n, from, to, aux) {
                if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(n) <= mlRuntime.coerceToFloat(0))))
                {
                    return [];
                }
                if (mlRuntime.isTruthy(mlRuntime.equals(n, 1)))
                {
                    return [{ ["from"]: from, ["to"]: to }];
                }
                let moves1 = generateSolution((mlRuntime.coerceToFloat(n) - mlRuntime.coerceToFloat(1)), from, aux, to);
                let mainMove = [{ ["from"]: from, ["to"]: to }];
                let moves2 = generateSolution((mlRuntime.coerceToFloat(n) - mlRuntime.coerceToFloat(1)), aux, to, from);
                return mlRuntime.callArrayMethod(mlRuntime.callArrayMethod(moves1, "concat", [mainMove]), "concat", [moves2]);
            }
            function startAutoSolve() {
                autoSolving = true;
                autoMoves = generateSolution(numDisks, 0, 2, 1);
                autoMoveTimer = 0;
            }
            function updateGame(dtMs) {
                if (mlRuntime.isTruthy(mlRuntime.game.wasKeyPressed("r")))
                {
                    resetGame();
                }
                if (mlRuntime.isTruthy((mlRuntime.isTruthy((mlRuntime.isTruthy(mlRuntime.game.wasKeyPressed("a")) && mlRuntime.isTruthy((!mlRuntime.isTruthy(autoSolving))))) && mlRuntime.isTruthy((!mlRuntime.isTruthy(gameWon))))))
                {
                    startAutoSolve();
                }
                if (mlRuntime.isTruthy(mlRuntime.game.wasKeyPressed("h")))
                {
                    if (mlRuntime.isTruthy(mlRuntime.equals(numDisks, 3)))
                    {
                        numDisks = 4;
                    }
                    else
                    {
                        if (mlRuntime.isTruthy(mlRuntime.equals(numDisks, 4)))
                        {
                            numDisks = 5;
                        }
                        else
                        {
                            numDisks = 3;
                        }
                    }
                    resetGame();
                }
                if (mlRuntime.isTruthy((mlRuntime.isTruthy(autoSolving) && mlRuntime.isTruthy((mlRuntime.coerceToFloat(mlRuntime.str.length(autoMoves)) > mlRuntime.coerceToFloat(0))))))
                {
                    autoMoveTimer = (autoMoveTimer + dtMs);
                    if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(autoMoveTimer) >= mlRuntime.coerceToFloat(autoMoveDelay))))
                    {
                        let nextMove = autoMoves[0];
                        moveDisk(nextMove.from, nextMove.to);
                        autoMoves = mlRuntime.callArrayMethod(autoMoves, "slice", [1, mlRuntime.str.length(autoMoves)]);
                        autoMoveTimer = 0;
                        if (mlRuntime.isTruthy(mlRuntime.equals(mlRuntime.str.length(autoMoves), 0)))
                        {
                            autoSolving = false;
                        }
                    }
                }
                if (mlRuntime.isTruthy((mlRuntime.isTruthy((!mlRuntime.isTruthy(autoSolving))) && mlRuntime.isTruthy(mlRuntime.game.wasMousePressed(0)))))
                {
                    let mx = mlRuntime.game.getMouseX();
                    let my = mlRuntime.game.getMouseY();
                    let clickedTower = getTowerAtX(mx);
                    if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(clickedTower) >= mlRuntime.coerceToFloat(0))))
                    {
                        if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(selectedTower) < mlRuntime.coerceToFloat(0))))
                        {
                            if (mlRuntime.isTruthy((mlRuntime.coerceToFloat(towers[clickedTower].length) > mlRuntime.coerceToFloat(0))))
                            {
                                selectedTower = clickedTower;
                            }
                        }
                        else
                        {
                            if (mlRuntime.isTruthy(mlRuntime.equals(clickedTower, selectedTower)))
                            {
                                selectedTower = (-mlRuntime.coerceToFloat(1));
                            }
                            else
                            {
                                let success = moveDisk(selectedTower, clickedTower);
                                selectedTower = (-mlRuntime.coerceToFloat(1));
                            }
                        }
                    }
                }
            }
            function renderGame() {
                mlRuntime.game.clear();
                let i = 0;
                while (mlRuntime.isTruthy((mlRuntime.coerceToFloat(i) < mlRuntime.coerceToFloat(3))))
                {
                    let baseX = (mlRuntime.coerceToFloat(towerX[i]) - mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(towerBaseWidth) / mlRuntime.coerceToFloat(2))));
                    mlRuntime.game.fillRect(baseX, towerBaseY, towerBaseWidth, towerBaseHeight, "#8b7355");
                    let poleX = (mlRuntime.coerceToFloat(towerX[i]) - mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(towerWidth) / mlRuntime.coerceToFloat(2))));
                    let poleY = (mlRuntime.coerceToFloat(towerBaseY) - mlRuntime.coerceToFloat(towerHeight));
                    mlRuntime.game.fillRect(poleX, poleY, towerWidth, towerHeight, "#654321");
                    mlRuntime.game.drawText(towerNames[i], (mlRuntime.coerceToFloat(towerX[i]) - mlRuntime.coerceToFloat(8)), ((towerBaseY + towerBaseHeight) + 25), "#ffffff", "20px sans-serif");
                    i = (i + 1);
                }
                let t = 0;
                while (mlRuntime.isTruthy((mlRuntime.coerceToFloat(t) < mlRuntime.coerceToFloat(3))))
                {
                    let tower = towers[t];
                    let pos = 0;
                    while (mlRuntime.isTruthy((mlRuntime.coerceToFloat(pos) < mlRuntime.coerceToFloat(tower.length))))
                    {
                        let diskSize = tower[pos];
                        let rect = getDiskRect(t, pos, diskSize);
                        let color = diskColors[(mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(diskSize) - mlRuntime.coerceToFloat(1))) % mlRuntime.coerceToFloat(mlRuntime.str.length(diskColors)))];
                        if (mlRuntime.isTruthy((mlRuntime.isTruthy(mlRuntime.equals(t, selectedTower)) && mlRuntime.isTruthy(mlRuntime.equals(pos, (mlRuntime.coerceToFloat(tower.length) - mlRuntime.coerceToFloat(1)))))))
                        {
                            color = "#ffff00";
                        }
                        mlRuntime.game.fillRect(rect.x, rect.y, rect.w, rect.h, color);
                        mlRuntime.game.drawText(mlRuntime.coerceToString(diskSize), (mlRuntime.coerceToFloat((rect.x + (mlRuntime.coerceToFloat(rect.w) / mlRuntime.coerceToFloat(2)))) - mlRuntime.coerceToFloat(6)), (rect.y + 18), "#000000", "14px bold sans-serif");
                        pos = (pos + 1);
                    }
                    t = (t + 1);
                }
                let statusY = 30;
                mlRuntime.game.drawText(("Moves: " + mlRuntime.coerceToString(moveCount)), 20, statusY, "#ffffff", "18px sans-serif");
                let minMoves = expectedMoves(numDisks);
                mlRuntime.game.drawText(("Minimum: " + mlRuntime.coerceToString(minMoves)), 20, (statusY + 25), "#aaaaaa", "14px sans-serif");
                if (mlRuntime.isTruthy(gameWon))
                {
                    let msg = (("You Won in " + mlRuntime.coerceToString(moveCount)) + " moves!");
                    mlRuntime.game.drawText(msg, (mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(canvasWidth) / mlRuntime.coerceToFloat(2))) - mlRuntime.coerceToFloat(120)), 80, "#00ff00", "24px bold sans-serif");
                    if (mlRuntime.isTruthy(mlRuntime.equals(moveCount, minMoves)))
                    {
                        mlRuntime.game.drawText("Perfect!", (mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(canvasWidth) / mlRuntime.coerceToFloat(2))) - mlRuntime.coerceToFloat(50)), 110, "#ffff00", "20px bold sans-serif");
                    }
                }
                if (mlRuntime.isTruthy(autoSolving))
                {
                    mlRuntime.game.drawText("Auto-solving...", (mlRuntime.coerceToFloat((mlRuntime.coerceToFloat(canvasWidth) / mlRuntime.coerceToFloat(2))) - mlRuntime.coerceToFloat(70)), 80, "#ffaa00", "20px sans-serif");
                }
                mlRuntime.game.drawText((("Controls: A=Auto-solve, R=Reset, H=Change disks (" + mlRuntime.coerceToString(numDisks)) + ")"), 20, (mlRuntime.coerceToFloat(canvasHeight) - mlRuntime.coerceToFloat(20)), "#888888", "14px monospace");
            }
            function expectedMoves(n) {
                let total = 1;
                let i = 0;
                while (mlRuntime.isTruthy((mlRuntime.coerceToFloat(i) < mlRuntime.coerceToFloat(n))))
                {
                    total = (mlRuntime.coerceToFloat(total) * mlRuntime.coerceToFloat(2));
                    i = (i + 1);
                }
                return (mlRuntime.coerceToFloat(total) - mlRuntime.coerceToFloat(1));
            }
            mlRuntime.game.createCanvas(canvasWidth, canvasHeight, "#app");
            mlRuntime.game.setBackground("#1a1a2e");
            mlRuntime.game.start(updateGame, renderGame);
        }
    }

    return { main };
})();

if (typeof globalThis !== "undefined") {
    globalThis.MaldaApp = MaldaApp;
}

if (typeof module !== "undefined" && module.exports) {
    module.exports = MaldaApp;
}

async function __maldaRunMain() {
    try {
        await MaldaApp.main();
    } finally {
        if (mlRuntime.actors && typeof mlRuntime.actors.shutdownAsync === "function") {
            await mlRuntime.actors.shutdownAsync();
        }
    }
}

if (typeof require !== "undefined" && require.main === module) {
    __maldaRunMain().catch((error) => {
        throw error;
    });
}
//# sourceMappingURL=towers_of_hanoi.js.map
