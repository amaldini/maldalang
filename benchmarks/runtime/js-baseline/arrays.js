// Hand-written JavaScript twin of arrays.malda (baseline for the js backend).
const n = 2000;
let grid = [];
let next = [];
for (let i = 0; i < n; i++) {
  grid.push(Math.sin(i * 0.01));
  next.push(0.0);
}
for (let pass = 0; pass < 500; pass++) {
  for (let k = 1; k < n - 1; k++) {
    next[k] = (grid[k - 1] + grid[k] * 2.0 + grid[k + 1]) * 0.25;
  }
  const swap = grid;
  grid = next;
  next = swap;
}
let sum = 0.0;
for (let j = 0; j < n; j++) {
  sum += grid[j] * grid[j];
}
console.log(Math.round(sum * 1000));
