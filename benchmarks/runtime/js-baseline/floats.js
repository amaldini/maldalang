// Hand-written JavaScript twin of floats.malda (baseline for the js backend).
let x = 0.0;
let v = 1.0;
const dt = 0.001;
let energy = 0.0;
let i = 0;
while (i < 2000000) {
  const a = -x;
  v = v + a * dt;
  x = x + v * dt;
  energy = energy + (x * x + v * v) * 0.5;
  i++;
}
console.log(Math.round(energy));
