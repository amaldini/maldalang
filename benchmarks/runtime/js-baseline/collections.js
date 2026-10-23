// Hand-written JavaScript twin of collections.malda (baseline for the js backend).
const items = [];
for (let i = 0; i < 200000; i++) {
  items.push((i * 2) % 1000);
}
let sum = 0;
for (let k = 0; k < items.length; k++) {
  sum = sum + items[k];
}
const counts = {};
for (const n of items) {
  const key = "k" + (n % 10);
  if (counts[key] == null) {
    counts[key] = 0;
  }
  counts[key] = counts[key] + 1;
}
console.log(sum);
console.log(counts["k4"]);
