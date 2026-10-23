// Hand-written JavaScript twin of loops.malda (baseline for the js backend).
let total = 0;
for (let i = 0; i < 3000; i++) {
  for (let j = 0; j < 1000; j++) {
    if ((i + j) % 3 === 0) {
      total++;
    }
  }
}
console.log(total);
